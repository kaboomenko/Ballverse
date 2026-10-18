namespace Ballverse.Core.Audio
{
    /// <summary>
    /// Sound pack codes (spec §2.7 table, parameter <c>soundPack</c> in §2.2, default <see cref="Notes"/>).
    /// Persisted by name (CoreJson writes enums as strings, e.g. <c>"soundPack": "Melody"</c> in project.json §7.4).
    /// </summary>
    public enum SoundPackId
    {
        Notes = 0,
        Melody = 1,
        Pop = 2,
        Glass = 3,
        Drums = 4,
        Asmr = 5,
        Chiptune = 6,
        None = 7,
    }
}
