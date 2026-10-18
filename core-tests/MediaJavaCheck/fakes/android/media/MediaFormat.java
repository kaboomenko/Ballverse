package android.media;

/** JVM fake for AacPcmEncoderCheck: just the MediaFormat surface AacPcmEncoder uses. Not the Android class. */
public final class MediaFormat {
    public static final String MIMETYPE_AUDIO_AAC = "audio/mp4a-latm";
    public static final String KEY_AAC_PROFILE = "aac-profile";
    public static final String KEY_BIT_RATE = "bitrate";
    public static final String KEY_MAX_INPUT_SIZE = "max-input-size";

    public static MediaFormat createAudioFormat(String mime, int sampleRate, int channelCount) {
        return new MediaFormat();
    }

    public void setInteger(String key, int value) {
        // not needed by the check
    }
}
