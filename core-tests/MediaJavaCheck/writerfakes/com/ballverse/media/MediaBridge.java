package com.ballverse.media;

import java.util.concurrent.LinkedBlockingQueue;
import java.util.concurrent.TimeUnit;

/**
 * JVM stand-in for the real MediaBridge (which needs an Activity and UnityPlayer): records the completions VideoWriter
 * reports. A listener runs synchronously on the reporting thread, so the check can capture the native state at the very
 * moment a release is reported.
 */
final class MediaBridge {
    static final String TAG = "BallverseMedia";

    interface Listener {
        void onResult(long id, String kind);
    }

    static volatile Listener sListener;
    private static final LinkedBlockingQueue<String> RESULTS = new LinkedBlockingQueue<>();

    private MediaBridge() {
    }

    static void succeed(long id, String json) {
        report(id, "ok");
    }

    static void fail(long id, MediaError error) {
        report(id, "fail:" + error.toNative());
    }

    static void cancelled(long id) {
        report(id, "cancelled");
    }

    private static void report(long id, String kind) {
        Listener l = sListener;
        if (l != null) l.onResult(id, kind);
        RESULTS.add(id + " " + kind);
    }

    /** Next result line ("id kind"), or null after timeoutMs. */
    static String poll(long timeoutMs) throws InterruptedException {
        return RESULTS.poll(timeoutMs, TimeUnit.MILLISECONDS);
    }

    static void clear() {
        RESULTS.clear();
    }
}
