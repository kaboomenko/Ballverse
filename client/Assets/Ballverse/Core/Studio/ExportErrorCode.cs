namespace Ballverse.Core.Studio
{
    /// <summary>
    /// Outcome codes of a studio export (spec §3.8 "Прерывания и надёжность", §7.12 single error catalogue). Stable numbers:
    /// they go to analytics and crash reports; never renumber, only append.
    /// </summary>
    public enum ExportErrorCode
    {
        /// <summary>The file was written and verified.</summary>
        None = 0,
        /// <summary>The user cancelled (temporary files deleted, spec §3.8).</summary>
        Cancelled = 1,
        /// <summary>The rewarded gate was not passed (user closed the ad sheet).</summary>
        GateDeclined = 2,
        /// <summary>Free space below 2 × estimate + 200 MB before the start, or the device ran out during the export.</summary>
        InsufficientSpace = 3,
        /// <summary>The project cannot be loaded or composed.</summary>
        ProjectInvalid = 4,
        /// <summary>The clip's render stream (or replay) is missing or corrupt.</summary>
        ClipDataMissing = 5,
        /// <summary>The user video file is missing or cannot be decoded.</summary>
        UserVideoFailed = 6,
        /// <summary>Audio decode or mix failed.</summary>
        AudioFailed = 7,
        /// <summary>The hardware encoder failed twice for the same segment (spec §3.8: one automatic retry, then offer 720p/30).</summary>
        EncoderFailed = 8,
        /// <summary>Joining segments / muxing failed.</summary>
        MuxerFailed = 9,
        /// <summary>The finished file has the wrong frame count or duration (spec §3.8 acceptance).</summary>
        VerificationFailed = 10,
        /// <summary>A file operation failed.</summary>
        Io = 11,
        /// <summary>A frame or readback did not arrive in time.</summary>
        Timeout = 12,
        /// <summary>Another export is running.</summary>
        Busy = 13,
        /// <summary>The resume state does not match the project or its files anymore (export restarts from scratch).</summary>
        ResumeInvalid = 14,
        /// <summary>Anything else.</summary>
        Unknown = 15,
    }
}
