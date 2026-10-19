package android.media;

/** JVM fake for VideoWriterReleaseCheck. Not the Android class. */
public final class MediaCodecList {
    public static final int REGULAR_CODECS = 0;

    public MediaCodecList(int kind) {
        // nothing to do
    }

    public String findEncoderForFormat(MediaFormat format) {
        return "fake.avc.encoder";
    }
}
