using System;
using System.Collections.Generic;

namespace Ballverse.App
{
    /// <summary>
    /// Keeps the screen on while at least one holder needs it (live simulation, preview playback, export).
    /// Holders take a token and dispose it when done; the screen goes back to the system sleep setting
    /// when the last token is released.
    /// </summary>
    public interface IScreenAwake
    {
        bool IsKeptAwake { get; }

        /// <summary>Reasons of the tokens currently held, for diagnostics.</summary>
        IReadOnlyCollection<string> ActiveReasons { get; }

        IDisposable Acquire(string reason);
    }

    public sealed class ScreenAwake : IScreenAwake
    {
        readonly Action<bool> _apply;
        readonly Dictionary<int, string> _holders = new Dictionary<int, string>();
        int _nextId;

        /// <param name="apply">Applies keep-awake on/off to the engine; <see cref="UnityApply"/> on device.</param>
        public ScreenAwake(Action<bool> apply)
        {
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
        }

        public bool IsKeptAwake => _holders.Count > 0;

        public IReadOnlyCollection<string> ActiveReasons => _holders.Values;

        public IDisposable Acquire(string reason)
        {
            if (string.IsNullOrEmpty(reason)) throw new ArgumentException("A reason is required", nameof(reason));
            bool wasAwake = IsKeptAwake;
            int id = ++_nextId;
            _holders.Add(id, reason);
            if (!wasAwake) _apply(true);
            return new Token(this, id);
        }

        void Release(int id)
        {
            if (!_holders.Remove(id)) return;
            if (!IsKeptAwake) _apply(false);
        }

        public static void UnityApply(bool keepAwake)
        {
            UnityEngine.Screen.sleepTimeout = keepAwake ? UnityEngine.SleepTimeout.NeverSleep : UnityEngine.SleepTimeout.SystemSetting;
        }

        sealed class Token : IDisposable
        {
            ScreenAwake _owner;
            readonly int _id;

            public Token(ScreenAwake owner, int id)
            {
                _owner = owner;
                _id = id;
            }

            public void Dispose()
            {
                ScreenAwake owner = _owner;
                if (owner == null) return;
                _owner = null;
                owner.Release(_id);
            }
        }
    }
}
