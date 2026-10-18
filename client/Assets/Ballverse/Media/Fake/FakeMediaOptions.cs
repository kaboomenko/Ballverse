namespace Ballverse.Media
{
    /// <summary>
    /// Settings of the FAKE media backend used in the editor and on desktop (see <see cref="MediaBackend.Fake"/>).
    /// It never decodes or encodes real video: picking creates a ".bvtestpattern" descriptor, readers render a test
    /// pattern (colour bars, a moving square, the frame index as 16 binary blocks along the top), audio is a 440 Hz tone
    /// with a click every second, and writers produce a PNG sequence + audio.wav + manifest.json directory.
    /// </summary>
    public sealed class FakeMediaOptions
    {
        /// <summary>Coded size of picked test-pattern videos (before rotation).</summary>
        public int PatternWidth { get; set; } = 1920;
        public int PatternHeight { get; set; } = 1080;
        /// <summary>Rotation metadata of picked videos (90 = phone portrait video, exercises rotation handling).</summary>
        public int PatternRotation { get; set; } = 90;
        public int PatternFps { get; set; } = 30;
        public double PatternDurationSeconds { get; set; } = 15;
        public bool PatternHasAudio { get; set; } = true;

        /// <summary>Write every Nth appended frame as PNG (1 = all frames).</summary>
        public int PngFrameStride { get; set; } = 1;

        /// <summary>
        /// Extra time a disposed/finished writer takes to report its release (models a slow hardware-encoder teardown),
        /// ms. Lets editor runs and tests exercise the redo path (<see cref="IVideoWriter.ReleaseAsync"/>,
        /// <see cref="MediaBridgeExtensions.OpenWriterAsync"/>). 0 = none.
        /// </summary>
        public int SimulatedReleaseDelayMs { get; set; }

        public ThermalState Thermal { get; set; } = ThermalState.Nominal;
        public bool LowPowerMode { get; set; }
        public bool ReduceMotion { get; set; }
        public bool ReduceTransparency { get; set; }
    }
}
