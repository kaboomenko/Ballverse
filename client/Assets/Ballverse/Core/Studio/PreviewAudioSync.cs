using System;
using Ballverse.Core.Timeline;

namespace Ballverse.Core.Studio
{
    /// <summary>What drove the preview clock on the last <see cref="PreviewAudioSync.Advance"/>.</summary>
    public enum PreviewClockSource
    {
        /// <summary>Not playing.</summary>
        Stopped = 0,
        /// <summary>Picture held after a start or seek until the first new samples are audible (DSP latency), no jump back.</summary>
        Hold = 1,
        /// <summary>The audio position is the master (slewed by <see cref="PlaybackClock"/>).</summary>
        Audio = 2,
        /// <summary>No audio, or the audio thread stopped pulling samples: the picture runs on the frame delta.</summary>
        WallClock = 3,
    }

    /// <summary>
    /// Decides, every preview frame, whether the picture follows the preview audio (spec §3.7: sound within one frame of the
    /// picture) and advances the <see cref="PlaybackClock"/> accordingly. It guards against an audio output that does not run —
    /// no listener, an interruption, audio focus lost, the end of the mix — which would otherwise pin the picture: when the
    /// audio cursor has not moved for <see cref="StallUs"/> while playing, the clock runs on the wall clock, and when the cursor
    /// moves again <see cref="NeedsAudioResync"/> asks the caller to seek the audio to the picture. After a start or seek the
    /// picture is held (not moved back) until the new samples are audible, at most <see cref="MaxHoldUs"/>. Once the cursor has
    /// handed out the whole mix the picture runs on the wall clock to the end (never pinned at the last audible sample), and a
    /// loop wrap of the clock (<see cref="PlaybackClock.Loop"/>) raises <see cref="NeedsAudioResync"/> so the caller restarts the
    /// mix at the new position (QA round 2: every loop after the first one was silent).
    /// Pure, main thread; the cursor position is passed in as a value.
    /// </summary>
    public sealed class PreviewAudioSync
    {
        readonly long _stallUs;
        readonly long _maxHoldUs;
        long _nowUs;
        long _lastCursorUs = long.MinValue;
        long _lastChangeUs;
        bool _stalled;
        bool _holding;
        long _holdTargetUs;
        long _holdSinceUs;

        /// <param name="stallUs">No cursor movement for this long while playing = the audio output does not run (&gt; 0).</param>
        /// <param name="maxHoldUs">Longest picture hold after a start or seek (≥ 0).</param>
        public PreviewAudioSync(long stallUs, long maxHoldUs)
        {
            if (stallUs <= 0) throw new ArgumentOutOfRangeException(nameof(stallUs));
            if (maxHoldUs < 0) throw new ArgumentOutOfRangeException(nameof(maxHoldUs));
            _stallUs = stallUs;
            _maxHoldUs = maxHoldUs;
        }

        /// <summary>
        /// Stall threshold for an output at <paramref name="fps"/> whose audio callback delivers blocks of
        /// <paramref name="dspBlockUs"/>: two frames, but never less than three callback periods (big Android buffers).
        /// </summary>
        public static long StallThresholdUs(int fps, long dspBlockUs)
        {
            long frame = PreviewPolicy.MaxAvDriftUs(fps);
            return Math.Max(2 * frame, 3 * Math.Max(0, dspBlockUs));
        }

        /// <summary>Default sync for an output: stall threshold from <see cref="StallThresholdUs"/>, hold up to latency + stall.</summary>
        public static PreviewAudioSync ForOutput(int fps, long dspBlockUs, long latencyUs)
        {
            long stall = StallThresholdUs(fps, dspBlockUs);
            return new PreviewAudioSync(stall, Math.Max(0, latencyUs) + stall);
        }

        public long StallUs => _stallUs;
        public long MaxHoldUs => _maxHoldUs;

        /// <summary>Source of the last advance.</summary>
        public PreviewClockSource Source { get; private set; }

        /// <summary>The audio output stopped pulling samples; the clock runs free.</summary>
        public bool AudioStalled => _stalled;

