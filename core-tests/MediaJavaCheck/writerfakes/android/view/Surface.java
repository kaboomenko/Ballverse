package android.view;

import android.graphics.Canvas;
import android.media.MediaCodec;

/** JVM fake: the encoder's input surface; posting a canvas hands one frame to the fake codec. Not the Android class. */
public final class Surface {
    private final MediaCodec mCodec;

    public Surface(MediaCodec codec) {
        mCodec = codec;
    }

    public Canvas lockHardwareCanvas() {
        return new Canvas();
    }

    public void unlockCanvasAndPost(Canvas canvas) {
        mCodec.onFramePosted();
    }

    /** Fake EGL: eglSwapBuffers on a window surface of this Surface posts one frame. */
    public void onEglSwap() {
        mCodec.onFramePosted();
    }

    public void release() {
        // nothing to do
    }
}
