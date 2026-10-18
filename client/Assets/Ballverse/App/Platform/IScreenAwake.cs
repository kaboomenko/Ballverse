using System;
using System.Collections.Generic;

namespace Ballverse.Platform
{
    /// <summary>
    /// Keeps the screen on while at least one holder needs it (live simulation, preview playback, export).
    /// Holders take a token and dispose it when done; the screen goes back to the system sleep setting when the last
    /// token is released. This is the only writer of the engine's sleep timeout: features must not set
    /// Screen.sleepTimeout themselves, or one holder's release would switch the screen off under another.
    /// </summary>
    public interface IScreenAwake
    {
        bool IsKeptAwake { get; }

        /// <summary>Reasons of the tokens currently held, for diagnostics.</summary>
        IReadOnlyCollection<string> ActiveReasons { get; }

        /// <summary>Takes a keep-awake token. Disposing it releases it; disposing twice is a no-op.</summary>
        IDisposable Acquire(string reason);
    }
}
