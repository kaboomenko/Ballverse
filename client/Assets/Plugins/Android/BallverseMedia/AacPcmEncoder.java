package com.ballverse.media;

import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaFormat;

import java.io.File;
import java.io.FileInputStream;
import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.nio.channels.FileChannel;

/**
 * MediaCodec AAC-LC 48 kHz stereo encoder fed from a raw PCM file in the C# PcmFile format (f32le, interleaved stereo,
 * 48 kHz). Shared by {@link VideoWriter} (audio of a single-file export) and {@link SegmentConcat} (the one AAC track of
 * a segmented export). Encoded samples and the output format go to a {@link Sink} (normally a MediaMuxer track).
 *
 * Not thread-safe: driven from one thread (the writer / concat thread).
 */
final class AacPcmEncoder {
    static final int SAMPLE_RATE = 48000;
    static final int CHANNELS = 2;
    private static final String MIME_AAC = MediaFormat.MIMETYPE_AUDIO_AAC;
    private static final int CHUNK_FRAMES = 1024;
    private static final long DRAIN_TIMEOUT_US = 10000;
    private static final long STALL_LIMIT_NS = 10000000000L;

    /** Receives the encoder output. */
    interface Sink {
        /** The encoder's output format (once, before the first sample): add the muxer track here. */
        void onFormat(MediaFormat format) throws MediaError;

        /** One encoded AAC access unit (data positioned at info.offset, info.size bytes). */
        void onSample(ByteBuffer data, MediaCodec.BufferInfo info) throws MediaError;

        /** True when the owner was cancelled: the encoder stops with an error. */
        boolean cancelled();
    }

    private final String mPath;
    private final int mBitrate;
    private final Sink mSink;
    private final MediaCodec.BufferInfo mInfo = new MediaCodec.BufferInfo();
    private MediaCodec mCodec;
    private FileInputStream mStream;
    private FileChannel mPcm;
    private ByteBuffer mPcmBytes;
    private long mPcmFrames;
    private long mFramesQueued;
    private boolean mInputDone;
    private boolean mOutputDone;

    /** Frames in a PcmFile (0 if the path is empty or the file does not exist). */
    static long frameCount(String path) {
        if (path == null || path.length() == 0) return 0;
        File f = new File(path);
        return f.isFile() ? f.length() / (4L * CHANNELS) : 0;
    }

    AacPcmEncoder(String pcmPath, int bitrate, Sink sink) {
        mPath = pcmPath;
        mBitrate = bitrate;
        mSink = sink;
    }

