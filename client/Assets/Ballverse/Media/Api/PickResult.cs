namespace Ballverse.Media
{
    public enum PickStatus
    {
        Picked = 0,
        /// <summary>The user dismissed the picker without choosing.</summary>
        Cancelled = 1,
    }

    /// <summary>Result of <see cref="IMediaBridge.PickVideoAsync"/>.</summary>
    public sealed class PickResult
    {
        public PickStatus Status { get; }
        /// <summary>Sandbox copy of the picked video (null when cancelled).</summary>
        public string Path { get; }
        /// <summary>Probe of the copy (null when cancelled).</summary>
        public MediaInfo Info { get; }

        public PickResult(PickStatus status, string path, MediaInfo info)
        {
            Status = status;
            Path = path;
            Info = info;
        }

        public static readonly PickResult Cancelled = new PickResult(PickStatus.Cancelled, null, null);
    }
}
