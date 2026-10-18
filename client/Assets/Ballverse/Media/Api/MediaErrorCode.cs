namespace Ballverse.Media
{
    /// <summary>
    /// Media failure categories. Native code reports them as UPPER_SNAKE strings (e.g. "UNSUPPORTED_FORMAT");
    /// unknown strings map to <see cref="Unknown"/>. The UI shows a readable text per code (spec §3.2: "choose another").
    /// </summary>
    public enum MediaErrorCode
    {
        Unknown = 0,
        /// <summary>The platform or backend lacks the feature.</summary>
        NotSupported = 1,
        FileNotFound = 2,
        /// <summary>Read/write/copy failure.</summary>
        Io = 3,
        /// <summary>Not enough free space for the copy or the export.</summary>
        InsufficientSpace = 4,
        PermissionDenied = 5,
        /// <summary>Container or codec cannot be decoded on this device (e.g. AV1 without a decoder).</summary>
        UnsupportedFormat = 6,
        DrmProtected = 7,
        /// <summary>The file is damaged or not a media file.</summary>
        Corrupt = 8,
        NoVideoTrack = 9,
        /// <summary>HDR video that this device can neither tone-map nor decode to 8/10-bit buffers.</summary>
        HdrUnsupported = 10,
        DecoderFailed = 11,
        EncoderFailed = 12,
        MuxerFailed = 13,
        Timeout = 14,
        /// <summary>Another picker or exclusive operation is already running.</summary>
        Busy = 15,
        InvalidState = 16,
        /// <summary>A picked item could not be downloaded (e.g. iCloud offline).</summary>
        Download = 17,
    }
}
