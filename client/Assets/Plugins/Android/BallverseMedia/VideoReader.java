package com.ballverse.media;

import android.graphics.ImageFormat;
import android.graphics.Rect;
import android.media.Image;
import android.media.ImageReader;
import android.media.MediaCodec;
import android.media.MediaExtractor;
import android.media.MediaFormat;
import android.os.Build;
import android.os.Handler;
import android.os.HandlerThread;
import android.util.Log;

import java.io.IOException;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.Semaphore;
import java.util.concurrent.TimeUnit;

/**
 * Sequential frame reader: MediaExtractor + MediaCodec decoding into an ImageReader (YUV_420_888, or YCBCR_P010 for
 * HDR when the platform cannot tone-map), converted by {@link YuvConverter} into RGBA32 direct buffers with rotation,
 * crop and scaling applied.
 *
 * Driven synchronously from ONE C# worker thread (Ballverse.Media.Android.AndroidFrameSource, JNI-attached):
 * open() → allocateFrameBuffer()* → readFrame()* → close(). Methods return status values and keep the last error in
 * {@link #lastError()} ("CODE|message") instead of throwing across JNI.
 *
 * Frame selection: readFrame(t) yields the last frame with pts <= t (first frame if t precedes it). Forward reads
 * decode on; backward jumps or jumps > 3 s seek to the previous sync frame.
 * Repeated frames (e.g. a 30 fps source read at 60 fps for export) are converted from YUV only once: the reader
 * remembers which source frame each output buffer holds and copies an already converted buffer instead (C# treats
 * frame buffers as read-only).
 * HDR: on API 33+ the decoder is asked for SDR output (KEY_COLOR_TRANSFER_REQUEST); if it declines, 10-bit P010 frames
 * are tone-mapped by YuvConverter.
 */
public final class VideoReader {
    private static final String TAG = MediaBridge.TAG;
    private static final long CODEC_TIMEOUT_US = 10000;
    private static final long FRAME_TIMEOUT_MS = 5000;
    private static final long SEEK_FORWARD_THRESHOLD_US = 3000000;
    /** MediaFormat.KEY_COLOR_TRANSFER_REQUEST (API 33). */
    private static final String KEY_COLOR_TRANSFER_REQUEST = "color-transfer-request";
    /** ImageFormat.YCBCR_P010 (API 31). */
    private static final int FORMAT_P010 = 0x36;

    private final String mPath;
    private final int mMaxWidth;
    private final int mMaxHeight;
    private final boolean mToneMapRequested;

    private MediaExtractor mExtractor;
    private MediaCodec mCodec;
    private ImageReader mImageReader;
    private HandlerThread mImageThread;
    private final Semaphore mImageAvailable = new Semaphore(0);
    private YuvConverter mConverter;
    private final List<ByteBuffer> mBuffers = new ArrayList<>();
    /** pts of the frame whose RGBA is in mBuffers[i] (Long.MIN_VALUE = none); only this class writes the buffers. */
    private final List<Long> mBufferPts = new ArrayList<>();
    private int mConversions;

    private int mRotation;
    private int mWidth;
    private int mHeight;
    private long mDurationUs;
    private boolean mSourceHdr;
    private int mSourceTransfer = YuvConverter.TRANSFER_SDR;
    private boolean mPlatformToneMapped;
    private boolean mToneMapped;
    private int mStandard = -1;
    private boolean mFullRange;
    private int mTransfer = YuvConverter.TRANSFER_SDR;

    private boolean mInputDone;
    private boolean mOutputDone;
    private Frame mCurrent;
    private Frame mNext;
    private String mLastError;
    private final MediaCodec.BufferInfo mInfo = new MediaCodec.BufferInfo();

    private static final class Frame {
        final Image image;
        final long ptsUs;

        Frame(Image image, long ptsUs) {
            this.image = image;
            this.ptsUs = ptsUs;
        }
    }

    public VideoReader(String path, int maxWidth, int maxHeight, boolean toneMapToSdr) {
        mPath = path;
        mMaxWidth = maxWidth;
        mMaxHeight = maxHeight;
        mToneMapRequested = toneMapToSdr;
    }

    public String lastError() {
        return mLastError;
    }

    public int width() {
        return mWidth;
    }

    public int height() {
        return mHeight;
    }

    public long durationUs() {
        return mDurationUs;
    }

    /** True when frames are SDR tone-mapped from an HDR source (platform or built-in). */
    public boolean toneMapped() {
        return mToneMapped;
    }

