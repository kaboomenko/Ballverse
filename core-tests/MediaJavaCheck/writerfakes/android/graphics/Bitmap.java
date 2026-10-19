package android.graphics;

import java.nio.Buffer;

/** JVM fake for VideoWriterReleaseCheck. Not the Android class. */
public final class Bitmap {
    public enum Config { ARGB_8888 }

    /** Next createBitmap throws (the writer thread's renderer setup fails after open() started the thread). */
    public static volatile boolean failCreate;

    public static Bitmap createBitmap(int width, int height, Config config) {
        if (failCreate) {
            failCreate = false;
            throw new IllegalStateException("fake bitmap allocation failure");
        }
        return new Bitmap();
    }

    public void setHasAlpha(boolean hasAlpha) {
        // nothing to do
    }

    public void copyPixelsFromBuffer(Buffer src) {
        // nothing to do
    }
}