    /** Configures and starts the codec and opens the PCM file. */
    void open() throws MediaError, IOException {
        mPcmFrames = frameCount(mPath);
        MediaFormat f = MediaFormat.createAudioFormat(MIME_AAC, SAMPLE_RATE, CHANNELS);
        f.setInteger(MediaFormat.KEY_AAC_PROFILE, MediaCodecInfo.CodecProfileLevel.AACObjectLC);
        f.setInteger(MediaFormat.KEY_BIT_RATE, mBitrate);
        f.setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, CHUNK_FRAMES * CHANNELS * 2 * 4);
        mCodec = MediaCodec.createEncoderByType(MIME_AAC);
        try {
            mCodec.configure(f, null, null, MediaCodec.CONFIGURE_FLAG_ENCODE);
        } catch (RuntimeException e) {
            throw new MediaError(MediaError.ENCODER_FAILED, "AAC encoder rejected 48 kHz stereo", e);
        }
        mStream = new FileInputStream(mPath);
        mPcm = mStream.getChannel();
        mPcmBytes = ByteBuffer.allocateDirect(CHUNK_FRAMES * CHANNELS * 4).order(ByteOrder.LITTLE_ENDIAN);
        mCodec.start();
    }

    static long ptsUs(long frames) {
        return frames * 1000000L / SAMPLE_RATE;
    }

    /** PCM frames covering [0, untilUs): untilUs in samples, rounded up to a whole frame. */
    static long framesUntil(long untilUs) {
        return untilUs <= 0 ? 0 : (untilUs * SAMPLE_RATE + 999999) / 1000000L;
    }

    /**
     * Queues PCM up to untilUs and never past it (chunks are cut at framesUntil(untilUs)): samples once queued cannot
     * be withdrawn and MediaMuxer writes no edit list, so callers pass a time KNOWN to lie inside the video (the end of
     * the frame just encoded), never a look-ahead; finish(endUs) then queues the rest up to the video end.
     * Not finishing: returns as soon as untilUs is reached or the codec has no free input buffer (call again later).
     * Finishing: queues everything up to untilUs (longer PCM is trimmed there), then end-of-stream.
     * PCM shorter than untilUs: end-of-stream as soon as it is exhausted.
     */
    void pump(long untilUs, boolean finishing) throws MediaError, IOException {
        long limitFrames = framesUntil(untilUs);
        long stallStart = System.nanoTime();
        while (!mInputDone) {
            if (mSink.cancelled()) throw new MediaError(MediaError.ENCODER_FAILED, "cancelled");
            if (!finishing && mFramesQueued >= limitFrames && mFramesQueued < mPcmFrames) return;
            int in = mCodec.dequeueInputBuffer(finishing ? DRAIN_TIMEOUT_US : 0);
            if (in < 0) {
                drain(false);
                if (!finishing) return;
                if (System.nanoTime() - stallStart > STALL_LIMIT_NS) throw new MediaError(MediaError.TIMEOUT, "audio encoder stalled");
                continue;
            }
            stallStart = System.nanoTime();
            ByteBuffer input = mCodec.getInputBuffer(in);
            if (input == null) throw new MediaError(MediaError.ENCODER_FAILED, "no AAC input buffer");
            input.clear();
            input.order(ByteOrder.nativeOrder());
            long n = Math.min(CHUNK_FRAMES, input.remaining() / (2 * CHANNELS));
            n = Math.min(n, mPcmFrames - mFramesQueued);
            n = Math.min(n, limitFrames - mFramesQueued);
            if (n <= 0) {
                mCodec.queueInputBuffer(in, 0, 0, ptsUs(mFramesQueued), MediaCodec.BUFFER_FLAG_END_OF_STREAM);
                mInputDone = true;
                break;
            }
            int samples = (int) n * CHANNELS;
            mPcmBytes.clear();
            mPcmBytes.limit(samples * 4);
            long position = mFramesQueued * CHANNELS * 4;
            while (mPcmBytes.hasRemaining()) {
                int read = mPcm.read(mPcmBytes, position + mPcmBytes.position());
                if (read < 0) break;
            }
            mPcmBytes.flip();
            int got = mPcmBytes.remaining() / 4;
            for (int i = 0; i < got; i++) {
                float v = mPcmBytes.getFloat();
                int s = Math.round(v * 32767f);
                input.putShort((short) (s > 32767 ? 32767 : (s < -32768 ? -32768 : s)));
            }
            int frames = got / CHANNELS;
            mCodec.queueInputBuffer(in, 0, frames * CHANNELS * 2, ptsUs(mFramesQueued), 0);
            mFramesQueued += frames;
            if (frames == 0) mPcmFrames = mFramesQueued; // file shrank: stop at the next iteration
            drain(false);
        }
    }

    /** Moves encoded output to the sink; with endOfStream waits until the codec signalled end of stream. */
    void drain(boolean endOfStream) throws MediaError {
        if (mOutputDone) return;
        long stallStart = System.nanoTime();
        while (true) {
            if (mSink.cancelled()) throw new MediaError(MediaError.ENCODER_FAILED, "cancelled");
            int index = mCodec.dequeueOutputBuffer(mInfo, endOfStream ? DRAIN_TIMEOUT_US : 0);
            if (index == MediaCodec.INFO_TRY_AGAIN_LATER) {
                if (!endOfStream) return;
                if (System.nanoTime() - stallStart > STALL_LIMIT_NS) throw new MediaError(MediaError.TIMEOUT, "audio encoder did not finish");
                continue;
            }
            if (index == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                mSink.onFormat(mCodec.getOutputFormat());
                continue;
            }
            if (index < 0) continue;
            stallStart = System.nanoTime();
            ByteBuffer data = mCodec.getOutputBuffer(index);
            if ((mInfo.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) != 0) mInfo.size = 0;
            if (mInfo.size > 0 && data != null) mSink.onSample(data, mInfo);
            boolean eos = (mInfo.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0;
            mCodec.releaseOutputBuffer(index, false);
            if (eos) {
                mOutputDone = true;
                return;
            }
        }
    }

    /** Queues the rest (trimmed to endUs) and waits for the last encoded sample. */
    void finish(long endUs) throws MediaError, IOException {
        pump(endUs, true);
        drain(true);
    }

    /** Stops and releases the codec and closes the PCM file; safe to call more than once. */
    void release() {
        if (mCodec != null) {
            try {
                mCodec.stop();
            } catch (RuntimeException ignored) {
                // not started or already failed
            }
            mCodec.release();
            mCodec = null;
        }
        if (mStream != null) {
            try {
                mStream.close();
            } catch (IOException ignored) {
                // nothing to do
            }
            mStream = null;
            mPcm = null;
        }
    }
}
