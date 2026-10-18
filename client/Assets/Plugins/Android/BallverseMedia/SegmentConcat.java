package com.ballverse.media;

import android.media.MediaCodec;
import android.media.MediaExtractor;
import android.media.MediaFormat;
import android.media.MediaMuxer;
import android.os.Build;
import android.util.Log;

import java.io.File;
import java.io.IOException;
import java.nio.ByteBuffer;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Segmented export (spec §3.8, §7.5): joins video-only MP4 segments written by {@link VideoWriter} without re-encoding
 * (MediaExtractor → MediaMuxer) and encodes the PCM mix into ONE AAC-LC track ({@link AacPcmEncoder}) muxed alongside.
 * C# moves moov to the front and adds the title/date afterwards (Mp4FastStart).
 *
 * Timing: the samples of segment k are placed at pts − firstPts(k) + segmentStartUs(frames before k, fps), where
 * firstPts(k) is the pts of the segment's first (sync) sample. Same formula as C# SegmentConcatOptions.SegmentStartUs.
 *
 * All segments must share the codec configuration (MIME, size, csd-0/csd-1 = SPS/PPS); a mismatch fails with
 * INVALID_STATE because one avcC sample entry cannot describe both. Runs on a MediaBridge executor thread.
 */
final class SegmentConcat {
    private static final String TAG = MediaBridge.TAG;
    private static final int MIN_SAMPLE_BUFFER = 1 << 20;

    private final String[] mSegments;
    private final String mPcmPath;
    private final String mOutPath;
    private final int mFps;
    private final int mAudioBitrate;
    private final AtomicBoolean mCancel;
    private final long mRequestId;

    private MediaMuxer mMuxer;
    private AacPcmEncoder mAudio;
    private int mVideoTrack = -1;
    private int mAudioTrack = -1;
    private boolean mHasAudio;
    private boolean mMuxerStarted;
    private final List<Object[]> mPending = new ArrayList<>(); // {track, ByteBuffer copy, BufferInfo}

    /** Output start time of a segment preceded by framesBefore frames: framesBefore·10⁶/fps rounded up. */
    static long segmentStartUs(long framesBefore, int fps) {
        return (framesBefore * 1000000L + fps - 1) / fps;
    }

    private SegmentConcat(String[] segments, String pcmPath, String outPath, int fps, int audioBitrate, AtomicBoolean cancel, long requestId) {
        mSegments = segments;
        mPcmPath = pcmPath;
        mOutPath = outPath;
        mFps = fps;
        mAudioBitrate = audioBitrate;
        mCancel = cancel;
        mRequestId = requestId;
    }

    /** Result JSON {"frames","durationUs"} (C# NativeWriterDto). Deletes the output on any failure or cancellation. */
    static String run(String[] segments, String pcmPath, String outPath, int fps, int audioBitrate, AtomicBoolean cancel, long requestId)
            throws MediaError, IOException {
        if (segments == null || segments.length == 0) throw new MediaError(MediaError.INVALID_STATE, "no segments");
        if (fps <= 0) throw new MediaError(MediaError.INVALID_STATE, "fps must be positive");
        return new SegmentConcat(segments, pcmPath, outPath, fps, audioBitrate, cancel, requestId).execute();
    }

    private String execute() throws MediaError, IOException {
        File out = new File(mOutPath);
        File dir = out.getParentFile();
        if (dir != null && !dir.isDirectory() && !dir.mkdirs()) throw new MediaError(MediaError.IO, "cannot create " + dir);
        if (out.exists() && !out.delete()) throw new MediaError(MediaError.IO, "cannot replace " + out);
        boolean ok = false;
        try {
            String json = concat();
            ok = true;
            return json;
        } finally {
            release();
            if (!ok && out.exists() && !out.delete()) Log.w(TAG, "cannot delete " + out);
        }
    }

