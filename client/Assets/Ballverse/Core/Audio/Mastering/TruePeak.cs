using System;

namespace Ballverse.Core.Audio.Mastering
{
    /// <summary>
    /// True-peak estimation with 4× oversampling (ITU-R BS.1770-4 Annex 2 method, spec §3.6 "−1 dBTP").
    /// Interpolator: 12-tap-per-phase windowed sinc (Kaiser β = 5, ±6.5 samples), each phase normalised to unity DC
    /// gain; phase 0 is the sample itself. Coefficients are literals so results do not depend on platform math libraries.
    /// Measured accuracy on sines: within ±0.07 dB up to 17 kHz, −0.44 dB worst case near 19 kHz (inherent to 4×).
    /// Samples outside the buffer are zero; non-finite samples count as zero.
    /// </summary>
    public static class TruePeak
    {
        /// <summary>Taps of the interpolator: value(n + p/4) = Σ_{j=−5..6} Coefficients[p][j+5] · x[n+j].</summary>
        internal static readonly double[][] Coefficients =
        {
            new[] { 0.0, 0.0, 0.0, 0.0, 0.0, 1.0, 0.0, 0.0, 0.0, 0.0, 0.0, 0.0 },
            new[]
            {
                -0.007385261471110194, 0.01828606577729958, -0.038388931642610306, 0.07618512342407845,
                -0.16611361506904923, 0.8997157699502455, 0.292057173897445, -0.10939250726110752,
                0.05409639263974713, -0.02685058345511647, 0.011964232882048677, -0.004173859671870593,
            },
            new[]
            {
                -0.00796779402630332, 0.02106292436955104, -0.045563685456404705, 0.0907870618383155,
                -0.18881694142262653, 0.630498434697468, 0.630498434697468, -0.18881694142262653,
                0.0907870618383155, -0.045563685456404705, 0.02106292436955104, -0.00796779402630332,
            },
            new[]
            {
                -0.004173859671870593, 0.011964232882048677, -0.02685058345511647, 0.05409639263974713,
                -0.10939250726110752, 0.292057173897445, 0.8997157699502455, -0.16611361506904923,
                0.07618512342407845, -0.038388931642610306, 0.01828606577729958, -0.007385261471110194,
            },
        };

        internal const int TapsBefore = 5;
        internal const int TapsAfter = 6;
        internal const int TapCount = 12;

        /// <summary>Largest absolute value of the 4× oversampled signal over all channels (linear). Allocation-free.</summary>
        public static double Measure(ReadOnlySpan<float> interleaved, int channels)
        {
            int frames = CheckFrames(interleaved, channels);
            double max = 0;
            for (int c = 0; c < channels; c++)
            {
                for (int n = -1; n < frames; n++)
                {
                    double seg = Segment(interleaved, channels, frames, c, n);
                    if (seg > max) max = seg;
                    if (n >= 0)
                    {
                        double p = Math.Abs(Clean(interleaved[n * channels + c]));
                        if (p > max) max = p;
                    }
                }
            }
            return max;
        }

        /// <summary>True peak in dBTP, floored at <see cref="LoudnessResult.SilenceFloor"/>.</summary>
        public static double MeasureDb(ReadOnlySpan<float> interleaved, int channels)
        {
            return AudioMath.GainToDb(Measure(interleaved, channels), LoudnessResult.SilenceFloor);
        }

        /// <summary>
        /// Max |interpolated| at phases 1…3 in the interval (n, n+1) of channel <paramref name="c"/>; taps x[n−5 … n+6],
        /// zero outside the buffer (so n = −1 and n = frames−1 also cover the ringing just outside it).
        /// </summary>
        static double Segment(ReadOnlySpan<float> x, int channels, int frames, int c, int n)
        {
            double[] h1 = Coefficients[1];
            double[] h2 = Coefficients[2];
            double[] h3 = Coefficients[3];
            double v1 = 0, v2 = 0, v3 = 0;
            for (int k = 0; k < TapCount; k++)
            {
                int idx = n - TapsBefore + k;
                if (idx < 0 || idx >= frames) continue;
                double s = Clean(x[idx * channels + c]);
                v1 += h1[k] * s;
                v2 += h2[k] * s;
                v3 += h3[k] * s;
            }
            return Math.Max(Math.Abs(v1), Math.Max(Math.Abs(v2), Math.Abs(v3)));
        }

        /// <summary>NaN and ±∞ become 0 (x − x is 0 only for finite x).</summary>
        internal static double Clean(float s)
        {
            return s - s == 0f ? s : 0.0;
        }

        internal static int CheckFrames(ReadOnlySpan<float> interleaved, int channels)
        {
            if (channels <= 0) throw new ArgumentOutOfRangeException(nameof(channels));
            if (interleaved.Length % channels != 0) throw new ArgumentException("buffer length is not a whole number of frames", nameof(interleaved));
            return interleaved.Length / channels;
        }
    }
}
