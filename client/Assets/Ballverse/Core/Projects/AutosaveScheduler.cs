using System;

namespace Ballverse.Core.Projects
{
    /// <summary>
    /// Pure autosave timing (spec §3.9 "Автосохранение после каждого изменения (с задержкой 1 с) и при уходе приложения в фон").
    /// The caller reports changes and app events with a monotonic millisecond clock and asks when to save; nothing is saved
    /// when nothing changed. Saves are tracked with a change token so edits made while a save is in flight stay dirty.
    /// The caller runs at most one save at a time.
    /// </summary>
    public sealed class AutosaveScheduler
    {
        /// <summary>Debounce after the last change, spec §3.9.</summary>
        public const long DefaultDebounceMs = 1000;

        readonly long _debounceMs;
        long _changeSeq;
        long _savedSeq;
        long _dueAtMs;

        /// <param name="debounceMs">Delay after the last change before saving (default 1 s).</param>
        public AutosaveScheduler(long debounceMs = DefaultDebounceMs)
        {
            if (debounceMs < 0) throw new ArgumentOutOfRangeException(nameof(debounceMs));
            _debounceMs = debounceMs;
        }

        public long DebounceMs => _debounceMs;

        /// <summary>True when there are changes not covered by a completed save.</summary>
        public bool IsDirty => _changeSeq > _savedSeq;

        /// <summary>When the next save is due (ms), or null when clean.</summary>
        public long? NextSaveAtMs => IsDirty ? _dueAtMs : (long?)null;

        /// <summary>Records an edit at <paramref name="nowMs"/>; restarts the debounce window.</summary>
        public void MarkChanged(long nowMs)
        {
            _changeSeq++;
            _dueAtMs = nowMs + _debounceMs;
        }

        /// <summary>True when dirty and the debounce window has elapsed.</summary>
        public bool ShouldSave(long nowMs)
        {
            return IsDirty && nowMs >= _dueAtMs;
        }

        /// <summary>
        /// The app goes to the background: returns true (save immediately) when dirty, false when there is nothing to save.
        /// </summary>
        public bool OnAppBackgrounded(long nowMs)
        {
            if (!IsDirty) return false;
            if (_dueAtMs > nowMs) _dueAtMs = nowMs;
            return true;
        }

        /// <summary>Call right before serializing the document; pass the token to <see cref="CompleteSave"/> or <see cref="FailSave"/>.</summary>
        public long BeginSave()
        {
            return _changeSeq;
        }

        /// <summary>The save started with <paramref name="token"/> succeeded; changes made after it keep the scheduler dirty.</summary>
        public void CompleteSave(long token)
        {
            if (token > _changeSeq) throw new ArgumentOutOfRangeException(nameof(token));
            if (token > _savedSeq) _savedSeq = token;
        }

        /// <summary>The save failed: stays dirty and retries one debounce interval after <paramref name="nowMs"/>.</summary>
        public void FailSave(long token, long nowMs)
        {
            if (token > _changeSeq) throw new ArgumentOutOfRangeException(nameof(token));
            if (IsDirty && _dueAtMs < nowMs + _debounceMs) _dueAtMs = nowMs + _debounceMs;
        }
    }
}
