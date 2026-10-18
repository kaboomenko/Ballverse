using System;

namespace Ballverse.Core.Audio
{
    /// <summary>
    /// Position in a melody for the Melody pack (spec §2.7): each qualifying hit plays the next note. At most 20 notes per
    /// sliding second; a hit over the limit does not advance the melody. After the last note the melody loops to the first
    /// note (<see cref="Loops"/> counts completed passes), so long clips keep playing the tune.
    /// </summary>
    public sealed class MelodyCursor
    {
        readonly Melody _melody;
        readonly SlidingRateWindow _window;
        int _index;
        int _loops;

        public MelodyCursor(Melody melody)
        {
            _melody = melody ?? throw new ArgumentNullException(nameof(melody));
            _window = new SlidingRateWindow(AudioConstants.MaxMelodyNotesPerWindow, AudioConstants.RateWindowFrames);
        }

        public Melody Melody => _melody;

        /// <summary>Index of the note the next accepted hit will play.</summary>
        public int NextIndex => _index;

        /// <summary>Number of times the melody wrapped from its last note to the first.</summary>
        public int Loops => _loops;

        /// <summary>
        /// Plays the next note at time <paramref name="frame"/> (48 kHz frames) if fewer than 20 notes were played in the
        /// window (frame − 1 s, frame]. On success returns the note and advances (wrapping at the end).
        /// </summary>
        public bool TryAdvance(long frame, out MelodyNote note)
        {
            if (!_window.TryAcquire(frame))
            {
                note = default;
                return false;
            }
            note = _melody[_index];
            _index++;
            if (_index >= _melody.Count)
            {
                _index = 0;
                _loops++;
            }
            return true;
        }

        public void Reset()
        {
            _index = 0;
            _loops = 0;
            _window.Reset();
        }
    }
}
