package com.ballverse.media;

import android.content.Context;
import android.media.MediaCodecInfo;
import android.media.MediaCodecList;
import android.media.MediaExtractor;
import android.media.MediaFormat;
import android.media.MediaMetadataRetriever;
import android.os.Build;

import java.io.File;
import java.io.IOException;
import java.util.Map;
import java.util.UUID;

/** MediaExtractor/MediaMetadataRetriever based probe (spec §3.2 input requirements). */
final class MediaProbe {
    private MediaProbe() {
    }

    /** Video track selection shared with {@link VideoReader}: the first video track that has a decoder. */
    static final class VideoTrack {
        int index = -1;
        MediaFormat format;
        String mime;
        String decoderName;
    }

    static Json probe(Context context, String path) throws MediaError {
        File file = new File(path);
        if (!file.isFile()) throw new MediaError(MediaError.FILE_NOT_FOUND, path);
        MediaExtractor extractor = new MediaExtractor();
        try {
            try {
                extractor.setDataSource(path);
            } catch (IOException e) {
                throw new MediaError(MediaError.CORRUPT, "cannot parse the file", e);
            }
            checkDrm(extractor);

            Json json = new Json().put("fileSizeBytes", file.length());
            long duration = 0;
            boolean anyVideo = false;
            VideoTrack video = selectVideoTrack(extractor);
            if (video.index >= 0) {
                MediaFormat f = video.format;
                int width = intOr(f, MediaFormat.KEY_WIDTH, 0);
                int height = intOr(f, MediaFormat.KEY_HEIGHT, 0);
                int rotation = rotationOf(f, path);
                double fps = frameRateOf(f);
                if (fps <= 0) fps = estimateFrameRate(path, video.index);
                int transfer = Build.VERSION.SDK_INT >= 24 ? intOr(f, MediaFormat.KEY_COLOR_TRANSFER, 0) : 0;
                int profile = intOr(f, MediaFormat.KEY_PROFILE, -1);
                duration = Math.max(duration, longOr(f, MediaFormat.KEY_DURATION, 0));
                json.put("hasVideo", true).put("width", width).put("height", height).put("rotation", rotation)
                        .put("frameRate", fps).put("videoCodec", video.mime)
                        .put("transfer", transferName(video.mime, transfer, profile))
                        .put("bitDepth", isTenBit(video.mime, profile) ? 10 : 8);
                anyVideo = true;
            } else {
                for (int i = 0; i < extractor.getTrackCount(); i++) {
                    String mime = extractor.getTrackFormat(i).getString(MediaFormat.KEY_MIME);
                    if (mime != null && mime.startsWith("video/")) {
                        throw new MediaError(MediaError.UNSUPPORTED_FORMAT, "no decoder for " + mime + " on this device");
                    }
                }
                json.put("hasVideo", false).put("width", 0).put("height", 0).put("rotation", 0).put("frameRate", 0.0)
                        .put("videoCodec", "").put("transfer", "sdr").put("bitDepth", 8);
            }

            boolean hasAudio = false;
            for (int i = 0; i < extractor.getTrackCount(); i++) {
                MediaFormat f = extractor.getTrackFormat(i);
                String mime = f.getString(MediaFormat.KEY_MIME);
                if (mime == null || !mime.startsWith("audio/")) continue;
                if (findDecoder(f) == null) continue; // undecodable audio: treat as silent video
                json.put("audioCodec", mime).put("audioSampleRate", intOr(f, MediaFormat.KEY_SAMPLE_RATE, 0))
                        .put("audioChannels", intOr(f, MediaFormat.KEY_CHANNEL_COUNT, 0));
                duration = Math.max(duration, longOr(f, MediaFormat.KEY_DURATION, 0));
                hasAudio = true;
                break;
            }
            if (!hasAudio) json.put("audioCodec", "").put("audioSampleRate", 0).put("audioChannels", 0);
            json.put("hasAudio", hasAudio);
            if (duration <= 0) duration = retrieverDurationUs(path);
            if (!anyVideo && !hasAudio) throw new MediaError(MediaError.UNSUPPORTED_FORMAT, "no decodable tracks");
            return json.put("durationUs", duration);
        } finally {
            extractor.release();
        }
    }

