package android.opengl;

import android.view.Surface;

import java.util.concurrent.atomic.AtomicInteger;

/**
 * JVM fake for VideoWriterReleaseCheck. Not the Android class. By default there is no EGL display, so VideoWriter takes
 * its lockHardwareCanvas fallback. With {@link #available} set, the display works: contexts and window surfaces are
 * counted (the check asserts the writer's EGL teardown ran before a release is reported) and eglSwapBuffers posts one
 * frame to the encoder's input surface (GLES20 / EGLExt fakes accept the renderer's GL calls).
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

    /** When true, the fake display works and the writer uses its EGL renderer. */
    public static volatile boolean available;
    /** Frames swapped into an encoder surface so far. */
    public static final AtomicInteger swaps = new AtomicInteger();

    private static final EGLDisplay DISPLAY = new EGLDisplay();
    private static final AtomicInteger LIVE_CONTEXTS = new AtomicInteger();
    private static final AtomicInteger LIVE_SURFACES = new AtomicInteger();

    private EGL14() {
    }

    /** EGL contexts + window surfaces created and not destroyed yet. */
    public static int liveCount() {
        return LIVE_CONTEXTS.get() + LIVE_SURFACES.get();
    }

    public static void reset() {
        available = false;
        swaps.set(0);
        LIVE_CONTEXTS.set(0);
        LIVE_SURFACES.set(0);
    }

    public static EGLDisplay eglGetDisplay(int displayId) {
        return available ? DISPLAY : EGL_NO_DISPLAY;
    }

    public static int eglGetError() {
        return available ? 0x3000 : 0x3008; // EGL_SUCCESS : EGL_BAD_DISPLAY
    }

    public static boolean eglInitialize(EGLDisplay dpy, int[] major, int majorOffset, int[] minor, int minorOffset) {
        if (dpy != DISPLAY) return false;
        major[majorOffset] = 1;
        minor[minorOffset] = 4;
        return true;
    }

    public static boolean eglChooseConfig(EGLDisplay dpy, int[] attribs, int attribsOffset, EGLConfig[] configs, int configsOffset,
            int configSize, int[] numConfig, int numConfigOffset) {
        if (dpy != DISPLAY) return false;
        configs[configsOffset] = new EGLConfig();
        numConfig[numConfigOffset] = 1;
        return true;
    }

    public static EGLContext eglCreateContext(EGLDisplay dpy, EGLConfig config, EGLContext share, int[] attribs, int offset) {
        if (dpy != DISPLAY) return EGL_NO_CONTEXT;
        LIVE_CONTEXTS.incrementAndGet();
        return new EGLContext();
    }

    public static EGLSurface eglCreateWindowSurface(EGLDisplay dpy, EGLConfig config, Object win, int[] attribs, int offset) {
        if (dpy != DISPLAY || !(win instanceof Surface)) return EGL_NO_SURFACE;
        LIVE_SURFACES.incrementAndGet();
        return new EGLSurface(win);
    }

    public static boolean eglMakeCurrent(EGLDisplay dpy, EGLSurface draw, EGLSurface read, EGLContext ctx) {
        return dpy == DISPLAY;
    }

    public static boolean eglSwapBuffers(EGLDisplay dpy, EGLSurface surface) {
        if (dpy != DISPLAY || !(surface.window instanceof Surface)) return false;
        swaps.incrementAndGet();
        ((Surface) surface.window).onEglSwap();
        return true;
    }

    public static boolean eglDestroySurface(EGLDisplay dpy, EGLSurface surface) {
        if (dpy == DISPLAY && surface != EGL_NO_SURFACE) LIVE_SURFACES.decrementAndGet();
        return true;
    }

    public static boolean eglDestroyContext(EGLDisplay dpy, EGLContext ctx) {
        if (dpy == DISPLAY && ctx != EGL_NO_CONTEXT) LIVE_CONTEXTS.decrementAndGet();
        return true;
    }

    public static boolean eglReleaseThread() {
        return true;
    }
}
