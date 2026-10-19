package android.media;

/** JVM fake for VideoWriterReleaseCheck: an AVC encoder without High profile or VBR (Baseline path). Not the Android class. */
public final class MediaCodecInfo {
    public CodecCapabilities getCapabilitiesForType(String type) {
        return new CodecCapabilities();
    }

    public static final class CodecCapabilities {
        public static final int COLOR_FormatSurface = 0x7F000789;
        public CodecProfileLevel[] profileLevels = new CodecProfileLevel[0];

        public EncoderCapabilities getEncoderCapabilities() {
            return new EncoderCapabilities();
        }
    }

    public static final class EncoderCapabilities {
        public static final int BITRATE_MODE_VBR = 1;

        public boolean isBitrateModeSupported(int mode) {
            return false;
        }
    }

    public static final class CodecProfileLevel {
        public static final int AVCProfileHigh = 8;
        public static final int AVCLevel42 = 0x2000;
        public static final int AACObjectLC = 2;
        public int profile;
        public int level;
    }
}
