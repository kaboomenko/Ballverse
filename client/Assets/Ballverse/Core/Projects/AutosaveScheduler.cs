using System;

namespace Ballverse.Core.Projects
{
    /// <summary>
    /// Pure autosave timing (spec §3.9 "Автосохранение после каждого изменения (с задержкой 1 с) и при уходе приложения в фон").
    /// The caller reports changes and app events with a monotonic millisecond clock and asks when to save; nothing is saved
    /// when nothing changed. Saves are tracked with a change token so edits made while a save is in flight stay dirty.
    /// <para>The scheduler knows when a save is running (between <see cref="BeginSave"/> and <see cref="CompleteSave"/> /
    /// <see cref="FailSave"/>): <see cref="ShouldSave"/> is false meanwhile, so a caller polling every frame never starts a
    /// second, overlapping save.</para>
    /// <para>Continuous editing (a slider drag reporting a change every frame) keeps restarting the 1 s debounce; the optional
    /// maximum wait bounds that, so a save still happens at most <see cref="MaxWaitMs"/> after the first unsaved change
    /// (spec §8 ST6, no project data loss).</para>
    /// </summary>
    public sealed class AutosaveScheduler
    {
        /// <summary>Debounce after the last change, spec §3.9.</summary>
        public const long DefaultDebounceMs = 1000;

        /// <summary>Default upper bound from the first unsaved change to its save during continuous editing (this module's choice).</summary>
        public const long DefaultMaxWaitMs = 5000;

        readonly long _debounceMs;
        readonly long _maxWaitMs;
        long _changeSeq;
        long _savedSeq;
        long _dueAtMs;
        long _inFlightToken = -1;
        bool _hasPending;      // changes not covered by the running save (or by any save when none runs)
        long _firstPendingMs;  // time of the first of those changes

        /// <param name="debounceMs">Delay after the last change before saving (default 1 s). Due times saturate, so
        /// <see cref="long.MaxValue"/> means "only when the app is backgrounded".</param>
        /// <param name="maxWaitMs">Longest wait from the first unsaved change (default 5 s); 0 or <see cref="long.MaxValue"/> disables the bound.</param>
        public AutosaveScheduler(long debounceMs = DefaultDebounceMs, long maxWaitMs = DefaultMaxWaitMs)
        {
            if (debounceMs < 0) throw new ArgumentOutOfRangeException(nameof(debounceMs));
            if (maxWaitMs < 0) throw new ArgumentOutOfRangeException(nameof(maxWaitMs));
            _debounceMs = debounceMs;
            _maxWaitMs = maxWaitMs;
        }

        public long DebounceMs => _debounceMs;

        /// <summary>Upper bound from the first unsaved change to the save; 0 = none.</summary>
        public long MaxWaitMs => _maxWaitMs;

        /// <summary>True when there are changes not covered by a completed save.</summary>
        public bool IsDirty => _changeSeq > _savedSeq;

        /// <summary>True between <see cref="BeginSave"/> and the matching <see cref="CompleteSave"/> or <see cref="FailSave"/>.</summary>
        public bool IsSaving => _inFlightToken >= 0;

        /// <summary>When the next save is due (ms), or null when clean.</summary>
        public long? NextSaveAtMs => IsDirty ? _dueAtMs : (long?)null;

        /// <summary>Records an edit at <paramref name="nowMs"/>; restarts the debounce window, bounded by the maximum wait.</summary>
        public void MarkChanged(long nowMs)
        {
            if (!_hasPending)
            {
                _hasPending = true;
                _firstPendingMs = nowMs;
            }
            _changeSeq++;
            _dueAtMs = AddSaturating(nowMs, _debounceMs);
            long maxDue = AddSaturating(_firstPendingMs, _maxWaitMs);
            if (_maxWaitMs > 0 && maxDue < _dueAtMs) _dueAtMs = Math.Max(nowMs, maxDue);
        }

        /// <summary>True when dirty, no save is running and the debounce window (or the maximum wait) has elapsed.</summary>
        public bool ShouldSave(long nowMs)
        {
            return IsDirty && !IsSaving && nowMs >= _dueAtMs;
        }

        /// <summary>
        /// The app goes to the background: returns true (save immediately) when there are changes that no completed or running
        /// save covers, false otherwise. While a save is running, true means "save again right after it": <see cref="ShouldSave"/>
        /// turns true as soon as that save completes.
        /// </summary>
        public bool OnAppBackgrounded(long nowMs)
        {
            if (!IsDirty) return false;
            if (IsSaving && _inFlightToken == _changeSeq) return false;
            if (_dueAtMs > nowMs) _dueAtMs = nowMs;
            return true;
        }

        /// <summary>Call right before serializing the document; pass the token to <see cref="CompleteSave"/> or <see cref="FailSave"/>.</summary>
        public long BeginSave()
        {
            _inFlightToken = _changeSeq;
            _hasPending = false;
            return _changeSeq;
        }

        /// <summary>The save started with <paramref name="token"/> succeeded; changes made after it keep the scheduler dirty.</summary>
        public void CompleteSave(long token)
        {
            if (token > _changeSeq) throw new ArgumentOutOfRangeException(nameof(token));
            if (token > _savedSeq) _savedSeq = token;
            if (token == _inFlightToken) _inFlightToken = -1;
        }

        /// <summary>The save failed: stays dirty and retries one debounce interval after <paramref name="nowMs"/>.</summary>
        public void FailSave(long token, long nowMs)
        {
            if (token > _changeSeq) throw new ArgumentOutOfRangeException(nameof(token));
            if (token == _inFlightToken) _inFlightToken = -1;
            if (!IsDirty) return;
            if (!_hasPending)
            {
                // The failed save's changes are pending again; the maximum wait restarts from the failure.
                _hasPending = true;
                _firstPendingMs = nowMs;
            }
            long retryAt = AddSaturating(nowMs, _debounceMs);
            if (_dueAtMs < retryAt) _dueAtMs = retryAt;
        }

        /// <summary><c>a + b</c> for <c>b ≥ 0</c>, saturating at <see cref="long.MaxValue"/> (huge intervals mean "never").</summary>
        static long AddSaturating(long a, long b)
        {
            return a > long.MaxValue - b ? long.MaxValue : a + b;
        }
    }
}
