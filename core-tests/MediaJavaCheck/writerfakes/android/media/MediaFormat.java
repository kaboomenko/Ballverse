package android.media;

/** JVM fake for VideoWriterReleaseCheck: the MediaFormat surface VideoWriter and AacPcmEncoder use. Not the Android class. */
public final class MediaFormat {
    public static final String MIMETYPE_VIDEO_AVC = "video/avc";
    public static final String MIMETYPE_AUDIO_AAC = "audio/mp4a-latm";
    public static final String KEY_AAC_PROFILE = "aac-profile";
    public static final String KEY_BIT_RATE = "bitrate";
    public static final String KEY_MAX_INPUT_SIZE = "max-input-size";
    public static final String KEY_COLOR_FORMAT = "color-format";
    public static final String KEY_FRAME_RATE = "frame-rate";
    public static final String KEY_I_FRAME_INTERVAL = "i-frame-interval";
    public static final String KEY_BITRATE_MODE = "bitrate-mode";
    public static final String KEY_COLOR_STANDARD = "color-standard";
    public static final String KEY_COLOR_RANGE = "color-range";
    public static final String KEY_COLOR_TRANSFER = "color-transfer";
    public static final String KEY_PROFILE = "profile";
    public static final String KEY_LEVEL = "level";
    public static final int COLOR_STANDARD_BT709 = 1;
    public static final int COLOR_RANGE_LIMITED = 2;
    public static final int COLOR_TRANSFER_SDR_VIDEO = 3;

    public static MediaFormat createVideoFormat(String mime, int width, int height) {
        return new MediaFormat();
    }

    public static MediaFormat createAudioFormat(String mime, int sampleRate, int channelCount) {
        return new MediaFormat();
    }

    public void setInteger(String key, int value) {
        // not needed by the check
    }
}
