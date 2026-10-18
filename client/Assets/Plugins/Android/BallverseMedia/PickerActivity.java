package com.ballverse.media;

import android.Manifest;
import android.app.Activity;
import android.content.ActivityNotFoundException;
import android.content.ClipData;
import android.content.Context;
import android.content.Intent;
import android.content.pm.PackageManager;
import android.content.pm.ResolveInfo;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.ext.SdkExtensions;
import android.provider.MediaStore;

import java.lang.ref.WeakReference;

/**
 * Transparent helper activity (declared by Ballverse.Media.Editor.AndroidManifestPatcher) that hosts system UI needing
 * an activity result without touching Unity's activity:
 *  - MODE_PICK_VIDEO: Android Photo Picker (MediaStore.ACTION_PICK_IMAGES, API 33+ or SDK extension R >= 2), else the
 *    Google Play services backport (androidx PICK_IMAGES action), else ACTION_OPEN_DOCUMENT; no permissions needed.
 *  - MODE_STORAGE_PERMISSION: WRITE_EXTERNAL_STORAGE request for gallery saves on API 26-28.
 * The result is delivered to {@link MediaBridge}; the activity finishes immediately so Unity resumes.
 * The URI read grant belongs to this task's activity stack (Unity's task), so the copy may continue after finish().
 */
public final class PickerActivity extends Activity {
    static final String EXTRA_REQUEST_ID = "com.ballverse.media.REQUEST_ID";
    static final String EXTRA_MODE = "com.ballverse.media.MODE";
    static final String MODE_PICK_VIDEO = "pick_video";
    static final String MODE_STORAGE_PERMISSION = "storage_permission";

    /** androidx.activity.result.contract.ActivityResultContracts.PickVisualMedia#ACTION_SYSTEM_FALLBACK_PICK_IMAGES. */
    static final String ACTION_GMS_PICK_IMAGES = "androidx.activity.result.contract.action.PICK_IMAGES";

    private static final int REQUEST_PICK = 0xB71;
    private static final int REQUEST_PERMISSION = 0xB72;
    private static final String STATE_DELIVERED = "delivered";

    private static WeakReference<PickerActivity> sActive = new WeakReference<>(null);

    private long mRequestId;
    private String mMode;
    private boolean mDelivered;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        Intent intent = getIntent();
        mRequestId = intent.getLongExtra(EXTRA_REQUEST_ID, -1);
        mMode = intent.getStringExtra(EXTRA_MODE);
        synchronized (PickerActivity.class) {
            sActive = new WeakReference<>(this);
        }
        if (savedInstanceState != null) {
            // Recreated (e.g. after a configuration change): the system UI is still up; wait for its result.
            mDelivered = savedInstanceState.getBoolean(STATE_DELIVERED, false);
            if (mDelivered) finish();
            return;
        }
        if (MODE_STORAGE_PERMISSION.equals(mMode)) {
            requestPermissions(new String[] {Manifest.permission.WRITE_EXTERNAL_STORAGE}, REQUEST_PERMISSION);
            return;
        }
        try {
            startActivityForResult(buildPickIntent(this), REQUEST_PICK);
        } catch (ActivityNotFoundException e) {
            try {
                startActivityForResult(buildOpenDocumentIntent(), REQUEST_PICK);
            } catch (ActivityNotFoundException e2) {
                mDelivered = true;
                MediaBridge.onPickerFailed(mRequestId, new MediaError(MediaError.NOT_SUPPORTED, "no picker activity on this device"));
                finish();
            }
        }
    }

    @Override
    protected void onSaveInstanceState(Bundle outState) {
        super.onSaveInstanceState(outState);
        outState.putBoolean(STATE_DELIVERED, mDelivered);
    }

    @Override
    protected void onActivityResult(int requestCode, int resultCode, Intent data) {
        super.onActivityResult(requestCode, resultCode, data);
        if (requestCode != REQUEST_PICK) return;
        Uri uri = null;
        if (resultCode == RESULT_OK && data != null) {
            uri = data.getData();
            ClipData clip = data.getClipData();
            if (uri == null && clip != null && clip.getItemCount() > 0) uri = clip.getItemAt(0).getUri();
        }
        mDelivered = true;
        MediaBridge.onPickerResult(mRequestId, uri);
        finish();
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode != REQUEST_PERMISSION) return;
        boolean granted = grantResults.length > 0 && grantResults[0] == PackageManager.PERMISSION_GRANTED;
        mDelivered = true;
        MediaBridge.onPermissionResult(mRequestId, granted);
        finish();
    }

    @Override
    protected void onDestroy() {
        super.onDestroy();
        synchronized (PickerActivity.class) {
            if (sActive.get() == this) sActive = new WeakReference<>(null);
        }
        if (isFinishing() && !mDelivered) {
            // Finished without a result (e.g. cancelled from C#, or the system finished us): report a dismissal.
            mDelivered = true;
            if (MODE_STORAGE_PERMISSION.equals(mMode)) MediaBridge.onPermissionResult(mRequestId, false);
            else MediaBridge.onPickerResult(mRequestId, null);
        }
    }

    /** C# cancelled the request: close the system UI and this activity. */
    static void cancelRequest(final long id) {
        final PickerActivity active;
        synchronized (PickerActivity.class) {
            active = sActive.get();
        }
        if (active == null || active.mRequestId != id) return;
        active.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                active.mDelivered = true;
                active.finishActivity(REQUEST_PICK);
                active.finish();
            }
        });
    }

    /** True when the platform Photo Picker exists (same rule as androidx PickVisualMedia.isPhotoPickerAvailable). */
    static boolean isSystemPhotoPickerAvailable() {
        if (Build.VERSION.SDK_INT >= 33) return true;
        if (Build.VERSION.SDK_INT >= 30) return SdkExtensions.getExtensionVersion(Build.VERSION_CODES.R) >= 2;
        return false;
    }

    @SuppressWarnings("deprecation")
    static Intent buildPickIntent(Context context) {
        if (isSystemPhotoPickerAvailable()) {
            Intent intent = new Intent(MediaStore.ACTION_PICK_IMAGES);
            intent.setType("video/*");
            return intent;
        }
        Intent gms = new Intent(ACTION_GMS_PICK_IMAGES);
        gms.setType("video/*");
        ResolveInfo info = context.getPackageManager().resolveActivity(gms, PackageManager.MATCH_SYSTEM_ONLY);
        if (info != null && info.activityInfo != null) {
            gms.setClassName(info.activityInfo.applicationInfo.packageName, info.activityInfo.name);
            return gms;
        }
        return buildOpenDocumentIntent();
    }

    static Intent buildOpenDocumentIntent() {
        Intent doc = new Intent(Intent.ACTION_OPEN_DOCUMENT);
        doc.addCategory(Intent.CATEGORY_OPENABLE);
        doc.setType("video/*");
        return doc;
    }
}
