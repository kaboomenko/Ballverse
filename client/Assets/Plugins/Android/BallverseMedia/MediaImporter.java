package com.ballverse.media;

import android.content.ContentResolver;
import android.content.Context;
import android.database.Cursor;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import android.os.StatFs;
import android.provider.OpenableColumns;

import java.io.File;
import java.io.FileInputStream;
import java.io.FileOutputStream;
import java.io.IOException;
import java.util.Locale;
import java.util.concurrent.atomic.AtomicBoolean;

/** Copies a picked content URI into the app sandbox via ContentResolver (spec §3.2: no re-encoding, with progress). */
final class MediaImporter {
    /** Space kept free after the copy. */
    private static final long SPACE_RESERVE_BYTES = 50L * 1024 * 1024;

    private MediaImporter() {
    }

    static File copy(Context context, Uri uri, File destDir, String baseName, long requestId, AtomicBoolean cancel) throws MediaError {
        ContentResolver resolver = context.getContentResolver();
        String mime = resolver.getType(uri);
        String displayName = null;
        long size = -1;
        Cursor cursor = null;
        try {
            cursor = resolver.query(uri, new String[] {OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE}, null, null, null);
            if (cursor != null && cursor.moveToFirst()) {
                int nameIndex = cursor.getColumnIndex(OpenableColumns.DISPLAY_NAME);
                int sizeIndex = cursor.getColumnIndex(OpenableColumns.SIZE);
                if (nameIndex >= 0 && !cursor.isNull(nameIndex)) displayName = cursor.getString(nameIndex);
                if (sizeIndex >= 0 && !cursor.isNull(sizeIndex)) size = cursor.getLong(sizeIndex);
            }
        } catch (RuntimeException e) {
            // Metadata is optional (some providers do not support the query).
        } finally {
            if (cursor != null) cursor.close();
        }

        if (!destDir.isDirectory() && !destDir.mkdirs()) throw new MediaError(MediaError.IO, "cannot create " + destDir);
        String ext = extensionFor(mime, displayName);
        File dest = new File(destDir, baseName + "." + ext);
        File part = new File(destDir, baseName + "." + ext + ".part");

        ParcelFileDescriptor pfd;
        try {
            pfd = resolver.openFileDescriptor(uri, "r");
        } catch (SecurityException e) {
            throw new MediaError(MediaError.PERMISSION_DENIED, "no read access to the picked item", e);
        } catch (IOException e) {
            throw new MediaError(MediaError.FILE_NOT_FOUND, "cannot open the picked item", e);
        }
        if (pfd == null) throw new MediaError(MediaError.FILE_NOT_FOUND, "provider returned no file");

        boolean ok = false;
        try {
            if (size <= 0) size = pfd.getStatSize();
            long free = new StatFs(destDir.getAbsolutePath()).getAvailableBytes();
            if (size > 0 && free < size + SPACE_RESERVE_BYTES) {
                throw new MediaError(MediaError.INSUFFICIENT_SPACE, "need " + size + " bytes, " + free + " free");
            }
            FileInputStream in = new FileInputStream(pfd.getFileDescriptor());
            FileOutputStream out = new FileOutputStream(part);
            try {
                byte[] buffer = new byte[1 << 20];
                long done = 0;
                long lastReport = 0;
                int n;
                while ((n = in.read(buffer)) > 0) {
                    if (cancel.get()) return dest; // caller reports cancellation; finally deletes the part file
                    out.write(buffer, 0, n);
                    done += n;
                    if (size > 0 && done - lastReport >= size / 100) {
                        lastReport = done;
                        MediaBridge.progress(requestId, Math.min(1.0, (double) done / size));
                    }
                }
                out.getFD().sync();
            } finally {
                out.close();
                in.close();
            }
            if (cancel.get()) return dest;
            if (dest.exists() && !dest.delete()) throw new MediaError(MediaError.IO, "cannot replace " + dest);
            if (!part.renameTo(dest)) throw new MediaError(MediaError.IO, "cannot rename " + part);
            MediaBridge.progress(requestId, 1.0);
            ok = true;
            return dest;
        } catch (IOException e) {
            String msg = String.valueOf(e.getMessage());
            if (msg.contains("ENOSPC")) throw new MediaError(MediaError.INSUFFICIENT_SPACE, "disk full", e);
            throw new MediaError(MediaError.IO, "copy failed", e);
        } finally {
            try {
                pfd.close();
            } catch (IOException ignored) {
                // nothing to do
            }
            if (!ok) {
                if (part.exists() && !part.delete()) part.deleteOnExit();
            }
        }
    }

    static String extensionFor(String mime, String displayName) {
        if (mime != null) {
            switch (mime.toLowerCase(Locale.ROOT)) {
                case "video/mp4":
                    return "mp4";
                case "video/quicktime":
                    return "mov";
                case "video/3gpp":
                case "video/3gpp2":
                    return "3gp";
                case "video/webm":
                    return "webm";
                case "video/x-matroska":
                    return "mkv";
                default:
                    break;
            }
        }
        if (displayName != null) {
            int dot = displayName.lastIndexOf('.');
            if (dot >= 0 && dot < displayName.length() - 1) {
                String ext = displayName.substring(dot + 1).toLowerCase(Locale.ROOT);
                if (ext.matches("[a-z0-9]{1,5}")) return ext;
            }
        }
        return "mp4";
    }
}