    /** Opens the extractor and decoder and decodes the first frame. */
    public boolean open() {
        try {
            openInternal();
            return true;
        } catch (MediaError e) {
            mLastError = e.toNative();
        } catch (Throwable t) {
            mLastError = MediaError.wrap(MediaError.DECODER_FAILED, t).toNative();
        }
        Log.w(TAG, "VideoReader.open failed: " + mLastError);
        releaseAll();
        return false;
    }

    /** Allocates one RGBA output buffer (direct, width*height*4) and returns its index. */
    public int allocateFrameBuffer() {
        ByteBuffer b = ByteBuffer.allocateDirect(mWidth * mHeight * 4).order(ByteOrder.LITTLE_ENDIAN);
        mBuffers.add(b);
        mBufferPts.add(Long.MIN_VALUE);
        return mBuffers.size() - 1;
    }

    public ByteBuffer frameBuffer(int index) {
        return mBuffers.get(index);
    }

    /** Decodes the frame shown at ptsUs into buffer #bufferIndex; returns its pts, or Long.MIN_VALUE on error. */
    public long readFrame(long ptsUs, int bufferIndex) {
        try {
            Frame f = seekAndDecode(ptsUs);
            fill(f, bufferIndex);
            return f.ptsUs;
        } catch (MediaError e) {
            mLastError = e.toNative();
        } catch (Throwable t) {
            mLastError = MediaError.wrap(MediaError.DECODER_FAILED, t).toNative();
        }
        Log.w(TAG, "VideoReader.readFrame failed: " + mLastError);
        return Long.MIN_VALUE;
    }

    /** YUV→RGBA conversions done so far (diagnostics: repeated frames are copied, not converted). */
    public int conversionCount() {
        return mConversions;
    }

    public void close() {
        releaseAll();
        mBuffers.clear();
        mBufferPts.clear();
    }

    /**
     * Puts frame f into buffer #index: nothing to do if the buffer already holds it, a memcpy if another buffer holds
     * it, otherwise the YUV conversion.
     */
    private void fill(Frame f, int index) throws MediaError {
        if (mBufferPts.get(index) == f.ptsUs) return;
        ByteBuffer dst = mBuffers.get(index);
        mBufferPts.set(index, Long.MIN_VALUE); // invalid while being written
        for (int i = 0; i < mBuffers.size(); i++) {
            if (i == index || mBufferPts.get(i) != f.ptsUs) continue;
            ByteBuffer src = mBuffers.get(i).duplicate();
            src.clear();
            dst.clear();
            dst.put(src);
            dst.clear();
            mBufferPts.set(index, f.ptsUs);
            return;
        }
        convert(f.image, dst);
        mConversions++;
        mBufferPts.set(index, f.ptsUs);
    }

    // ------------------------------------------------------------------ open

