namespace Ballverse.Core.Audio.Mastering
{
    /// <summary>
    /// Result of an ITU-R BS.1770-4 / EBU R128 measurement (spec §3.6, §7.8). Never contains NaN or −∞: when no 400 ms
    /// block passes the −70 LUFS absolute gate (silence, or input shorter than one block) <see cref="IsSilent"/> is true and
    /// <see cref="IntegratedLufs"/> equals <see cref="SilenceFloor"/>; peaks of digital silence are also reported at the floor.
    /// </summary>
    public struct LoudnessResult
    {
        /// <summary>Floor reported for silence, in LUFS / dBTP / dBFS (−144 ≈ 24-bit noise floor, below any gate).</summary>
        public const double SilenceFloor = -144.0;

        /// <summary>Integrated (gated) loudness, LUFS.</summary>
        public double IntegratedLufs;
        /// <summary>No block above the absolute gate.</summary>
        public bool IsSilent;
        /// <summary>4× oversampled peak, linear.</summary>
        public double TruePeak;
        /// <summary>4× oversampled peak, dBTP (floored).</summary>
        public double TruePeakDbtp;
        /// <summary>Largest absolute sample value, linear.</summary>
        public double SamplePeak;
        /// <summary>Number of complete 400 ms blocks (75 % overlap).</summary>
        public int BlockCount;
        /// <summary>Blocks that passed both gates.</summary>
        public int GatedBlockCount;
        /// <summary>Frames measured.</summary>
        public long Frames;
    }
}
