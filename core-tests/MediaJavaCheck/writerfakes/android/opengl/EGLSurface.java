package android.opengl;

/** JVM fake handle for VideoWriterReleaseCheck: remembers the window (encoder input Surface) it draws into. Not the Android class. */
public final class EGLSurface {
    /** The native window passed to eglCreateWindowSurface (null for EGL_NO_SURFACE). */
    public final Object window;

    public EGLSurface() {
        this(null);
    }

    public EGLSurface(Object window) {
        this.window = window;
    }
}