    private void openInternal() throws MediaError, IOException {
        mExtractor = new MediaExtractor();
        try {
            mExtractor.setDataSource(mPath);
        } catch (IOException e) {
            throw new MediaError(MediaError.CORRUPT, "cannot parse the file", e);
        }
        MediaProbe.checkDrm(mExtractor);
        MediaProbe.VideoTrack track = MediaProbe.selectVideoTrack(mExtractor);
        if (track.index < 0) {
            for (int i = 0; i < mExtractor.getTrackCount(); i++) {
                String mime = mExtractor.getTrackFormat(i).getString(MediaFormat.KEY_MIME);
                if (mime != null && mime.startsWith("video/")) throw new MediaError(MediaError.UNSUPPORTED_FORMAT, "no decoder for " + mime);
            }
            throw new MediaError(MediaError.NO_VIDEO_TRACK, "no video track");
        }
        mExtractor.selectTrack(track.index);
        MediaFormat format = track.format;
        int codedW = MediaProbe.intOr(format, MediaFormat.KEY_WIDTH, 0);
        int codedH = MediaProbe.intOr(format, MediaFormat.KEY_HEIGHT, 0);
        if (codedW <= 0 || codedH <= 0) throw new MediaError(MediaError.CORRUPT, "invalid video size");
        mRotation = MediaProbe.rotationOf(format, mPath);
        mDurationUs = MediaProbe.longOr(format, MediaFormat.KEY_DURATION, 0);
        int profile = MediaProbe.intOr(format, MediaFormat.KEY_PROFILE, -1);
        String transferName = MediaProbe.transferName(track.mime, Build.VERSION.SDK_INT >= 24
                ? MediaProbe.intOr(format, MediaFormat.KEY_COLOR_TRANSFER, 0) : 0, profile);
        mSourceHdr = !"sdr".equals(transferName);
        mSourceTransfer = "pq".equals(transferName) ? YuvConverter.TRANSFER_PQ
                : (mSourceHdr ? YuvConverter.TRANSFER_HLG : YuvConverter.TRANSFER_SDR);
        if ("dolbyvision".equals(transferName)) mSourceTransfer = YuvConverter.TRANSFER_HLG; // DV 8.4 base layer is HLG
        boolean tenBit = MediaProbe.isTenBit(track.mime, profile) || mSourceHdr;
        readColorFrom(format);

        boolean swap = mRotation == 90 || mRotation == 270;
        int[] out = YuvConverter.outputSize(swap ? codedH : codedW, swap ? codedW : codedH, mMaxWidth, mMaxHeight);
        mWidth = out[0];
        mHeight = out[1];
        mConverter = new YuvConverter(mWidth, mHeight, mRotation);

        // Attempts: (1) platform tone-mapping to SDR (API 33+), (2) P010 + built-in tone-mapping (API 31+), (3) plain YUV_420_888.
        List<int[]> attempts = new ArrayList<>(); // {imageFormat, requestSdr}
        if (mSourceHdr && mToneMapRequested && Build.VERSION.SDK_INT >= 33) attempts.add(new int[] {ImageFormat.YUV_420_888, 1});
        if (tenBit && Build.VERSION.SDK_INT >= 31) attempts.add(new int[] {FORMAT_P010, 0});
        attempts.add(new int[] {ImageFormat.YUV_420_888, 0});

        MediaError lastFailure = null;
        for (int[] attempt : attempts) {
            try {
                startDecoder(track.decoderName, format, codedW, codedH, attempt[0], attempt[1] == 1);
                Frame first = decodeNext();
                if (first == null) throw new MediaError(MediaError.CORRUPT, "no video frames");
                mNext = first;
                mToneMapped = mSourceHdr && (mPlatformToneMapped || (mToneMapRequested && attempt[0] == FORMAT_P010));
                if (mSourceHdr && !mToneMapped) Log.w(TAG, "HDR source decoded without tone mapping (device limitation)");
                return;
            } catch (MediaError e) {
                lastFailure = e;
            } catch (RuntimeException e) {
                lastFailure = MediaError.wrap(MediaError.DECODER_FAILED, e);
            }
            Log.w(TAG, "decoder attempt failed (format " + attempt[0] + ", sdr " + attempt[1] + "): " + lastFailure.toNative());
            stopDecoder();
            mExtractor.seekTo(0, MediaExtractor.SEEK_TO_PREVIOUS_SYNC);
        }
        if (mSourceHdr) throw new MediaError(MediaError.HDR_UNSUPPORTED, "cannot decode this HDR video: " + lastFailure.getMessage());
        throw lastFailure;
    }

    private void startDecoder(String decoderName, MediaFormat trackFormat, int w, int h, int imageFormat, boolean requestSdr)
            throws MediaError, IOException {
        mImageThread = new HandlerThread("BallverseVideoReader");
        mImageThread.start();
        mImageAvailable.drainPermits();
        mImageReader = ImageReader.newInstance(w, h, imageFormat, 4);
        mImageReader.setOnImageAvailableListener(new ImageReader.OnImageAvailableListener() {
            @Override
            public void onImageAvailable(ImageReader reader) {
                mImageAvailable.release();
            }
        }, new Handler(mImageThread.getLooper()));

        MediaFormat format = MediaFormat.createVideoFormat(trackFormat.getString(MediaFormat.KEY_MIME), w, h);
        copyKeys(trackFormat, format);
        if (requestSdr) format.setInteger(KEY_COLOR_TRANSFER_REQUEST, MediaFormat.COLOR_TRANSFER_SDR_VIDEO);
        mCodec = MediaCodec.createByCodecName(decoderName);
        mCodec.configure(format, mImageReader.getSurface(), null, 0);
        mPlatformToneMapped = false;
        if (requestSdr) {
            // A decoder that supports tone mapping echoes the request in its input format after configure().
            MediaFormat input = mCodec.getInputFormat();
            mPlatformToneMapped = MediaProbe.intOr(input, KEY_COLOR_TRANSFER_REQUEST, 0) == MediaFormat.COLOR_TRANSFER_SDR_VIDEO;
            if (!mPlatformToneMapped) throw new MediaError(MediaError.HDR_UNSUPPORTED, "decoder does not tone-map");
        }
        mCodec.start();
        mInputDone = false;
        mOutputDone = false;
    }

