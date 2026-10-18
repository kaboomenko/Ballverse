using System;
using System.IO;
using System.Threading;

namespace Ballverse.Media
{
    /// <summary>
    /// Streams interleaved f32le PCM of any rate/channel count into the <see cref="PcmFile"/> format (48 kHz stereo):
    /// standard downmix of multichannel layouts (WAVE channel order, as MediaCodec outputs) and band-limited
    /// windowed-sinc resampling. Pure C#; runs on a worker thread (≈ 0.3 s per minute of audio).
    /// </summary>
    public static class PcmConverter
    {
        const int ChunkFrames = 8192;

        /// <summary>Converts <paramref name="inputPath"/> (f32le interleaved, <paramref name="inputRate"/> Hz, <paramref name="inputChannels"/>) to a PcmFile; returns output frames.</summary>
        public static long ConvertFile(string inputPath, int inputRate, int inputChannels, string outputPath, CancellationToken ct)
        {
            if (inputRate <= 0) throw new ArgumentOutOfRangeException(nameof(inputRate));
            if (inputChannels <= 0) throw new ArgumentOutOfRangeException(nameof(inputChannels));
            PcmFile.EnsureLittleEndian();
            string tmp = outputPath + ".tmp";
            long written = 0;
            using (var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            using (var output = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
            {
                var inBytes = new byte[ChunkFrames * inputChannels * sizeof(float)];
                var inSamples = new float[ChunkFrames * inputChannels];
                var stereo = new float[ChunkFrames * 2];
                var resampler = inputRate == PcmFile.SampleRate ? null : new SincResampler(inputRate, PcmFile.SampleRate, 2);
                var outBytes = new byte[0];
                int frameBytes = inputChannels * sizeof(float);
                while (true)
                {
                    ct.ThrowIfCancellationRequested();
                    int got = PcmFile.ReadFully(input, inBytes, inBytes.Length);
                    int frames = got / frameBytes;
                    if (frames > 0)
                    {
                        Buffer.BlockCopy(inBytes, 0, inSamples, 0, frames * frameBytes);
                        Downmix(inSamples, inputChannels, frames, stereo);
                    }
                    bool eof = got < inBytes.Length;
                    float[] outSamples;
                    int outCount;
                    if (resampler == null)
                    {
                        outSamples = stereo;
                        outCount = frames * 2;
                    }
                    else
                    {
                        resampler.Push(stereo, frames);
                        if (eof) resampler.EndOfInput();
                        outCount = resampler.Drain(out outSamples);
                    }
                    if (outCount > 0)
                    {
                        if (outBytes.Length < outCount * sizeof(float)) outBytes = new byte[outCount * sizeof(float)];
                        Buffer.BlockCopy(outSamples, 0, outBytes, 0, outCount * sizeof(float));
                        output.Write(outBytes, 0, outCount * sizeof(float));
                        written += outCount / 2;
                    }
                    if (eof) break;
                }
            }
            if (File.Exists(outputPath)) File.Delete(outputPath);
            File.Move(tmp, outputPath);
            return written;
        }

        /// <summary>
        /// Downmixes interleaved frames to stereo. Layouts (WAVE order): 1 mono, 2 L R, 3 L R C, 4 L R BL BR,
        /// 5 L R C BL BR, 6 L R C LFE BL BR, 7 L R C LFE BC SL SR, 8 L R C LFE BL BR SL SR. LFE is dropped; centre and
        /// surrounds enter at −3 dB; the result is normalised so that full-scale inputs cannot clip. Other counts: average.
        /// </summary>
        public static void Downmix(float[] input, int channels, int frames, float[] stereoOut)
        {
            const float m3 = 0.70710678f;
            switch (channels)
            {
                case 1:
                    for (int i = 0; i < frames; i++) { float v = input[i]; stereoOut[2 * i] = v; stereoOut[2 * i + 1] = v; }
                    return;
                case 2:
                    Array.Copy(input, 0, stereoOut, 0, frames * 2);
                    return;
            }
            // Per channel: gain to left, gain to right.
            float[] gl = new float[channels], gr = new float[channels];
            switch (channels)
            {
                case 3: Set(gl, gr, 1, 0, 0, 1, m3, m3); break;
                case 4: Set(gl, gr, 1, 0, 0, 1, m3, 0, 0, m3); break;
                case 5: Set(gl, gr, 1, 0, 0, 1, m3, m3, m3, 0, 0, m3); break;
                case 6: Set(gl, gr, 1, 0, 0, 1, m3, m3, 0, 0, m3, 0, 0, m3); break;
                case 7: Set(gl, gr, 1, 0, 0, 1, m3, m3, 0, 0, 0.5f, 0.5f, m3, 0, 0, m3); break;
                case 8: Set(gl, gr, 1, 0, 0, 1, m3, m3, 0, 0, m3, 0, 0, m3, m3, 0, 0, m3); break;
                default:
                    for (int c = 0; c < channels; c++) { gl[c] = 1f; gr[c] = 1f; }
                    break;
            }
            float sumL = 0, sumR = 0;
            for (int c = 0; c < channels; c++) { sumL += gl[c]; sumR += gr[c]; }
            float norm = 1f / Math.Max(1f, Math.Max(sumL, sumR));
            for (int i = 0; i < frames; i++)
            {
                float l = 0, r = 0;
                int b = i * channels;
                for (int c = 0; c < channels; c++)
                {
                    float v = input[b + c];
                    l += v * gl[c];
                    r += v * gr[c];
                }
                stereoOut[2 * i] = l * norm;
                stereoOut[2 * i + 1] = r * norm;
            }
        }

        static void Set(float[] gl, float[] gr, params float[] pairs)
        {
            for (int c = 0; c < gl.Length; c++) { gl[c] = pairs[2 * c]; gr[c] = pairs[2 * c + 1]; }
        }
    }

    /// <summary>
    /// Streaming band-limited resampler: Blackman-windowed sinc, 16 zero crossings per side (scaled for downsampling),
    /// cutoff at 95% of the lower Nyquist frequency, exact rational time base (no drift). Not thread-safe.
    /// </summary>
    public sealed class SincResampler
    {
        const int ZeroCrossings = 16;
        const int TableResolution = 512;

        readonly long _inRate;
        readonly long _outRate;
        readonly int _channels;
        readonly int _halfWidth;
        readonly float[] _table;

        float[] _buf = new float[0];
        int _bufFrames;
        long _bufStart;      // absolute input frame index of _buf[0]
        long _inTotal;       // input frames pushed so far
        long _outIndex;      // next output frame index
        bool _eof;
        float[] _out = new float[0];

        public SincResampler(int inputRate, int outputRate, int channels)
        {
            if (inputRate <= 0) throw new ArgumentOutOfRangeException(nameof(inputRate));
            if (outputRate <= 0) throw new ArgumentOutOfRangeException(nameof(outputRate));
            if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
            _inRate = inputRate;
            _outRate = outputRate;
            _channels = channels;
            double cutoff = 0.95 * Math.Min(1.0, (double)outputRate / inputRate);
            _halfWidth = (int)Math.Ceiling(ZeroCrossings / cutoff);
            _table = new float[_halfWidth * TableResolution + 2];
            for (int i = 0; i < _table.Length; i++)
            {
                double x = (double)i / TableResolution;
                double u = x / _halfWidth;
                if (u >= 1.0) { _table[i] = 0f; continue; }
                double y = cutoff * x;
                double sinc = y < 1e-9 ? 1.0 : Math.Sin(Math.PI * y) / (Math.PI * y);
                double window = 0.42 + 0.5 * Math.Cos(Math.PI * u) + 0.08 * Math.Cos(2.0 * Math.PI * u);
                _table[i] = (float)(cutoff * sinc * window);
            }
        }

        /// <summary>Appends interleaved input frames.</summary>
        public void Push(float[] interleaved, int frames)
        {
            if (_eof) throw new InvalidOperationException("EndOfInput already called");
            if (frames <= 0) return;
            int need = (_bufFrames + frames) * _channels;
            if (_buf.Length < need) Array.Resize(ref _buf, Math.Max(need, _buf.Length * 2));
            Array.Copy(interleaved, 0, _buf, _bufFrames * _channels, frames * _channels);
            _bufFrames += frames;
            _inTotal += frames;
        }

        /// <summary>Marks the end of input: the tail is flushed by the next <see cref="Drain"/>.</summary>
        public void EndOfInput()
        {
            _eof = true;
        }

        /// <summary>Total output frames for <paramref name="inputFrames"/> input frames: ceil(in × outRate / inRate).</summary>
        public long OutputFramesFor(long inputFrames)
        {
            return (inputFrames * _outRate + _inRate - 1) / _inRate;
        }

        /// <summary>Produces every output frame computable from the input so far; returns the sample count in <paramref name="output"/> (valid until the next call).</summary>
        public int Drain(out float[] output)
        {
            long limit = _eof ? OutputFramesFor(_inTotal) : long.MaxValue;
            int produced = 0;
            while (_outIndex < limit)
            {
                long num = _outIndex * _inRate;
                long center = num / _outRate;
                if (!_eof && center + _halfWidth >= _inTotal) break;
                double frac = (double)(num % _outRate) / _outRate;
                int need = (produced + 1) * _channels;
                if (_out.Length < need) Array.Resize(ref _out, Math.Max(need, Math.Max(1024, _out.Length * 2)));
                for (int c = 0; c < _channels; c++) _out[produced * _channels + c] = 0f;
                long first = center - _halfWidth + 1;
                long last = center + _halfWidth;
                for (long i = first; i <= last; i++)
                {
                    if (i < 0 || i >= _inTotal) continue;
                    double dist = Math.Abs(frac + (center - i));
                    double pos = dist * TableResolution;
                    int k = (int)pos;
                    if (k >= _table.Length - 1) continue;
                    float t = (float)(pos - k);
                    float h = _table[k] + (_table[k + 1] - _table[k]) * t;
                    int b = (int)(i - _bufStart) * _channels;
                    for (int c = 0; c < _channels; c++) _out[produced * _channels + c] += _buf[b + c] * h;
                }
                produced++;
                _outIndex++;
            }
            Compact();
            output = _out;
            return produced * _channels;
        }

        void Compact()
        {
            long center = _outIndex * _inRate / _outRate;
            long keepFrom = Math.Max(_bufStart, center - _halfWidth);
            int drop = (int)Math.Min(_bufFrames, keepFrom - _bufStart);
            if (drop <= 0) return;
            Array.Copy(_buf, drop * _channels, _buf, 0, (_bufFrames - drop) * _channels);
            _bufFrames -= drop;
            _bufStart += drop;
        }
    }
}
