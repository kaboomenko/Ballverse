using System;

namespace Ballverse.Core.Timeline
{
    /// <summary>Allowed constant output frame rates of the studio timeline and the exported file (spec §3.8: CFR 30 or 60).</summary>
    public static class OutputFps
    {
        public const int Fps30 = 30;
        public const int Fps60 = 60;

        public static bool IsValid(int fps)
        {
            return fps == Fps30 || fps == Fps60;
        }

        /// <summary>Throws <see cref="ArgumentOutOfRangeException"/> unless <paramref name="fps"/> is 30 or 60.</summary>
        public static void Validate(int fps, string paramName)
        {
            if (!IsValid(fps)) throw new ArgumentOutOfRangeException(paramName, fps, "Output fps must be 30 or 60 (spec §3.8).");
        }
    }
}
