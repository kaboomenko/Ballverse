package android.media;

import java.nio.ByteBuffer;
import java.util.ArrayDeque;

/**
 * JVM fake of an AAC encoder for AacPcmEncoderCheck (not the Android class). Models what matters for timing:
 * a small pool of input buffers that only frees when output is drained, one buffer of encoder delay, a format change
 * before the first output, and end-of-stream propagation. Records every queued PCM range for the check.
 */
public final class MediaCodec {
    public static final int CONFIGURE_FLAG_ENCODE = 1;
    public static final int BUFFER_FLAG_CODEC_CONFIG = 2;
    public static final int BUFFER_FLAG_END_OF_STREAM = 4;
    public static final int INFO_TRY_AGAIN_LATER = -1;
    public static final int INFO_OUTPUT_FORMAT_CHANGED = -2;
    private static final int INPUT_BUFFERS = 2;
    private static final int INPUT_BYTES = 8192; // 2048 s16 stereo frames, larger than the encoder's chunk

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

    /** Last created codec (the check inspects it). */
    public static MediaCodec last;

    public long framesIn;
    public long lastInputEndUs = -1;
    public boolean eosQueued;
    public boolean started;
    public boolean released;
    private int mFreeInputs = INPUT_BUFFERS;
    private boolean mFormatSent;
    private final ArrayDeque<long[]> mQueued = new ArrayDeque<>(); // {pts, size, flags}
    private final ByteBuffer[] mInputs = new ByteBuffer[INPUT_BUFFERS];
    private final ByteBuffer mOutput = ByteBuffer.allocate(64);

    public static MediaCodec createEncoderByType(String type) {
        last = new MediaCodec();
        return last;
    }

    private MediaCodec() {
        for (int i = 0; i < INPUT_BUFFERS; i++) mInputs[i] = ByteBuffer.allocate(INPUT_BYTES);
    }

    public void configure(MediaFormat format, Object surface, Object crypto, int flags) {
        if (flags != CONFIGURE_FLAG_ENCODE) throw new IllegalStateException("not an encoder");
    }

    public void start() {
        started = true;
    }

    public void stop() {
        started = false;
    }

    public void release() {
        released = true;
    }

    public int dequeueInputBuffer(long timeoutUs) {
        if (eosQueued) throw new IllegalStateException("input after end of stream");
        if (mFreeInputs == 0) return INFO_TRY_AGAIN_LATER;
        mFreeInputs--;
        return mFreeInputs;
    }

    public ByteBuffer getInputBuffer(int index) {
        return mInputs[index];
    }

    public void queueInputBuffer(int index, int offset, int size, long presentationTimeUs, int flags) {
        long frames = size / 4;
        framesIn += frames;
        if (size > 0) lastInputEndUs = presentationTimeUs + (frames * 1000000L + 47999) / 48000;
        if ((flags & BUFFER_FLAG_END_OF_STREAM) != 0) eosQueued = true;
        mQueued.add(new long[] {presentationTimeUs, size, flags});
    }

    public int dequeueOutputBuffer(BufferInfo info, long timeoutUs) {
        if (!mFormatSent && !mQueued.isEmpty()) {
            mFormatSent = true;
            return INFO_OUTPUT_FORMAT_CHANGED;
        }
        // One buffer of encoder delay, except once end of stream is queued (flush).
        if (mQueued.isEmpty() || (mQueued.size() < 2 && !eosQueued)) return INFO_TRY_AGAIN_LATER;
        long[] q = mQueued.poll();
        mFreeInputs++;
        info.set(0, q[1] > 0 ? 16 : 0, q[0], (int) q[2]);
        return 0;
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
}
