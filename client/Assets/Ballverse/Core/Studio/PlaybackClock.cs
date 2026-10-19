using System;
using Ballverse.Core.Timeline;

namespace Ballverse.Core.Studio
{
    /// <summary>
    /// Timeline clock of the studio preview (spec §3.7): play, pause, seek and the output time to draw. Time is advanced by the
    /// caller with the real frame delta; when the preview audio is running its position is the master and the clock slews
    /// towards it, so picture and sound stay within one frame (<see cref="PreviewPolicy.MaxAvDriftUs"/>). Larger drifts (audio
    /// underrun, a stalled frame) snap to the audio position. Pure, single-threaded (main thread); the audio position is
    /// passed in as a value.
    /// </summary>
    public sealed class PlaybackClock
    {
        long _durationUs;
        long _positionUs;
        int _fps;

        /// <param name="durationUs">Output duration (<see cref="StudioTimeline.DurationUs"/>), &gt; 0.</param>
        /// <param name="fps">Output frame rate, for the drift bounds.</param>
        public PlaybackClock(long durationUs, int fps)
        {
            SetDuration(durationUs, fps);
        }

        public long DurationUs => _durationUs;

        /// <summary>Current output time, µs, in [0, DurationUs − 1].</summary>
        public long PositionUs => _positionUs;

        public bool IsPlaying { get; private set; }

        /// <summary>Restart from 0 when the end is reached (preview loop toggle). Default false: stop at the end.</summary>
        public bool Loop { get; set; }

        /// <summary>Incremented by every <see cref="Seek"/> and loop wrap: drawing code resets history-dependent state (trails, decoder prefetch).</summary>
        public int Discontinuity { get; private set; }

        /// <summary>True after playback stopped at the end (cleared by <see cref="Play"/> and <see cref="Seek"/>).</summary>
        public bool ReachedEnd { get; private set; }

        /// <summary>Largest drift corrected by slewing; beyond it the clock jumps to the audio position. Two frames.</summary>
        public long SnapDriftUs => 2 * PreviewPolicy.MaxAvDriftUs(_fps);

        /// <summary>Changes the duration and fps after an edit (keeps the position, clamped).</summary>
        public void SetDuration(long durationUs, int fps)
        {
            if (durationUs <= 0) throw new ArgumentOutOfRangeException(nameof(durationUs));
            OutputFps.Validate(fps, nameof(fps));
            _durationUs = durationUs;
            _fps = fps;
            if (_positionUs > durationUs - 1) _positionUs = durationUs - 1;
        }

        /// <summary>Starts playback; from the start when the end had been reached.</summary>
        public void Play()
        {
            if (ReachedEnd || _positionUs >= _durationUs - 1)
            {
                _positionUs = 0;
                Discontinuity++;
            }
            ReachedEnd = false;
            IsPlaying = true;
        }

        public void Pause()
        {
            IsPlaying = false;
        }

        /// <summary>Jumps to <paramref name="outputUs"/> (clamped); playback state is kept.</summary>
        public void Seek(long outputUs)
        {
            _positionUs = Clamp(outputUs, 0, _durationUs - 1);
            ReachedEnd = false;
            Discontinuity++;
        }

        /// <summary>
        /// Advances by <paramref name="realDeltaUs"/> while playing. With <paramref name="audioPositionUs"/> (the output time the
        /// listener hears now) the clock follows the audio: drift up to <see cref="SnapDriftUs"/> is removed by 1/8 per call,
        /// larger drift snaps. Returns the new position.
        /// </summary>
        public long Advance(long realDeltaUs, long? audioPositionUs = null)
        {
            if (!IsPlaying) return _positionUs;
            if (realDeltaUs < 0) realDeltaUs = 0;
            long predicted = _positionUs + realDeltaUs;
            if (audioPositionUs.HasValue)
            {
                long audio = audioPositionUs.Value;
                long drift = predicted - audio;
                predicted = Math.Abs(drift) > SnapDriftUs ? audio : predicted - drift / 8;
            }
            if (predicted >= _durationUs)
            {
                if (Loop)
                {
                    predicted %= _durationUs;
                    Discontinuity++;
                }
                else
                {
                    predicted = _durationUs - 1;
                    IsPlaying = false;
                    ReachedEnd = true;
                }
            }
            _positionUs = Clamp(predicted, 0, _durationUs - 1);
            return _positionUs;
        }

        static long Clamp(long v, long min, long max)
        {
            return v < min ? min : v > max ? max : v;
        }
    }
}
