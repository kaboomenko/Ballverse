using System;
using System.Threading;
using Ballverse.Core.Audio;

namespace Ballverse.Core.Studio
{
    /// <summary>
    /// Plays the offline studio mix (interleaved stereo float, 48 kHz — the same samples the export encodes) on the engine's audio
    /// thread for the preview (spec §3.7: sound within one frame of the picture). The main thread swaps the buffer, seeks and
    /// starts/stops; the audio thread calls <see cref="Read"/> (Unity <c>OnAudioFilterRead</c>), which resamples linearly to the
    /// device rate, maps stereo to the device channel count and publishes the played position for the clock.
    /// <para>Thread safety: the mix buffer is swapped as an immutable reference; seek and play requests are single words
    /// written with <see cref="Volatile"/> / <see cref="Interlocked"/>; the audio thread owns the read position. Starts and seeks
    /// fade in over 5 ms and stops fade out, so scrubbing does not click. No allocation in <see cref="Read"/>.</para>
    /// </summary>
    public sealed class PcmPlaybackCursor
    {
        /// <summary>Fade length after a start or seek and before a stop, frames at the device rate (5 ms at 48 kHz).</summary>
        public const int FadeFrames = 240;

        float[] _buffer = Array.Empty<float>();
        long _seekRequest = -1;         // source frame, or -1
        int _playing;                   // requested state, 0/1
        long _publishedFrame;           // source frame position after the last Read
        double _position;               // audio thread only
        float _gain;                    // audio thread only: current fade gain 0..1

        /// <summary>Replaces the mix (main thread). The position is kept (clamped by reads).</summary>
        public void SetBuffer(float[] interleavedStereo)
        {
            if (interleavedStereo == null) throw new ArgumentNullException(nameof(interleavedStereo));
            if ((interleavedStereo.Length & 1) != 0) throw new ArgumentException("interleaved stereo length must be even", nameof(interleavedStereo));
            Volatile.Write(ref _buffer, interleavedStereo);
        }

        /// <summary>Frames of the current mix.</summary>
        public long FrameCount => Volatile.Read(ref _buffer).Length / 2;

        /// <summary>Requested play state (main thread writes, audio thread reads).</summary>
        public bool Playing
        {
            get => Volatile.Read(ref _playing) != 0;
            set => Volatile.Write(ref _playing, value ? 1 : 0);
        }

        /// <summary>Asks the audio thread to continue from source frame <paramref name="frame"/> (48 kHz).</summary>
        public void Seek(long frame)
        {
            Interlocked.Exchange(ref _seekRequest, Math.Max(0, frame));
            Interlocked.Exchange(ref _publishedFrame, Math.Max(0, frame));
        }

        /// <summary>Seek to an output time in µs.</summary>
        public void SeekUs(long outputUs)
        {
            Seek(AudioConstants.MicrosToFrames(Math.Max(0, outputUs)));
        }

        /// <summary>Source frame (48 kHz) following the last block handed to the device.</summary>
        public long PositionFrames => Interlocked.Read(ref _publishedFrame);

        /// <summary>Output time, µs, following the last block handed to the device (device latency not included).</summary>
        public long PositionUs => PositionFrames * 1000000L / AudioConstants.SampleRate;

        /// <summary>
        /// Audio thread: fills <paramref name="data"/> (interleaved, <paramref name="channels"/> per frame) at
        /// <paramref name="outputRate"/> Hz and advances. Mono devices get (L + R) / 2; channels beyond two get silence.
        /// Returns the number of frames that carried mix samples (0 while stopped or past the end).
        /// </summary>
        public int Read(float[] data, int channels, int outputRate)
        {
            if (data == null || channels <= 0 || outputRate <= 0) return 0;
            int frames = data.Length / channels;
            long seek = Interlocked.Exchange(ref _seekRequest, -1);
            if (seek >= 0)
            {
                _position = seek;
                _gain = 0f;
            }
            float[] buf = Volatile.Read(ref _buffer);
            long srcFrames = buf.Length / 2;
            bool playing = Volatile.Read(ref _playing) != 0;
            double step = (double)AudioConstants.SampleRate / outputRate;
            float fadeStep = 1f / FadeFrames;
            int produced = 0;
            for (int f = 0; f < frames; f++)
            {
                int o = f * channels;
                if (playing) _gain = Math.Min(1f, _gain + fadeStep);
                else _gain = Math.Max(0f, _gain - fadeStep);
                float l = 0f, r = 0f;
                if (_gain > 0f && _position < srcFrames)
                {
                    long i0 = (long)_position;
                    float frac = (float)(_position - i0);
                    long i1 = Math.Min(i0 + 1, srcFrames - 1);
                    l = (buf[i0 * 2] + (buf[i1 * 2] - buf[i0 * 2]) * frac) * _gain;
                    r = (buf[i0 * 2 + 1] + (buf[i1 * 2 + 1] - buf[i0 * 2 + 1]) * frac) * _gain;
                    produced++;
                }
                if (_gain > 0f || playing) _position += step;
                if (channels == 1)
                {
                    data[o] = (l + r) * 0.5f;
                }
                else
                {
                    data[o] = l;
                    data[o + 1] = r;
                    for (int c = 2; c < channels; c++) data[o + c] = 0f;
                }
            }
            if (_position > srcFrames) _position = srcFrames;
            Interlocked.Exchange(ref _publishedFrame, (long)_position);
            return produced;
        }
    }
}
