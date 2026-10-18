package com.ballverse.media;

import android.Manifest;
import android.animation.ValueAnimator;
import android.app.Activity;
import android.content.ClipData;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Build;
import android.os.PowerManager;
import android.os.StatFs;
import android.provider.Settings;
import android.util.Log;

import com.unity3d.player.UnityPlayer;

import java.io.File;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.ThreadFactory;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.concurrent.atomic.AtomicInteger;

/**
 * Static entry points called from C# (Ballverse.Media.Android.AndroidMediaBridge) for the asynchronous, UI-related
 * part of the MediaBridge: picking + copying, probing, audio decoding, gallery save, share and device signals.
 * Frame decoding/encoding live in {@link VideoReader} and {@link VideoWriter} (synchronous objects driven from C#).
 *
 * Threading: methods may be called from any thread (normally the Unity main thread). Work runs on a small daemon
 * executor; completions go to the C# {@link MediaCallback} from those threads.
 */
public final class MediaBridge {
    static final String TAG = "BallverseMedia";
    static final int STATUS_OK = 0;
    static final int STATUS_CANCELLED = 1;
    static final int STATUS_ERROR = 2;

    private static volatile MediaCallback sCallback;
    private static volatile Context sAppContext;

    static final ExecutorService EXECUTOR = Executors.newCachedThreadPool(new ThreadFactory() {
        private final AtomicInteger mCount = new AtomicInteger();

        @Override
        public Thread newThread(Runnable r) {
            Thread t = new Thread(r, "BallverseMedia-" + mCount.incrementAndGet());
            t.setDaemon(true);
            return t;
        }
    });

    /** Cancellation flags of running requests. */
    private static final ConcurrentHashMap<Long, AtomicBoolean> CANCEL_FLAGS = new ConcurrentHashMap<>();
    /** Pick requests waiting for the picker result: id -> {destDir, baseName}. */
    private static final ConcurrentHashMap<Long, String[]> PENDING_PICKS = new ConcurrentHashMap<>();
    /** Saves waiting for the legacy storage permission: id -> {path, album}. */
    private static final ConcurrentHashMap<Long, String[]> PENDING_SAVES = new ConcurrentHashMap<>();

    private MediaBridge() {
    }

    /** Must be called once (C# bridge constructor) before any other method. */
    public static void init(Context context, MediaCallback callback) {
        sAppContext = context.getApplicationContext();
        sCallback = callback;
    }

    static Context context() {
        Context c = sAppContext;
        if (c == null) {
            Activity a = UnityPlayer.currentActivity;
            if (a != null) {
                c = a.getApplicationContext();
                sAppContext = c;
            }
        }
        if (c == null) throw new IllegalStateException("MediaBridge.init was not called");
        return c;
    }

    static Activity currentActivity() {
        return UnityPlayer.currentActivity;
    }

    // ---------------------------------------------------------------- completion helpers

    static void succeed(long id, String json) {
        CANCEL_FLAGS.remove(id);
        MediaCallback cb = sCallback;
        if (cb != null) cb.onResult(id, STATUS_OK, json);
    }

    static void fail(long id, MediaError error) {
        CANCEL_FLAGS.remove(id);
        Log.w(TAG, "request " + id + " failed: " + error.toNative());
        MediaCallback cb = sCallback;
        if (cb != null) cb.onResult(id, STATUS_ERROR, error.toJson());
    }

    static void cancelled(long id) {
        CANCEL_FLAGS.remove(id);
        MediaCallback cb = sCallback;
        if (cb != null) cb.onResult(id, STATUS_CANCELLED, "");
    }

    static void progress(long id, double value) {
        MediaCallback cb = sCallback;
        if (cb != null) cb.onProgress(id, value);
    }

    static AtomicBoolean cancelFlag(long id) {
        AtomicBoolean flag = new AtomicBoolean(false);
        AtomicBoolean prev = CANCEL_FLAGS.putIfAbsent(id, flag);
        return prev != null ? prev : flag;
    }

    /** Runs a request on the executor, mapping exceptions and cancellation to callbacks. */
    private interface Job {
        String run(AtomicBoolean cancel) throws Exception;
    }