        /// <summary>The stalled audio runs again: seek it to the picture and call <see cref="Start"/> (then this clears).</summary>
        public bool NeedsAudioResync { get; private set; }

        /// <summary>
        /// Playback (re)started at <paramref name="positionUs"/> with the audio cursor seeked there: hold the picture until the audio
        /// at that position is audible. <paramref name="cursorUs"/> is the cursor position right after the seek.
        /// </summary>
        public void Start(long positionUs, long cursorUs)
        {
            _holding = true;
            _holdTargetUs = positionUs;
            _holdSinceUs = _nowUs;
            _lastCursorUs = cursorUs;
            _lastChangeUs = _nowUs;
            _stalled = false;
            NeedsAudioResync = false;
        }

        /// <summary>
        /// Advances <paramref name="clock"/> by one preview frame.
        /// </summary>
        /// <param name="realDeltaUs">Real time since the previous frame.</param>
        /// <param name="audioAvailable">A mix is loaded and playing (not being rebuilt).</param>
        /// <param name="cursorUs">Audio cursor position (output µs following the last block handed to the device).</param>
        /// <param name="latencyUs">Device buffering between the cursor and the speaker.</param>
        /// <param name="audioEndUs">Output time of the end of the loaded mix (the cursor stops there); the clock's duration when
        /// omitted.</param>
        /// <returns>The clock position.</returns>
        public long Advance(PlaybackClock clock, long realDeltaUs, bool audioAvailable, long cursorUs, long latencyUs, long audioEndUs = long.MaxValue)
        {
            if (clock == null) throw new ArgumentNullException(nameof(clock));
            if (realDeltaUs < 0) realDeltaUs = 0;
            _nowUs += realDeltaUs;
            if (!clock.IsPlaying)
            {
                Source = PreviewClockSource.Stopped;
                return clock.PositionUs;
            }
            if (!audioAvailable)
            {
                _holding = false;
                Source = PreviewClockSource.WallClock;
                return clock.Advance(realDeltaUs);
            }
            // The cursor handed out the last sample: the rest is audible within the latency, the picture runs on to the end.
            bool atEnd = cursorUs >= Math.Min(audioEndUs, clock.DurationUs);
            if (cursorUs != _lastCursorUs)
            {
                _lastCursorUs = cursorUs;
                _lastChangeUs = _nowUs;
                if (_stalled)
                {
                    // The output runs again but its position is stale: the caller re-seeks it to the picture.
                    _stalled = false;
                    NeedsAudioResync = true;
                }
            }
            else if (_nowUs - _lastChangeUs > _stallUs)
            {
                _stalled = true;
                _holding = false;
            }
            if (_stalled || NeedsAudioResync || atEnd)
            {
                _holding = false;
                Source = PreviewClockSource.WallClock;
                // A stalled output (no listener, interruption) is resynced when it runs again; the end of the mix is not a stall.
                return AdvanceClock(clock, realDeltaUs, null, atEnd || NeedsAudioResync);
            }
            long audible = Math.Max(0, cursorUs - Math.Max(0, latencyUs));
            if (_holding)
            {
                if (audible < _holdTargetUs && _nowUs - _holdSinceUs <= _maxHoldUs)
                {
                    Source = PreviewClockSource.Hold;
                    return clock.PositionUs;
                }
                _holding = false;
            }
            Source = PreviewClockSource.Audio;
            return AdvanceClock(clock, realDeltaUs, audible, true);
        }

        /// <summary>Advances the clock; a loop wrap restarts the audio at the new position (<see cref="NeedsAudioResync"/>).</summary>
        long AdvanceClock(PlaybackClock clock, long realDeltaUs, long? audiblePositionUs, bool resyncOnWrap)
        {
            int before = clock.Discontinuity;
            long position = clock.Advance(realDeltaUs, audiblePositionUs);
            if (resyncOnWrap && clock.Discontinuity != before && clock.IsPlaying) NeedsAudioResync = true;
            return position;
        }
    }
}
