namespace Ballverse.Media
{
    /// <summary>Probe result of a local media file (spec §3.2 input requirements). Immutable.</summary>
    public sealed class MediaInfo
    {
        public string Path { get; }
        public long FileSizeBytes { get; }
        /// <summary>Container duration, µs.</summary>
        public long DurationUs { get; }

        public bool HasVideo { get; }
        /// <summary>Coded width before rotation.</summary>
        public int Width { get; }
        /// <summary>Coded height before rotation.</summary>
        public int Height { get; }
        /// <summary>Clockwise rotation to apply for display: 0, 90, 180 or 270.</summary>
        public int RotationDegrees { get; }
        /// <summary>Average/nominal frame rate (may be fractional or estimated for VFR; 0 if unknown).</summary>
        public double FrameRate { get; }
        /// <summary>Platform codec id, e.g. "video/avc", "video/hevc" (Android) or "avc1", "hvc1" (iOS).</summary>
        public string VideoCodec { get; }
        public HdrTransfer Transfer { get; }
        /// <summary>Luma bit depth when known (8 or 10), else 8.</summary>
        public int BitDepth { get; }

        public bool HasAudio { get; }
        public string AudioCodec { get; }
        public int AudioSampleRate { get; }
        public int AudioChannels { get; }

        public bool IsHdr => Transfer != HdrTransfer.Sdr;
        /// <summary>Width as displayed (after rotation).</summary>
        public int DisplayWidth => RotationDegrees == 90 || RotationDegrees == 270 ? Height : Width;
        /// <summary>Height as displayed (after rotation).</summary>
        public int DisplayHeight => RotationDegrees == 90 || RotationDegrees == 270 ? Width : Height;
        public double DurationSeconds => DurationUs / 1000000.0;

        public MediaInfo(
            string path, long fileSizeBytes, long durationUs,
            bool hasVideo, int width, int height, int rotationDegrees, double frameRate, string videoCodec,
            HdrTransfer transfer, int bitDepth,
            bool hasAudio, string audioCodec, int audioSampleRate, int audioChannels)
        {
            Path = path;
            FileSizeBytes = fileSizeBytes;
            DurationUs = durationUs;
            HasVideo = hasVideo;
            Width = width;
            Height = height;
            RotationDegrees = NormalizeRotation(rotationDegrees);
            FrameRate = frameRate;
            VideoCodec = videoCodec ?? string.Empty;
            Transfer = transfer;
            BitDepth = bitDepth > 0 ? bitDepth : 8;
            HasAudio = hasAudio;
            AudioCodec = audioCodec ?? string.Empty;
            AudioSampleRate = audioSampleRate;
            AudioChannels = audioChannels;
        }

        /// <summary>Snaps any angle to 0/90/180/270 (clockwise).</summary>
        public static int NormalizeRotation(int degrees)
        {
            int d = ((degrees % 360) + 360) % 360;
            int snapped = (d + 45) / 90 * 90;
            return snapped == 360 ? 0 : snapped;
        }

        public override string ToString()
        {
            return "MediaInfo(" + DisplayWidth + "x" + DisplayHeight + " rot " + RotationDegrees + ", " + DurationSeconds.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                + " s, " + FrameRate.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + " fps, " + VideoCodec + ", " + Transfer
                + (HasAudio ? ", audio " + AudioCodec + " " + AudioSampleRate + " Hz x" + AudioChannels : ", no audio") + ")";
        }
    }
}
