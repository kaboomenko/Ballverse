package android.opengl;

import java.nio.Buffer;

/**
 * JVM fake for VideoWriterReleaseCheck (EGL path only): accepts the GL calls of VideoWriter's EglRenderer, reports
 * successful shader compile/link and no GL errors. Only the members the writer uses. Not the Android class.
 */
public final class GLES20 {
    public static final int GL_NO_ERROR = 0;
    public static final int GL_TEXTURE_2D = 0x0DE1;
    public static final int GL_TEXTURE_MIN_FILTER = 0x2801;
    public static final int GL_TEXTURE_MAG_FILTER = 0x2800;
    public static final int GL_TEXTURE_WRAP_S = 0x2802;
    public static final int GL_TEXTURE_WRAP_T = 0x2803;
    public static final int GL_NEAREST = 0x2600;
    public static final int GL_CLAMP_TO_EDGE = 0x812F;
    public static final int GL_UNPACK_ALIGNMENT = 0x0CF5;
    public static final int GL_RGBA = 0x1908;
    public static final int GL_UNSIGNED_BYTE = 0x1401;
    public static final int GL_FLOAT = 0x1406;
    public static final int GL_TEXTURE0 = 0x84C0;
    public static final int GL_TRIANGLE_STRIP = 0x0005;
    public static final int GL_VERTEX_SHADER = 0x8B31;
    public static final int GL_FRAGMENT_SHADER = 0x8B30;
    public static final int GL_COMPILE_STATUS = 0x8B81;
    public static final int GL_LINK_STATUS = 0x8B82;

    private static int sNextName = 1;

    private GLES20() {
    }

    private static synchronized int name() {
        return sNextName++;
    }

    public static int glGetError() {
        return GL_NO_ERROR;
    }

    public static int glCreateShader(int type) {
        return name();
    }

    public static void glShaderSource(int shader, String source) {
        // accepted
    }

    public static void glCompileShader(int shader) {
        // accepted
    }

    public static void glGetShaderiv(int shader, int pname, int[] params, int offset) {
        params[offset] = 1;
    }

    public static String glGetShaderInfoLog(int shader) {
        return "";
    }

    public static int glCreateProgram() {
        return name();
    }

    public static void glAttachShader(int program, int shader) {
        // accepted
    }

    public static void glLinkProgram(int program) {
        // accepted
    }

    public static void glGetProgramiv(int program, int pname, int[] params, int offset) {
        params[offset] = 1;
    }

    public static String glGetProgramInfoLog(int program) {
        return "";
    }

    public static int glGetAttribLocation(int program, String name) {
        return "aPos".equals(name) ? 0 : 1;
    }

    public static void glGenTextures(int n, int[] textures, int offset) {
        for (int i = 0; i < n; i++) textures[offset + i] = name();
    }

    public static void glBindTexture(int target, int texture) {
        // accepted
    }

    public static void glTexParameteri(int target, int pname, int param) {
        // accepted
    }

    public static void glPixelStorei(int pname, int param) {
        // accepted
    }

    public static void glTexImage2D(int target, int level, int internalformat, int width, int height, int border, int format, int type,
            Buffer pixels) {
        // accepted
    }

    public static void glTexSubImage2D(int target, int level, int xoffset, int yoffset, int width, int height, int format, int type,
            Buffer pixels) {
        if (pixels == null || pixels.remaining() < width * height * 4) throw new IllegalArgumentException("short pixel buffer");
    }

    public static void glViewport(int x, int y, int width, int height) {
        // accepted
    }

    public static void glUseProgram(int program) {
        // accepted
    }

    public static void glActiveTexture(int texture) {
        // accepted
    }

    public static void glVertexAttribPointer(int index, int size, int type, boolean normalized, int stride, Buffer ptr) {
        // accepted
    }

    public static void glEnableVertexAttribArray(int index) {
        // accepted
    }

    public static void glDrawArrays(int mode, int first, int count) {
        // accepted
    }
}
