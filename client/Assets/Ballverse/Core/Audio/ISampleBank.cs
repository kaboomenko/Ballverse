namespace Ballverse.Core.Audio
{
    /// <summary>
    /// Source of decoded samples by id (spec §7.8 sample bank). Implementations must be safe to call from the audio
    /// thread: no allocation, no blocking. A missing id makes the voice silent (the event still counted toward the limits).
    /// </summary>
    public interface ISampleBank
    {
        bool TryGetSample(string id, out AudioSample sample);
    }
}