    static void checkDrm(MediaExtractor extractor) throws MediaError {
        Map<UUID, byte[]> pssh = extractor.getPsshInfo();
        if (pssh != null && !pssh.isEmpty()) throw new MediaError(MediaError.DRM_PROTECTED, "the file is DRM protected");
    }

    static VideoTrack selectVideoTrack(MediaExtractor extractor) {
        VideoTrack result = new VideoTrack();
        for (int i = 0; i < extractor.getTrackCount(); i++) {
            MediaFormat f = extractor.getTrackFormat(i);
            String mime = f.getString(MediaFormat.KEY_MIME);
            if (mime == null || !mime.startsWith("video/")) continue;
            String decoder = findDecoder(f);
            if (decoder == null) continue;
            result.index = i;
            result.format = f;
            result.mime = mime;
            result.decoderName = decoder;
            return result;
        }
        return result;
    }

    /** Name of a decoder supporting the track (mime, size, profile), or null. */
    static String findDecoder(MediaFormat track) {
        String mime = track.getString(MediaFormat.KEY_MIME);
        if (mime == null) return null;
        MediaFormat query;
        if (mime.startsWith("video/")) {
            query = MediaFormat.createVideoFormat(mime, intOr(track, MediaFormat.KEY_WIDTH, 0), intOr(track, MediaFormat.KEY_HEIGHT, 0));
            if (track.containsKey(MediaFormat.KEY_PROFILE)) query.setInteger(MediaFormat.KEY_PROFILE, intOr(track, MediaFormat.KEY_PROFILE, 0));
        } else {
            query = MediaFormat.createAudioFormat(mime, intOr(track, MediaFormat.KEY_SAMPLE_RATE, 44100), intOr(track, MediaFormat.KEY_CHANNEL_COUNT, 2));
        }
        try {
            return new MediaCodecList(MediaCodecList.REGULAR_CODECS).findDecoderForFormat(query);
        } catch (RuntimeException e) {
            return null;
        }
    }

    static int rotationOf(MediaFormat f, String path) {
        if (f.containsKey("rotation-degrees")) return normalizeRotation(intOr(f, "rotation-degrees", 0));
        MediaMetadataRetriever r = new MediaMetadataRetriever();
        try {
            r.setDataSource(path);
            String s = r.extractMetadata(MediaMetadataRetriever.METADATA_KEY_VIDEO_ROTATION);
            return s == null ? 0 : normalizeRotation(Integer.parseInt(s.trim()));
        } catch (RuntimeException e) {
            return 0;
        } finally {
            releaseQuietly(r);
        }
    }

    static long retrieverDurationUs(String path) {
        MediaMetadataRetriever r = new MediaMetadataRetriever();
        try {
            r.setDataSource(path);
            String s = r.extractMetadata(MediaMetadataRetriever.METADATA_KEY_DURATION);
            return s == null ? 0 : Long.parseLong(s.trim()) * 1000L;
        } catch (RuntimeException e) {
            return 0;
        } finally {
            releaseQuietly(r);
        }
    }

    static void releaseQuietly(MediaMetadataRetriever r) {
        try {
            r.release();
        } catch (Exception ignored) {
            // release() declares IOException on API 29+
        }
    }

    static int normalizeRotation(int degrees) {
        int d = ((degrees % 360) + 360) % 360;
        int snapped = (d + 45) / 90 * 90;
        return snapped == 360 ? 0 : snapped;
    }

    /** KEY_FRAME_RATE may be stored as int or float. */
    static double frameRateOf(MediaFormat f) {
        if (!f.containsKey(MediaFormat.KEY_FRAME_RATE)) return 0;
        try {
            return f.getInteger(MediaFormat.KEY_FRAME_RATE);
        } catch (ClassCastException e) {
            try {
                return f.getFloat(MediaFormat.KEY_FRAME_RATE);
            } catch (ClassCastException e2) {
                return 0;
            }
        }
    }