    private String concat() throws MediaError, IOException {
        mMuxer = new MediaMuxer(mOutPath, MediaMuxer.OutputFormat.MUXER_OUTPUT_MPEG_4);
        mHasAudio = AacPcmEncoder.frameCount(mPcmPath) > 0;
        if (mHasAudio) {
            mAudio = new AacPcmEncoder(mPcmPath, mAudioBitrate, new AudioSink());
            mAudio.open();
        }
        MediaFormat reference = null;
        ByteBuffer buffer = null;
        MediaCodec.BufferInfo info = new MediaCodec.BufferInfo();
        long total = 0;
        for (int s = 0; s < mSegments.length; s++) {
            checkCancel();
            MediaExtractor extractor = new MediaExtractor();
            try {
                try {
                    extractor.setDataSource(mSegments[s]);
                } catch (IOException e) {
                    throw new MediaError(MediaError.CORRUPT, "cannot read segment " + s + ": " + mSegments[s], e);
                }
                int track = videoTrackOf(extractor);
                if (track < 0) throw new MediaError(MediaError.NO_VIDEO_TRACK, "segment " + s + " has no video track");
                MediaFormat format = extractor.getTrackFormat(track);
                if (reference == null) {
                    reference = format;
                    mVideoTrack = mMuxer.addTrack(format);
                    maybeStartMuxer();
                } else {
                    checkSameConfig(reference, format, s);
                }
                extractor.selectTrack(track);
                if (buffer == null) buffer = ByteBuffer.allocateDirect(initialBufferSize(format));

                long base = segmentStartUs(total, mFps);
                long first = Long.MIN_VALUE;
                long count = 0;
                while (true) {
                    checkCancel();
                    if (Build.VERSION.SDK_INT >= 28) {
                        long needed = extractor.getSampleSize();
                        if (needed > buffer.capacity()) buffer = ByteBuffer.allocateDirect((int) needed);
                    }
                    buffer.clear();
                    int size = extractor.readSampleData(buffer, 0);
                    if (size < 0) break;
                    long t = extractor.getSampleTime();
                    int flags = extractor.getSampleFlags();
                    boolean sync = (flags & MediaExtractor.SAMPLE_FLAG_SYNC) != 0;
                    if (first == Long.MIN_VALUE) {
                        if (!sync) throw new MediaError(MediaError.INVALID_STATE, "segment " + s + " does not start with a sync frame");
                        first = t;
                    }
                    long outPts = t - first + base;
                    // Audio never runs ahead of the video (queued samples cannot be withdrawn): up to this sample's
                    // start, which lies inside the output; finish(endUs) queues the rest and trims longer PCM there.
                    if (mAudio != null) mAudio.pump(outPts, false);
                    info.set(0, size, outPts, sync ? MediaCodec.BUFFER_FLAG_KEY_FRAME : 0);
                    write(mVideoTrack, buffer, info);
                    count++;
                    extractor.advance();
                }
                if (count == 0) throw new MediaError(MediaError.CORRUPT, "segment " + s + " has no video samples");
                total += count;
            } finally {
                extractor.release();
            }
            MediaBridge.progress(mRequestId, (double) (s + 1) / (mSegments.length + 1));
        }
        long endUs = segmentStartUs(total, mFps);
        if (mAudio != null) mAudio.finish(endUs);
        if (!mMuxerStarted) throw new MediaError(MediaError.MUXER_FAILED, "muxer never started (no audio output)");
        mMuxer.stop();
        return new Json().put("frames", total).put("durationUs", endUs).toString();
    }

    private void checkCancel() throws MediaError {
        if (mCancel.get()) throw new MediaError(MediaError.INVALID_STATE, "cancelled");
    }

    private static int videoTrackOf(MediaExtractor extractor) {
        for (int i = 0; i < extractor.getTrackCount(); i++) {
            String mime = extractor.getTrackFormat(i).getString(MediaFormat.KEY_MIME);
            if (mime != null && mime.startsWith("video/")) return i;
        }
        return -1;
    }

