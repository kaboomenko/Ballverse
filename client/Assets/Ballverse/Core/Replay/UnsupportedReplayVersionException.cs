using System.IO;

namespace Ballverse.Core.Replay
{
    /// <summary>
    /// The file is a .bvr replay (magic matched) but its formatVersion is not supported by this build (spec §7.4, §3.9).
    /// Distinct from <see cref="InvalidDataException"/> (which is sealed) so the UI can offer "update the app" instead of
    /// "file is damaged"; derives from <see cref="IOException"/> like other file-read failures.
    /// </summary>
    public sealed class UnsupportedReplayVersionException : IOException
    {
        public UnsupportedReplayVersionException(ushort version)
            : base("Unsupported .bvr format version " + version + " (this build supports " + BvrFormat.CurrentVersion + ")")
        {
            Version = version;
        }

        /// <summary>The formatVersion found in the file.</summary>
        public ushort Version { get; }
    }
}
