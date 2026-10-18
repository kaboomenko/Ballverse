namespace Ballverse.Media
{
    /// <summary>A raw PCM file in the <see cref="PcmFile"/> format.</summary>
    public sealed class PcmFileInfo
    {
        public string Path { get; }
        public long FrameCount { get; }
        public int SampleRate => PcmFile.SampleRate;
        public int Channels => PcmFile.Channels;
        public long DurationUs => FrameCount * 1000000L / PcmFile.SampleRate;

        public PcmFileInfo(string path, long frameCount)
        {
            Path = path;
            FrameCount = frameCount;
        }
    }
}