    private static int initialBufferSize(MediaFormat f) {
        int w = MediaProbe.intOr(f, MediaFormat.KEY_WIDTH, 1920);
        int h = MediaProbe.intOr(f, MediaFormat.KEY_HEIGHT, 1080);
        int maxInput = MediaProbe.intOr(f, MediaFormat.KEY_MAX_INPUT_SIZE, 0);
        return Math.max(MIN_SAMPLE_BUFFER, Math.max(maxInput, w * h * 3 / 2));
    }

    private static void checkSameConfig(MediaFormat a, MediaFormat b, int segment) throws MediaError {
        String mimeA = a.getString(MediaFormat.KEY_MIME);
        String mimeB = b.getString(MediaFormat.KEY_MIME);
        boolean same = mimeA != null && mimeA.equals(mimeB)
                && MediaProbe.intOr(a, MediaFormat.KEY_WIDTH, -1) == MediaProbe.intOr(b, MediaFormat.KEY_WIDTH, -2)
                && MediaProbe.intOr(a, MediaFormat.KEY_HEIGHT, -1) == MediaProbe.intOr(b, MediaFormat.KEY_HEIGHT, -2)
                && sameBuffer(a, b, "csd-0") && sameBuffer(a, b, "csd-1");
        if (!same) throw new MediaError(MediaError.INVALID_STATE, "segment " + segment + " has a different codec configuration than segment 0");
    }

    private static boolean sameBuffer(MediaFormat a, MediaFormat b, String key) {
        boolean hasA = a.containsKey(key);
        if (hasA != b.containsKey(key)) return false;
        if (!hasA) return true;
        ByteBuffer x = a.getByteBuffer(key);
        ByteBuffer y = b.getByteBuffer(key);
        if (x == null || y == null) return x == y;
        ByteBuffer dx = x.duplicate();
        ByteBuffer dy = y.duplicate();
        dx.rewind(); // Buffer.rewind() returns Buffer before Java 9: compare the ByteBuffers themselves
        dy.rewind();
        return dx.equals(dy);
    }

    // ------------------------------------------------------------------ muxer

    private final class AudioSink implements AacPcmEncoder.Sink {
        @Override
        public void onFormat(MediaFormat format) throws MediaError {
            if (mAudioTrack >= 0) throw new MediaError(MediaError.MUXER_FAILED, "audio format changed twice");
            mAudioTrack = mMuxer.addTrack(format);
            maybeStartMuxer();
        }

        @Override
        public void onSample(ByteBuffer data, MediaCodec.BufferInfo info) {
            write(mAudioTrack, data, info);
        }

        @Override
        public boolean cancelled() {
            return mCancel.get();
        }
    }

    private void maybeStartMuxer() {
        if (mMuxerStarted || mVideoTrack < 0 || (mHasAudio && mAudioTrack < 0)) return;
        mMuxer.start();
        mMuxerStarted = true;
        for (Object[] p : mPending) mMuxer.writeSampleData((Integer) p[0], (ByteBuffer) p[1], (MediaCodec.BufferInfo) p[2]);
        mPending.clear();
    }

    private void write(int track, ByteBuffer data, MediaCodec.BufferInfo info) {
        data.position(info.offset);
        data.limit(info.offset + info.size);
        if (!mMuxerStarted) {
            // Video before the AAC encoder reported its format: keep a copy until the muxer can start.
            ByteBuffer copy = ByteBuffer.allocate(info.size);
            copy.put(data);
            copy.flip();
            MediaCodec.BufferInfo bi = new MediaCodec.BufferInfo();
            bi.set(0, info.size, info.presentationTimeUs, info.flags);
            mPending.add(new Object[] {track, copy, bi});
            return;
        }
        mMuxer.writeSampleData(track, data, info);
    }

    private void release() {
        if (mAudio != null) {
            mAudio.release();
            mAudio = null;
        }
        if (mMuxer != null) {
            try {
                mMuxer.release();
            } catch (RuntimeException ignored) {
                // release() of a started-but-not-stopped muxer may throw; the file is deleted anyway
            }
            mMuxer = null;
        }
        mPending.clear();
    }
}
