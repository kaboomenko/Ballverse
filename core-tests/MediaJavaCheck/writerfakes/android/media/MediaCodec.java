package android.media;

import android.view.Surface;

import java.nio.ByteBuffer;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * JVM fake of a SURFACE-input H.264 encoder for VideoWriterReleaseCheck (not the Android class). Frames arrive through
 * the input {@link Surface} (unlockCanvasAndPost), come out in order after a format change, and end-of-stream
 * propagates. Gates let the check park the writer thread inside the codec (a slow hardware teardown / stuck frame).
 * The AAC input methods exist only so that AacPcmEncoder compiles; the check writes video-only files.
 */
public final class MediaCodec {
    public static final int CONFIGURE_FLAG_ENCODE = 1;
    public static final int BUFFER_FLAG_CODEC_CONFIG = 2;
    public static final int BUFFER_FLAG_END_OF_STREAM = 4;
    public static final int INFO_TRY_AGAIN_LATER = -1;
    public static final int INFO_OUTPUT_FORMAT_CHANGED = -2;

    public static final class BufferInfo {
        public int offset;
        public int size;
        public long presentationTimeUs;
        public int flags;

        public void set(int newOffset, int newSize, long newTimeUs, int newFlags) {
            offset = newOffset;
            size = newSize;
            presentationTimeUs = newTimeUs;
            flags = newFlags;
        }
    }

    /** When set, a frame posted to the input surface blocks the posting (writer) thread until the latch opens. */
    public static volatile CountDownLatch frameGate;
    /** When set, signalEndOfInputStream blocks until the latch opens. */
    public static volatile CountDownLatch eosGate;
    /** Threads that have reached a gate (frameGate / eosGate) so far. */
    public static final AtomicInteger blocked = new AtomicInteger();
    /** release() takes this long (a slow hardware-encoder teardown), ms. */
    public static volatile long releaseDelayMs;
    /** Next configure() throws (encoder rejects the format). */
    public static volatile boolean failConfigure;
    /**
     * Encoder instances the "device" allows at once (0 = unlimited). Creating one more throws, like a phone with a single
     * hardware AVC encoder whose previous user has not released it yet.
     */
    public static volatile int maxLive;

    private static final List<MediaCodec> ALL = new ArrayList<>();

    private final ArrayDeque<Integer> mFrames = new ArrayDeque<>();
    private boolean mFormatSent;
    private boolean mEos;
    private boolean mEosDelivered;
    private volatile boolean mReleased;
    private final ByteBuffer mOutput = ByteBuffer.allocate(16);

    /** Codecs created and not released yet (the check asserts the hardware encoder is free). */
    public static int liveCount() {
        synchronized (ALL) {
            int n = 0;
            for (MediaCodec c : ALL) if (!c.mReleased) n++;
            return n;
        }
    }

    public static void resetAll() {
        synchronized (ALL) {
            ALL.clear();
        }
        frameGate = null;
        eosGate = null;
        failConfigure = false;
        maxLive = 0;
        releaseDelayMs = 0;
        blocked.set(0);
    }

    private MediaCodec() {
        synchronized (ALL) {
            ALL.add(this);
        }
    }

    public static MediaCodec createByCodecName(String name) {
        return create();
    }

    public static MediaCodec createEncoderByType(String type) {
        return create();
    }

    private static MediaCodec create() {
        int max = maxLive;
        if (max > 0 && liveCount() >= max) throw new IllegalStateException("fake device: no free encoder instance (" + max + " in use)");
        return new MediaCodec();
    }

    public MediaCodecInfo getCodecInfo() {
        return new MediaCodecInfo();
    }

    public void configure(MediaFormat format, Surface surface, Object crypto, int flags) {
        if (flags != CONFIGURE_FLAG_ENCODE) throw new IllegalStateException("not an encoder");
        if (failConfigure) {
            failConfigure = false;
            throw new IllegalArgumentException("fake encoder rejects the format");
        }
    }

    public void reset() {
        // nothing to do
    }

    public Surface createInputSurface() {
        return new Surface(this);
    }

    public void start() {
        // nothing to do
    }

    public void stop() {
        // nothing to do
    }

    public void release() {
        long delay = releaseDelayMs;
        if (delay > 0) {
            try {
                Thread.sleep(delay);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
            }
        }
        mReleased = true;
    }

    /** Called by the fake Surface for every posted frame (writer thread). */
    public void onFramePosted() {
        await(frameGate);
        synchronized (this) {
            mFrames.add(mFrames.size());
        }
    }

    public void signalEndOfInputStream() {
        await(eosGate);
        synchronized (this) {
            mEos = true;
        }
    }

    private static void await(CountDownLatch gate) {
        if (gate == null) return;
        blocked.incrementAndGet();
        try {
            if (!gate.await(30, TimeUnit.SECONDS)) throw new IllegalStateException("gate never opened");
        } catch (InterruptedException e) {
            Thread.currentThread().interrupt();
        }
    }

    public synchronized int dequeueOutputBuffer(BufferInfo info, long timeoutUs) {
        if (!mFormatSent && !mFrames.isEmpty()) {
            mFormatSent = true;
            return INFO_OUTPUT_FORMAT_CHANGED;
        }
        if (!mFrames.isEmpty()) {
            mFrames.poll();
            info.set(0, mOutput.capacity(), 0, 0);
            return 0;
        }
        if (mEos && !mEosDelivered) {
            mEosDelivered = true;
            info.set(0, 0, 0, BUFFER_FLAG_END_OF_STREAM);
            return 0;
        }
        return INFO_TRY_AGAIN_LATER;
    }

    public ByteBuffer getOutputBuffer(int index) {
        mOutput.clear();
        return mOutput;
    }

    public MediaFormat getOutputFormat() {
        return new MediaFormat();
    }

    public void releaseOutputBuffer(int index, boolean render) {
        // nothing to do
    }

    public int dequeueInputBuffer(long timeoutUs) {
        throw new UnsupportedOperationException("audio is not part of this check");
    }

    public ByteBuffer getInputBuffer(int index) {
        throw new UnsupportedOperationException("audio is not part of this check");
    }

    public void queueInputBuffer(int index, int offset, int size, long presentationTimeUs, int flags) {
        throw new UnsupportedOperationException("audio is not part of this check");
    }
}
