package com.ballverse.media;

import android.content.ContentProvider;
import android.content.ContentValues;
import android.content.Context;
import android.database.Cursor;
import android.database.MatrixCursor;
import android.net.Uri;
import android.os.ParcelFileDescriptor;
import android.provider.OpenableColumns;

import java.io.File;
import java.io.FileNotFoundException;
import java.io.IOException;
import java.util.List;
import java.util.Locale;

/**
 * Minimal read-only FileProvider replacement (no androidx dependency) that exposes files from the app's own storage
 * to share targets: content://&lt;applicationId&gt;.ballverse.media.share/&lt;root&gt;/&lt;relative path&gt; with roots
 * files (getFilesDir), cache (getCacheDir), ext-files (getExternalFilesDir(null)), ext-cache (getExternalCacheDir).
 * Declared non-exported with grantUriPermissions="true" (see Ballverse.Media.Editor.AndroidManifestPatcher); access is
 * granted per Intent through FLAG_GRANT_READ_URI_PERMISSION. Paths are canonicalised and must stay inside their root.
 */
public final class MediaFileProvider extends ContentProvider {
    static final String AUTHORITY_SUFFIX = ".ballverse.media.share";
    private static final String[] DEFAULT_COLUMNS = {OpenableColumns.DISPLAY_NAME, OpenableColumns.SIZE};

    static String authority(Context context) {
        return context.getPackageName() + AUTHORITY_SUFFIX;
    }

    /** Content URI for a file under one of the roots, or null if the file is elsewhere. */
    static Uri uriForFile(Context context, File file) {
        String path;
        try {
            path = file.getCanonicalPath();
        } catch (IOException e) {
            return null;
        }
        String[] names = {"files", "cache", "ext-files", "ext-cache"};
        File[] roots = roots(context);
        for (int i = 0; i < roots.length; i++) {
            if (roots[i] == null) continue;
            String root;
            try {
                root = roots[i].getCanonicalPath();
            } catch (IOException e) {
                continue;
            }
            if (path.startsWith(root + File.separator)) {
                String relative = path.substring(root.length() + 1);
                Uri.Builder b = new Uri.Builder().scheme("content").authority(authority(context)).appendPath(names[i]);
                for (String segment : relative.split("/")) b.appendPath(segment);
                return b.build();
            }
        }
        return null;
    }

    static String mimeTypeOf(String name) {
        String n = name.toLowerCase(Locale.ROOT);
        if (n.endsWith(".mp4") || n.endsWith(".m4v")) return "video/mp4";
        if (n.endsWith(".mov")) return "video/quicktime";
        if (n.endsWith(".png")) return "image/png";
        if (n.endsWith(".jpg") || n.endsWith(".jpeg")) return "image/jpeg";
        if (n.endsWith(".gif")) return "image/gif";
        return "application/octet-stream";
    }

    private static File[] roots(Context context) {
        return new File[] {context.getFilesDir(), context.getCacheDir(), context.getExternalFilesDir(null), context.getExternalCacheDir()};
    }

    private File fileForUri(Uri uri) throws FileNotFoundException {
        Context context = getContext();
        if (context == null) throw new FileNotFoundException("no context");
        List<String> segments = uri.getPathSegments();
        if (segments.size() < 2) throw new FileNotFoundException(uri.toString());
        int rootIndex;
        switch (segments.get(0)) {
            case "files":
                rootIndex = 0;
                break;
            case "cache":
                rootIndex = 1;
                break;
            case "ext-files":
                rootIndex = 2;
                break;
            case "ext-cache":
                rootIndex = 3;
                break;
            default:
                throw new FileNotFoundException(uri.toString());
        }
        File root = roots(context)[rootIndex];
        if (root == null) throw new FileNotFoundException(uri.toString());
        File file = root;
        for (int i = 1; i < segments.size(); i++) file = new File(file, segments.get(i));
        try {
            String canonical = file.getCanonicalPath();
            if (!canonical.startsWith(root.getCanonicalPath() + File.separator)) throw new FileNotFoundException("outside root");
            return new File(canonical);
        } catch (IOException e) {
            throw new FileNotFoundException(e.getMessage());
        }
    }

    @Override
    public boolean onCreate() {
        return true;
    }

    @Override
    public Cursor query(Uri uri, String[] projection, String selection, String[] selectionArgs, String sortOrder) {
        File file;
        try {
            file = fileForUri(uri);
        } catch (FileNotFoundException e) {
            return null;
        }
        String[] columns = projection != null ? projection : DEFAULT_COLUMNS;
        String[] cols = new String[columns.length];
        Object[] values = new Object[columns.length];
        int n = 0;
        for (String c : columns) {
            if (OpenableColumns.DISPLAY_NAME.equals(c)) {
                cols[n] = c;
                values[n++] = file.getName();
            } else if (OpenableColumns.SIZE.equals(c)) {
                cols[n] = c;
                values[n++] = file.length();
            }
        }
        String[] outCols = new String[n];
        Object[] outValues = new Object[n];
        System.arraycopy(cols, 0, outCols, 0, n);
        System.arraycopy(values, 0, outValues, 0, n);
        MatrixCursor cursor = new MatrixCursor(outCols, 1);
        cursor.addRow(outValues);
        return cursor;
    }

    @Override
    public String getType(Uri uri) {
        List<String> segments = uri.getPathSegments();
        return segments.isEmpty() ? null : mimeTypeOf(segments.get(segments.size() - 1));
    }

    @Override
    public ParcelFileDescriptor openFile(Uri uri, String mode) throws FileNotFoundException {
        if (mode == null || !mode.equals("r")) throw new SecurityException("read-only provider");
        return ParcelFileDescriptor.open(fileForUri(uri), ParcelFileDescriptor.MODE_READ_ONLY);
    }

    @Override
    public Uri insert(Uri uri, ContentValues values) {
        throw new UnsupportedOperationException("read-only provider");
    }

    @Override
    public int delete(Uri uri, String selection, String[] selectionArgs) {
        throw new UnsupportedOperationException("read-only provider");
    }

    @Override
    public int update(Uri uri, ContentValues values, String selection, String[] selectionArgs) {
        throw new UnsupportedOperationException("read-only provider");
    }
}
