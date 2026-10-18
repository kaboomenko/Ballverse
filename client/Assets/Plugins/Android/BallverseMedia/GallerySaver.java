package com.ballverse.media;

import android.content.ContentResolver;
import android.content.ContentValues;
import android.content.Context;
import android.media.MediaScannerConnection;
import android.net.Uri;
import android.os.Build;
import android.os.Environment;
import android.provider.MediaStore;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Saves a finished video to the shared gallery (spec §3.8): API 29+ MediaStore insert into Movies/&lt;album&gt; with
 * IS_PENDING (no permission); API 26-28 copy into the public Movies/&lt;album&gt; folder (WRITE_EXTERNAL_STORAGE, requested
 * by MediaBridge beforehand) and a media scan.
 */
final class GallerySaver {
    private GallerySaver() {
    }

    static Json save(Context context, File file, String album, AtomicBoolean cancel) throws MediaError {
        if (!file.isFile()) throw new MediaError(MediaError.FILE_NOT_FOUND, file.getPath());
        String mime = MediaFileProvider.mimeTypeOf(file.getName());
        if (Build.VERSION.SDK_INT >= 29) return saveMediaStore(context, file, album, mime, cancel);
        return saveLegacy(context, file, album, mime, cancel);
    }

    private static Json saveMediaStore(Context context, File file, String album, String mime, AtomicBoolean cancel) throws MediaError {
        ContentResolver resolver = context.getContentResolver();
        ContentValues values = new ContentValues();
        values.put(MediaStore.MediaColumns.DISPLAY_NAME, file.getName());
        values.put(MediaStore.MediaColumns.MIME_TYPE, mime);
        values.put(MediaStore.MediaColumns.RELATIVE_PATH, Environment.DIRECTORY_MOVIES + "/" + album);
        values.put(MediaStore.MediaColumns.IS_PENDING, 1);
        Uri collection = MediaStore.Video.Media.getContentUri(MediaStore.VOLUME_EXTERNAL_PRIMARY);
        Uri item;
        try {
            item = resolver.insert(collection, values);
        } catch (RuntimeException e) {
            throw new MediaError(MediaError.IO, "MediaStore insert failed", e);
        }
        if (item == null) throw new MediaError(MediaError.IO, "MediaStore insert returned null");
        boolean ok = false;
        try {
            OutputStream out = resolver.openOutputStream(item);
            if (out == null) throw new MediaError(MediaError.IO, "cannot open MediaStore output");
            try {
                copy(file, out, cancel);
            } finally {
                out.close();
            }
            if (cancel.get()) return null;
            ContentValues done = new ContentValues();
            done.put(MediaStore.MediaColumns.IS_PENDING, 0);
            resolver.update(item, done, null, null);
            ok = true;
            return new Json().put("status", "saved").put("location", item.toString()).put("addedToAlbum", true).put("message", "");
        } catch (IOException e) {
            throw new MediaError(MediaError.IO, "copy to MediaStore failed", e);
        } finally {
            if (!ok) {
                try {
                    resolver.delete(item, null, null);
                } catch (RuntimeException ignored) {
                    // best effort
                }
            }
        }
    }

    @SuppressWarnings("deprecation")
    private static Json saveLegacy(Context context, File file, String album, String mime, AtomicBoolean cancel) throws MediaError {
        File dir = new File(Environment.getExternalStoragePublicDirectory(Environment.DIRECTORY_MOVIES), album);
        if (!dir.isDirectory() && !dir.mkdirs()) throw new MediaError(MediaError.IO, "cannot create " + dir);
        File dest = uniqueFile(dir, file.getName());
        boolean ok = false;
        try {
            OutputStream out = new FileOutputStream(dest);
            try {
                copy(file, out, cancel);
            } finally {
                out.close();
            }
            if (cancel.get()) return null;
            ok = true;
        } catch (IOException e) {
            throw new MediaError(MediaError.IO, "copy to Movies failed", e);
        } finally {
            if (!ok && dest.exists() && !dest.delete()) dest.deleteOnExit();
        }
        MediaScannerConnection.scanFile(context, new String[] {dest.getAbsolutePath()}, new String[] {mime}, null);
        return new Json().put("status", "saved").put("location", dest.getAbsolutePath()).put("addedToAlbum", true).put("message", "");
    }

    private static File uniqueFile(File dir, String name) {
        File f = new File(dir, name);
        if (!f.exists()) return f;
        int dot = name.lastIndexOf('.');
        String stem = dot > 0 ? name.substring(0, dot) : name;
        String ext = dot > 0 ? name.substring(dot) : "";
        for (int i = 1; i < 1000; i++) {
            f = new File(dir, stem + " (" + i + ")" + ext);
            if (!f.exists()) return f;
        }
        return new File(dir, stem + "_" + System.currentTimeMillis() + ext);
    }

    private static void copy(File from, OutputStream out, AtomicBoolean cancel) throws IOException {
        InputStream in = new FileInputStream(from);
        try {
            byte[] buffer = new byte[1 << 20];
            int n;
            while ((n = in.read(buffer)) > 0) {
                if (cancel.get()) return;
                out.write(buffer, 0, n);
            }
        } finally {
            in.close();
        }
    }
}
