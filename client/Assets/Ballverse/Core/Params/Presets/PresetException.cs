using System;

namespace Ballverse.Core.Params.Presets
{
    /// <summary>Invalid preset data (spec §2.3). Remote presets never throw it; they are rejected with a reason instead.</summary>
    public sealed class PresetException : Exception
    {
        public PresetException(string message)
            : base(message)
        {
        }
    }
}