    /** Copies the codec-relevant keys of the track (csd buffers, profile, colour, rotation) into the decoder format. */
    private static void copyKeys(MediaFormat from, MediaFormat to) {
        String[] keys = {"csd-0", "csd-1", "csd-2"};
        for (String k : keys) if (from.containsKey(k)) to.setByteBuffer(k, from.getByteBuffer(k));
        String[] ints = {MediaFormat.KEY_PROFILE, MediaFormat.KEY_LEVEL, MediaFormat.KEY_COLOR_STANDARD,
                MediaFormat.KEY_COLOR_RANGE, MediaFormat.KEY_COLOR_TRANSFER, MediaFormat.KEY_MAX_INPUT_SIZE};
        for (String k : ints) {
            int v = MediaProbe.intOr(from, k, Integer.MIN_VALUE);
            if (v != Integer.MIN_VALUE) to.setInteger(k, v);
        }
        if (from.containsKey(MediaFormat.KEY_HDR_STATIC_INFO)) to.setByteBuffer(MediaFormat.KEY_HDR_STATIC_INFO, from.getByteBuffer(MediaFormat.KEY_HDR_STATIC_INFO));
    }

    private void readColorFrom(MediaFormat f) {
        if (Build.VERSION.SDK_INT < 24) return;
        int standard = MediaProbe.intOr(f, MediaFormat.KEY_COLOR_STANDARD, 0);
        int range = MediaProbe.intOr(f, MediaFormat.KEY_COLOR_RANGE, 0);
        int transfer = MediaProbe.intOr(f, MediaFormat.KEY_COLOR_TRANSFER, 0);
        if (standard == MediaFormat.COLOR_STANDARD_BT709) mStandard = YuvConverter.STANDARD_BT709;
        else if (standard == MediaFormat.COLOR_STANDARD_BT2020) mStandard = YuvConverter.STANDARD_BT2020;
        else if (standard == MediaFormat.COLOR_STANDARD_BT601_PAL || standard == MediaFormat.COLOR_STANDARD_BT601_NTSC) mStandard = YuvConverter.STANDARD_BT601;
        if (range != 0) mFullRange = range == MediaFormat.COLOR_RANGE_FULL;
        if (transfer == MediaFormat.COLOR_TRANSFER_ST2084) mTransfer = YuvConverter.TRANSFER_PQ;
        else if (transfer == MediaFormat.COLOR_TRANSFER_HLG) mTransfer = YuvConverter.TRANSFER_HLG;
        else if (transfer != 0) mTransfer = YuvConverter.TRANSFER_SDR;
    }

    // ------------------------------------------------------------------ decode

    private Frame seekAndDecode(long target) throws MediaError {
        if (mCodec == null) throw new MediaError(MediaError.INVALID_STATE, "reader is not open");
        Frame anchor = mCurrent != null ? mCurrent : mNext;
        boolean backward = anchor != null && target < anchor.ptsUs && mCurrent != null;
        boolean farForward = anchor != null && target - anchor.ptsUs > SEEK_FORWARD_THRESHOLD_US
                && (mNext == null || target >= mNext.ptsUs);
        if (backward || farForward) seek(target);

        while (true) {
            if (mNext == null && !mOutputDone) mNext = decodeNext();
            if (mNext == null) break;                 // end of stream: keep the last frame
            if (mNext.ptsUs <= target) {
                release(mCurrent);
                mCurrent = mNext;
                mNext = null;
                continue;
            }
            break;
        }
        if (mCurrent == null) {
            // target precedes the first frame
            if (mNext == null) throw new MediaError(MediaError.CORRUPT, "no decodable frames");
            return mNext;
        }
        return mCurrent;
    }

    private void seek(long target) throws MediaError {
        release(mCurrent);
        release(mNext);
        mCurrent = null;
        mNext = null;
        mExtractor.seekTo(target, MediaExtractor.SEEK_TO_PREVIOUS_SYNC);
        try {
            mCodec.flush();
        } catch (RuntimeException e) {
            throw MediaError.wrap(MediaError.DECODER_FAILED, e);
        }
        mInputDone = false;
        mOutputDone = false;
        mImageAvailable.drainPermits();
    }

