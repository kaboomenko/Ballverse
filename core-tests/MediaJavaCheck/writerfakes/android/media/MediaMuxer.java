package android.media;

import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.ByteBuffer;
import java.util.concurrent.atomic.AtomicInteger;

/** JVM fake for VideoWriterReleaseCheck: creates and appends to the real file so the check can see who deletes what. */
public final class MediaMuxer {
    public static final class OutputFormat {
        public static final int MUXER_OUTPUT_MPEG_4 = 0;
    }

    private static final AtomicInteger LIVE = new AtomicInteger();
    private FileOutputStream mOut;
    private int mTracks;
    private boolean mReleased;

    /** Muxers created and not released yet. */
    public static int liveCount() {
        return LIVE.get();
    }

    public MediaMuxer(String path, int format) throws IOException {
        mOut = new FileOutputStream(path);
        mOut.write(new byte[] {0, 0, 0, 8, 'f', 't', 'y', 'p'});
        LIVE.incrementAndGet();
    }

    public int addTrack(MediaFormat format) {
        return mTracks++;
    }

    public void start() {
        // nothing to do
    }

    public void writeSampleData(int track, ByteBuffer data, MediaCodec.BufferInfo info) {
        try {
            byte[] bytes = new byte[info.size];
            data.get(bytes);
            mOut.write(bytes);
        } catch (IOException e) {
            throw new IllegalStateException(e);
        }
    }

    public void stop() {
        close();
    }

    public void release() {
        close();
        if (!mReleased) {
            mReleased = true;
            LIVE.decrementAndGet();
        }
    }

    private void close() {
        try {
            if (mOut != null) mOut.close();
        } catch (IOException e) {
            throw new IllegalStateException(e);
        }
        mOut = null;
    }
}
