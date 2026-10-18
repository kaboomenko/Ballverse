namespace Ballverse.Media
{
    /// <summary>Result of <see cref="IMediaBridge.FinalizeAsync"/>.</summary>
    public sealed class VideoWriterResult
    {
        /// <summary>
        /// The finished MP4. For <see cref="MediaBackend.Fake"/> this is a directory with a PNG sequence, audio.wav and
        /// manifest.json instead (see <see cref="IsSynthetic"/>).
        /// </summary>
        public string Path { get; }
        /// <summary>Video frames written (spec §3.8 acceptance: must equal the plan's frame count).</summary>
        public int FrameCount { get; }
        /// <summary>Video track duration, µs (last pts + frame duration).</summary>
        public long DurationUs { get; }
        /// <summary>The <c>moov</c> index is at the front of the file.</summary>
        public bool FastStart { get; }
        /// <summary>True when produced by the fake backend (not a playable video file).</summary>
        public bool IsSynthetic { get; }

        public VideoWriterResult(string path, int frameCount, long durationUs, bool fastStart, bool isSynthetic)
        {
            Path = path;
            FrameCount = frameCount;
            DurationUs = durationUs;
            FastStart = fastStart;
            IsSynthetic = isSynthetic;
        }
    }
}