    private static void submit(final long id, final String fallbackCode, final Job job) {
        final AtomicBoolean cancel = cancelFlag(id);
        EXECUTOR.execute(new Runnable() {
            @Override
            public void run() {
                try {
                    String json = job.run(cancel);
                    if (cancel.get()) cancelled(id);
                    else succeed(id, json);
                } catch (Throwable t) {
                    if (cancel.get()) cancelled(id);
                    else fail(id, MediaError.wrap(fallbackCode, t));
                }
            }
        });
    }

    /** Asks a running request to stop (partial output is deleted by the request itself). */
    public static void cancel(long id) {
        AtomicBoolean flag = CANCEL_FLAGS.get(id);
        if (flag != null) flag.set(true);
        if (PENDING_PICKS.remove(id) != null) {
            PickerActivity.cancelRequest(id);
            cancelled(id);
        }
        if (PENDING_SAVES.remove(id) != null) cancelled(id);
    }

    // ---------------------------------------------------------------- pick + import

    /** Opens the system picker in a transparent activity; result: {"path": copy} or {"path": ""} if dismissed. */
    public static void pickVideo(long id, String destDir, String baseName) {
        if (!PENDING_PICKS.isEmpty()) {
            fail(id, new MediaError(MediaError.BUSY, "a picker is already open"));
            return;
        }
        PENDING_PICKS.put(id, new String[] {destDir, baseName});
        cancelFlag(id);
        try {
            startPickerActivity(id, PickerActivity.MODE_PICK_VIDEO);
        } catch (RuntimeException e) {
            PENDING_PICKS.remove(id);
            fail(id, MediaError.wrap(MediaError.NOT_SUPPORTED, e));
        }
    }

    private static void startPickerActivity(long id, String mode) {
        Activity activity = currentActivity();
        Context ctx = activity != null ? activity : context();
        Intent intent = new Intent(ctx, PickerActivity.class);
        intent.putExtra(PickerActivity.EXTRA_REQUEST_ID, id);
        intent.putExtra(PickerActivity.EXTRA_MODE, mode);
        if (activity == null) intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        ctx.startActivity(intent);
    }

    /** From PickerActivity: uri == null means the user dismissed the picker. */
    static void onPickerResult(final long id, final Uri uri) {
        final String[] pending = PENDING_PICKS.remove(id);
        if (pending == null) return; // cancelled meanwhile or stale (process restarted)
        if (uri == null) {
            succeed(id, new Json().put("path", "").toString());
            return;
        }
        submit(id, MediaError.IO, new Job() {
            @Override
            public String run(AtomicBoolean cancel) throws Exception {
                File copy = MediaImporter.copy(context(), uri, new File(pending[0]), pending[1], id, cancel);
                return new Json().put("path", copy.getAbsolutePath()).toString();
            }
        });
    }

    static void onPickerFailed(long id, MediaError error) {
        if (PENDING_PICKS.remove(id) != null) fail(id, error);
    }

    // ---------------------------------------------------------------- probe / audio

    /** Result: MediaInfo JSON (C# NativeMediaInfoDto). */
    public static void probe(long id, final String path) {
        submit(id, MediaError.CORRUPT, new Job() {
            @Override
            public String run(AtomicBoolean cancel) throws Exception {
                return MediaProbe.probe(context(), path).toString();
            }
        });
    }

    /**
     * Decodes the first audio track (range [startUs, startUs + durationUs), durationUs 0 = to the end) to raw f32le
     * interleaved PCM at the decoder's output rate/channels. Result: {"path","sampleRate","channels","frames"};
     * frames = 0 when the file has no (decodable) audio.
     */
    public static void decodeAudio(long id, final String path, final String outPath, final long startUs, final long durationUs) {
        submit(id, MediaError.DECODER_FAILED, new Job() {
            @Override
            public String run(AtomicBoolean cancel) throws Exception {
                return AudioDecoder.decode(path, outPath, startUs, durationUs, cancel).toString();
            }
        });
    }

    // ---------------------------------------------------------------- gallery / share

