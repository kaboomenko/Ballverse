package com.ballverse.media;

import org.json.JSONException;
import org.json.JSONObject;

/** A media failure with a stable UPPER_SNAKE code understood by C# (Ballverse.Media.MediaErrorCode). */
final class MediaError extends Exception {
    private static final long serialVersionUID = 1L;

    static final String NOT_SUPPORTED = "NOT_SUPPORTED";
    static final String FILE_NOT_FOUND = "FILE_NOT_FOUND";
    static final String IO = "IO";
    static final String INSUFFICIENT_SPACE = "INSUFFICIENT_SPACE";
    static final String PERMISSION_DENIED = "PERMISSION_DENIED";
    static final String UNSUPPORTED_FORMAT = "UNSUPPORTED_FORMAT";
    static final String DRM_PROTECTED = "DRM_PROTECTED";
    static final String CORRUPT = "CORRUPT";
    static final String NO_VIDEO_TRACK = "NO_VIDEO_TRACK";
    static final String HDR_UNSUPPORTED = "HDR_UNSUPPORTED";
    static final String DECODER_FAILED = "DECODER_FAILED";
    static final String ENCODER_FAILED = "ENCODER_FAILED";
    static final String MUXER_FAILED = "MUXER_FAILED";
    static final String TIMEOUT = "TIMEOUT";
    static final String BUSY = "BUSY";
    static final String INVALID_STATE = "INVALID_STATE";

    final String code;

    MediaError(String code, String message) {
        super(message);
        this.code = code;
    }

    MediaError(String code, String message, Throwable cause) {
        super(message + (cause != null ? " (" + cause + ")" : ""), cause);
        this.code = code;
    }

    /** Wraps any throwable, keeping MediaError codes. */
    static MediaError wrap(String fallbackCode, Throwable t) {
        if (t instanceof MediaError) return (MediaError) t;
        return new MediaError(fallbackCode, t.getClass().getSimpleName() + ": " + t.getMessage(), t);
    }

    /** "CODE|message" for synchronous calls (C#: MediaException.FromNative). */
    String toNative() {
        return code + "|" + getMessage();
    }

    String toJson() {
        try {
            JSONObject o = new JSONObject();
            o.put("code", code);
            o.put("message", String.valueOf(getMessage()));
            return o.toString();
        } catch (JSONException e) {
            return "{\"code\":\"" + code + "\",\"message\":\"\"}";
        }
    }
}