    /** Average frame rate over the first 120 samples (VFR-tolerant). */
    static double estimateFrameRate(String path, int track) {
        MediaExtractor ex = new MediaExtractor();
        try {
            ex.setDataSource(path);
            ex.selectTrack(track);
            long first = -1, last = -1;
            int count = 0;
            while (count < 120) {
                long t = ex.getSampleTime();
                if (t < 0) break;
                if (first < 0 || t < first) first = t;
                if (t > last) last = t;
                count++;
                if (!ex.advance()) break;
            }
            return count > 1 && last > first ? (count - 1) * 1000000.0 / (last - first) : 0;
        } catch (IOException | RuntimeException e) {
            return 0;
        } finally {
            ex.release();
        }
    }

    static boolean isTenBit(String mime, int profile) {
        if (profile < 0 || mime == null) return false;
        switch (mime) {
            case MediaFormat.MIMETYPE_VIDEO_HEVC:
                return profile == MediaCodecInfo.CodecProfileLevel.HEVCProfileMain10
                        || profile == MediaCodecInfo.CodecProfileLevel.HEVCProfileMain10HDR10
                        || profile == MediaCodecInfo.CodecProfileLevel.HEVCProfileMain10HDR10Plus;
            case MediaFormat.MIMETYPE_VIDEO_VP9:
                return profile == MediaCodecInfo.CodecProfileLevel.VP9Profile2
                        || profile == MediaCodecInfo.CodecProfileLevel.VP9Profile3
                        || profile == MediaCodecInfo.CodecProfileLevel.VP9Profile2HDR
                        || profile == MediaCodecInfo.CodecProfileLevel.VP9Profile3HDR
                        || profile == MediaCodecInfo.CodecProfileLevel.VP9Profile2HDR10Plus
                        || profile == MediaCodecInfo.CodecProfileLevel.VP9Profile3HDR10Plus;
            case MediaFormat.MIMETYPE_VIDEO_AV1:
                return profile == MediaCodecInfo.CodecProfileLevel.AV1ProfileMain10
                        || profile == MediaCodecInfo.CodecProfileLevel.AV1ProfileMain10HDR10
                        || profile == MediaCodecInfo.CodecProfileLevel.AV1ProfileMain10HDR10Plus;
            case MediaFormat.MIMETYPE_VIDEO_DOLBY_VISION:
                return true;
            default:
                return false;
        }
    }

    /** "sdr" | "pq" | "hlg" | "dolbyvision" from mime, KEY_COLOR_TRANSFER and profile. */
    static String transferName(String mime, int transfer, int profile) {
        if (MediaFormat.MIMETYPE_VIDEO_DOLBY_VISION.equals(mime)) return "dolbyvision";
        if (transfer == MediaFormat.COLOR_TRANSFER_ST2084) return "pq";
        if (transfer == MediaFormat.COLOR_TRANSFER_HLG) return "hlg";
        if (transfer == 0 && MediaFormat.MIMETYPE_VIDEO_HEVC.equals(mime)
                && (profile == MediaCodecInfo.CodecProfileLevel.HEVCProfileMain10HDR10
                || profile == MediaCodecInfo.CodecProfileLevel.HEVCProfileMain10HDR10Plus)) return "pq";
        return "sdr";
    }

    static int intOr(MediaFormat f, String key, int fallback) {
        if (!f.containsKey(key)) return fallback;
        try {
            return f.getInteger(key);
        } catch (ClassCastException e) {
            try {
                return (int) f.getLong(key);
            } catch (ClassCastException e2) {
                return fallback;
            }
        }
    }

    static long longOr(MediaFormat f, String key, long fallback) {
        if (!f.containsKey(key)) return fallback;
        try {
            return f.getLong(key);
        } catch (ClassCastException e) {
            try {
                return f.getInteger(key);
            } catch (ClassCastException e2) {
                return fallback;
            }
        }
    }
}