    /** Result: {"status": "saved"|"permission_denied"|"failed", "location", "addedToAlbum", "message"}. */
    public static void saveToGallery(long id, String path, String album) {
        if (Build.VERSION.SDK_INT < 29
                && context().checkSelfPermission(Manifest.permission.WRITE_EXTERNAL_STORAGE) != PackageManager.PERMISSION_GRANTED) {
            PENDING_SAVES.put(id, new String[] {path, album});
            cancelFlag(id);
            try {
                startPickerActivity(id, PickerActivity.MODE_STORAGE_PERMISSION);
            } catch (RuntimeException e) {
                PENDING_SAVES.remove(id);
                fail(id, MediaError.wrap(MediaError.PERMISSION_DENIED, e));
            }
            return;
        }
        runSave(id, path, album);
    }

    static void onPermissionResult(long id, boolean granted) {
        String[] pending = PENDING_SAVES.remove(id);
        if (pending == null) return;
        if (!granted) {
            succeed(id, new Json().put("status", "permission_denied").put("location", "").put("addedToAlbum", false)
                    .put("message", "storage permission denied").toString());
            return;
        }
        runSave(id, pending[0], pending[1]);
    }

    private static void runSave(long id, final String path, final String album) {
        submit(id, MediaError.IO, new Job() {
            @Override
            public String run(AtomicBoolean cancel) throws Exception {
                return GallerySaver.save(context(), new File(path), album, cancel).toString();
            }
        });
    }

    /** Opens the system share sheet (ACTION_SEND) for a file inside the app's storage. */
    public static void share(String path, String caption) {
        final Context ctx = context();
        File file = new File(path);
        Uri uri = MediaFileProvider.uriForFile(ctx, file);
        if (uri == null) {
            Log.e(TAG, "share: file is outside the shareable app directories: " + path);
            return;
        }
        Intent send = new Intent(Intent.ACTION_SEND);
        send.setType(MediaFileProvider.mimeTypeOf(file.getName()));
        send.putExtra(Intent.EXTRA_STREAM, uri);
        if (caption != null && caption.length() > 0) send.putExtra(Intent.EXTRA_TEXT, caption);
        send.setClipData(ClipData.newRawUri(file.getName(), uri));
        send.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        final Intent chooser = Intent.createChooser(send, null);
        chooser.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION);
        final Activity activity = currentActivity();
        if (activity != null) {
            activity.runOnUiThread(new Runnable() {
                @Override
                public void run() {
                    try {
                        activity.startActivity(chooser);
                    } catch (RuntimeException e) {
                        Log.e(TAG, "share failed", e);
                    }
                }
            });
        } else {
            chooser.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            ctx.startActivity(chooser);
        }
    }

    // ---------------------------------------------------------------- signals

    /** {"thermal": -1..3, "lowPower", "reduceMotion", "reduceTransparency", "freeSpaceBytes"} (C# NativeSignalsDto). */
    public static String getSignals() {
        Context ctx = context();
        PowerManager pm = (PowerManager) ctx.getSystemService(Context.POWER_SERVICE);
        int thermal = -1;
        if (pm != null && Build.VERSION.SDK_INT >= 29) {
            switch (pm.getCurrentThermalStatus()) {
                case PowerManager.THERMAL_STATUS_NONE:
                    thermal = 0;
                    break;
                case PowerManager.THERMAL_STATUS_LIGHT:
                case PowerManager.THERMAL_STATUS_MODERATE:
                    thermal = 1;
                    break;
                case PowerManager.THERMAL_STATUS_SEVERE:
                    thermal = 2;
                    break;
                default:
                    thermal = 3;
                    break;
            }
        }
        boolean lowPower = pm != null && pm.isPowerSaveMode();
        boolean reduceMotion;
        if (Build.VERSION.SDK_INT >= 26) {
            reduceMotion = !ValueAnimator.areAnimatorsEnabled();
        } else {
            reduceMotion = Settings.Global.getFloat(ctx.getContentResolver(), Settings.Global.ANIMATOR_DURATION_SCALE, 1f) == 0f;
        }
        long free = -1;
        try {
            free = new StatFs(ctx.getFilesDir().getAbsolutePath()).getAvailableBytes();
        } catch (RuntimeException e) {
            Log.w(TAG, "StatFs failed", e);
        }
        return new Json().put("thermal", thermal).put("lowPower", lowPower).put("reduceMotion", reduceMotion)
                .put("reduceTransparency", false).put("freeSpaceBytes", free).toString();
    }
}