    /** Feeds input until one output frame lands in the ImageReader; null at end of stream. */
    private Frame decodeNext() throws MediaError {
        long deadline = System.nanoTime() + FRAME_TIMEOUT_MS * 1000000L;
        while (true) {
            if (!mInputDone) {
                int in = mCodec.dequeueInputBuffer(CODEC_TIMEOUT_US);
                if (in >= 0) {
                    ByteBuffer buf = mCodec.getInputBuffer(in);
                    int size = buf == null ? -1 : mExtractor.readSampleData(buf, 0);
                    if (size < 0) {
                        mCodec.queueInputBuffer(in, 0, 0, 0, MediaCodec.BUFFER_FLAG_END_OF_STREAM);
                        mInputDone = true;
                    } else {
                        mCodec.queueInputBuffer(in, 0, size, mExtractor.getSampleTime(), 0);
                        mExtractor.advance();
                    }
                }
            }
            if (mOutputDone) return null;
            int out = mCodec.dequeueOutputBuffer(mInfo, CODEC_TIMEOUT_US);
            if (out == MediaCodec.INFO_OUTPUT_FORMAT_CHANGED) {
                readColorFrom(mCodec.getOutputFormat());
            } else if (out >= 0) {
                boolean eos = (mInfo.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0;
                boolean render = mInfo.size > 0 || !eos;
                long pts = mInfo.presentationTimeUs;
                mCodec.releaseOutputBuffer(out, render);
                if (eos) mOutputDone = true;
                if (render) {
                    Image image = awaitImage();
                    if (image != null) return new Frame(image, pts);
                    throw new MediaError(MediaError.TIMEOUT, "decoded frame did not reach the ImageReader");
                }
                if (eos) return null;
            }
            if (System.nanoTime() > deadline) throw new MediaError(MediaError.TIMEOUT, "decoder produced no frame in " + FRAME_TIMEOUT_MS + " ms");
        }
    }

    private Image awaitImage() throws MediaError {
        try {
            if (!mImageAvailable.tryAcquire(FRAME_TIMEOUT_MS, TimeUnit.MILLISECONDS)) return null;
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
            throw new MediaError(MediaError.DECODER_FAILED, "interrupted");
        }
        try {
            return mImageReader.acquireNextImage();
        } catch (UnsupportedOperationException e) {
            throw new MediaError(MediaError.HDR_UNSUPPORTED, "decoder output format not readable", e);
        }
    }

    private void convert(Image image, ByteBuffer dst) throws MediaError {
        Image.Plane[] planes = image.getPlanes();
        if (planes.length < 3) throw new MediaError(MediaError.DECODER_FAILED, "unexpected plane count " + planes.length);
        boolean sixteenBit = image.getFormat() == FORMAT_P010 || planes[0].getPixelStride() == 2;
        Rect crop = image.getCropRect();
        int cx = 0, cy = 0, cw = image.getWidth(), ch = image.getHeight();
        if (crop != null && crop.width() > 0 && crop.height() > 0) {
            cx = crop.left & ~1;
            cy = crop.top & ~1;
            cw = Math.min(crop.width(), image.getWidth() - cx);
            ch = Math.min(crop.height(), image.getHeight() - cy);
        }
        int standard = mStandard >= 0 ? mStandard
                : (sixteenBit ? YuvConverter.STANDARD_BT2020 : (ch >= 720 || cw >= 1280 ? YuvConverter.STANDARD_BT709 : YuvConverter.STANDARD_BT601));
        int transfer = mPlatformToneMapped ? YuvConverter.TRANSFER_SDR : (mTransfer != YuvConverter.TRANSFER_SDR ? mTransfer : mSourceTransfer);
        if (!sixteenBit || !mToneMapRequested) transfer = YuvConverter.TRANSFER_SDR; // 8-bit buffers are already SDR-coded
        mConverter.setColor(standard, mFullRange, transfer);
        mConverter.convert(plane(planes[0]), plane(planes[1]), plane(planes[2]), sixteenBit ? 2 : 1, cx, cy, cw, ch, dst);
    }

    private static YuvConverter.Plane plane(Image.Plane p) {
        return new YuvConverter.Plane(p.getBuffer(), p.getRowStride(), p.getPixelStride());
    }

    // ------------------------------------------------------------------ cleanup

    private static void release(Frame f) {
        if (f != null) f.image.close();
    }

    private void stopDecoder() {
        release(mCurrent);
        release(mNext);
        mCurrent = null;
        mNext = null;
        if (mCodec != null) {
            try {
                mCodec.stop();
            } catch (RuntimeException ignored) {
                // not started / already failed
            }
            mCodec.release();
            mCodec = null;
        }
        if (mImageReader != null) {
            mImageReader.close();
            mImageReader = null;
        }
        if (mImageThread != null) {
            mImageThread.quitSafely();
            mImageThread = null;
        }
    }

    private void releaseAll() {
        stopDecoder();
        if (mExtractor != null) {
            mExtractor.release();
            mExtractor = null;
        }
    }
}
