package android.opengl;

/**
 * JVM fake for VideoWriterReleaseCheck: there is no EGL display, so VideoWriter takes its lockHardwareCanvas fallback
 * (GLES20 / EGLExt are then never called; they compile against android-all.jar). Not the Android class.
 */
public final class EGL14 {
    public static final EGLDisplay EGL_NO_DISPLAY = new EGLDisplay();
    public static final EGLContext EGL_NO_CONTEXT = new EGLContext();
    public static final EGLSurface EGL_NO_SURFACE = new EGLSurface();
    public static final int EGL_DEFAULT_DISPLAY = 0;
    public static final int EGL_RED_SIZE = 0x3024;
    public static final int EGL_GREEN_SIZE = 0x3023;
    public static final int EGL_BLUE_SIZE = 0x3022;
    public static final int EGL_ALPHA_SIZE = 0x3021;
    public static final int EGL_RENDERABLE_TYPE = 0x3040;
    public static final int EGL_OPENGL_ES2_BIT = 4;
    public static final int EGL_NONE = 0x3038;
    public static final int EGL_CONTEXT_CLIENT_VERSION = 0x3098;

    public static EGLDisplay eglGetDisplay(int displayId) {
        return EGL_NO_DISPLAY;
    }

    public static int eglGetError() {
        return 0x3008; // EGL_BAD_DISPLAY
    }

    public static boolean eglInitialize(EGLDisplay dpy, int[] major, int majorOffset, int[] minor, int minorOffset) {
        return false;
    }

    public static boolean eglChooseConfig(EGLDisplay dpy, int[] attribs, int attribsOffset, EGLConfig[] configs, int configsOffset,
            int configSize, int[] numConfig, int numConfigOffset) {
        return false;
    }

    public static EGLContext eglCreateContext(EGLDisplay dpy, EGLConfig config, EGLContext share, int[] attribs, int offset) {
        return EGL_NO_CONTEXT;
    }

    public static EGLSurface eglCreateWindowSurface(EGLDisplay dpy, EGLConfig config, Object win, int[] attribs, int offset) {
        return EGL_NO_SURFACE;
    }

    public static boolean eglMakeCurrent(EGLDisplay dpy, EGLSurface draw, EGLSurface read, EGLContext ctx) {
        return false;
    }

    public static boolean eglSwapBuffers(EGLDisplay dpy, EGLSurface surface) {
        return false;
    }

    public static boolean eglDestroySurface(EGLDisplay dpy, EGLSurface surface) {
        return true;
    }

    public static boolean eglDestroyContext(EGLDisplay dpy, EGLContext ctx) {
        return true;
    }

    public static boolean eglReleaseThread() {
        return true;
    }
}
