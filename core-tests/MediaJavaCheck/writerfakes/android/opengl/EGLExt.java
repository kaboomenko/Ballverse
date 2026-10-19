package android.opengl;

/** JVM fake for VideoWriterReleaseCheck (EGL path only). Not the Android class. */
public final class EGLExt {
    /** Presentation time of the last swapped frame, ns. */
    public static volatile long lastPresentationTimeNs = -1;

    private EGLExt() {
    }

    public static boolean eglPresentationTimeANDROID(EGLDisplay dpy, EGLSurface surface, long timeNs) {
        lastPresentationTimeNs = timeNs;
        return true;
    }
}
