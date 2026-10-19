using Ballverse.Core.Modes.RingEscape;

namespace Ballverse.Core.Modes
{
    /// <summary>
    /// The mode definitions compiled into this build (spec §7.6 "Регистрация": explicit, no reflection). Release 1 ships
    /// M01, M02, M05; M02 (<c>Core/Modes/GeometryBounce</c>) and M05 (<c>Core/Modes/HelixDrop</c>) are added here by the
    /// integration step that merges their tracks (see Framework/CONTRACT.md §5).
    /// </summary>
    public static class ReleaseModes
    {
        /// <summary>A new registry with every mode of this build.</summary>
        public static ModeRegistry CreateRegistry()
        {
            var registry = new ModeRegistry();
            registry.Register(RingEscapeMode.Instance);
            return registry;
        }
    }
}
