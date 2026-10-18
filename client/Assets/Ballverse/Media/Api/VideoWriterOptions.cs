using System;
using Ballverse.Core.Timeline;

namespace Ballverse.Media
{
    /// <summary>Encoder settings of <see cref="IMediaBridge.OpenWriter"/> (spec §3.8). Immutable.</summary>
    public sealed class VideoWriterOptions
    {
        /// <summary>AAC output sample rate (fixed, spec §3.8).</summary>
        public const int AudioSampleRate = PcmFile.SampleRate;
        public const int AudioChannels = PcmFile.Channels;
        public const int DefaultAudioBitrateBps = 192000;
        public const int DefaultKeyframeIntervalSeconds = 2;
        public const int DefaultAppendTimeoutMs = 10000;

        /// <summary>Frame width, even, 16…4096.</summary>
        public int Width { get; }
        /// <summary>Frame height, even, 16…4096.</summary>
        public int Height { get; }
        /// <summary>Constant frame rate: 30 or 60.</summary>
        public int Fps { get; }
        /// <summary>Target average (VBR) video bitrate, bit/s.</summary>
        public int VideoBitrateBps { get; }
        /// <summary>IDR interval, seconds (2 per spec).</summary>
        public int KeyframeIntervalSeconds { get; }
        /// <summary>
        /// Optional audio source in the <see cref="PcmFile"/> format (raw f32le, interleaved stereo, 48 kHz), encoded to
        /// AAC-LC. Null or an empty file = no audio track (segments of the segmented export: the mix is encoded once by
        /// <see cref="IMediaBridge.ConcatSegmentsAsync"/>). Audio past the end of the video is trimmed.
        /// </summary>
        public string AudioPcmPath { get; }
        public int AudioBitrateBps { get; }
        /// <summary>How long <see cref="IVideoWriter.AppendVideoFrame(byte[], long)"/> may block waiting for a slot.</summary>
        public int AppendTimeoutMs { get; }
        /// <summary>Title/date written into the MP4 (spec §3.8), or null. Segment writers leave it null: the concatenated file gets it.</summary>
        public Mp4Metadata Metadata { get; }

        public VideoWriterOptions(int width, int height, int fps, int videoBitrateBps,
            string audioPcmPath = null, int audioBitrateBps = DefaultAudioBitrateBps,
            int keyframeIntervalSeconds = DefaultKeyframeIntervalSeconds, int appendTimeoutMs = DefaultAppendTimeoutMs,
            Mp4Metadata metadata = null)
        {
            if (width < 16 || width > 4096 || (width & 1) != 0) throw new ArgumentOutOfRangeException(nameof(width), width, "width must be even, 16..4096");
            if (height < 16 || height > 4096 || (height & 1) != 0) throw new ArgumentOutOfRangeException(nameof(height), height, "height must be even, 16..4096");
            OutputFps.Validate(fps, nameof(fps));
            if (videoBitrateBps < 100000 || videoBitrateBps > 100000000) throw new ArgumentOutOfRangeException(nameof(videoBitrateBps));
            if (audioBitrateBps < 32000 || audioBitrateBps > 320000) throw new ArgumentOutOfRangeException(nameof(audioBitrateBps));
            if (keyframeIntervalSeconds < 1 || keyframeIntervalSeconds > 10) throw new ArgumentOutOfRangeException(nameof(keyframeIntervalSeconds));
            if (appendTimeoutMs < 1) throw new ArgumentOutOfRangeException(nameof(appendTimeoutMs));
            Width = width;
            Height = height;
            Fps = fps;
            VideoBitrateBps = videoBitrateBps;
            AudioPcmPath = string.IsNullOrEmpty(audioPcmPath) ? null : audioPcmPath;
            AudioBitrateBps = audioBitrateBps;
            KeyframeIntervalSeconds = keyframeIntervalSeconds;
            AppendTimeoutMs = appendTimeoutMs;
            Metadata = metadata == null || metadata.IsEmpty ? null : metadata;
        }

        /// <summary>Bytes of one RGBA32 input frame.</summary>
        public int FrameBytes => Width * Height * 4;

        /// <summary>Frame duration, µs (rounded up so that frame times from the export plan never collide).</summary>
        public long FrameDurationUs => (1000000L + Fps - 1) / Fps;

        /// <summary>
        /// Options for an export plan's settings (resolution, fps, bitrates, 2 s GOP). For a 2-second segment of the
        /// segmented export pass <paramref name="audioPcmPath"/> = null (video only; the PCM mix is encoded once by
        /// <see cref="IMediaBridge.ConcatSegmentsAsync"/>) and no metadata.
        /// </summary>
        public static VideoWriterOptions FromExportSettings(ExportSettings settings, string audioPcmPath, Mp4Metadata metadata = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            return new VideoWriterOptions(settings.Width, settings.Height, settings.Fps,
                checked((int)settings.VideoBitrateBps), audioPcmPath, checked((int)settings.AudioBitrateBps),
                ExportSettings.KeyframeIntervalSeconds, DefaultAppendTimeoutMs, metadata);
        }
    }
}
