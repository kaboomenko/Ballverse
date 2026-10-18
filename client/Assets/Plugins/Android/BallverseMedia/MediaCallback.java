package com.ballverse.media;

/**
 * Completion sink implemented in C# (AndroidJavaProxy, Ballverse.Media.Android.AndroidCallbackProxy).
 * Called from background threads; the C# side marshals to the Unity main thread.
 * status: 0 = ok (json = result), 1 = cancelled, 2 = error (json = {"code","message"}).
 */
public interface MediaCallback {
    void onResult(long requestId, int status, String json);

    void onProgress(long requestId, double progress);
}
