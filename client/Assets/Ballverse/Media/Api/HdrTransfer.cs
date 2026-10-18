namespace Ballverse.Media
{
    /// <summary>Transfer characteristic of the source video (spec §3.2: HDR is tone-mapped to SDR BT.709).</summary>
    public enum HdrTransfer
    {
        /// <summary>SDR (BT.709 / BT.601 / sRGB) or unknown-but-not-flagged-HDR.</summary>
        Sdr = 0,
        /// <summary>HDR10 / HDR10+ (SMPTE ST 2084).</summary>
        Pq = 1,
        /// <summary>Hybrid Log-Gamma (iPhone HDR, many Android phones).</summary>
        Hlg = 2,
        /// <summary>Dolby Vision (decoded through its HLG/PQ-compatible base layer where possible).</summary>
        DolbyVision = 3,
    }
}
