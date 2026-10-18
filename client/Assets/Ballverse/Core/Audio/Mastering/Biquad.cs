namespace Ballverse.Core.Audio.Mastering
{
    /// <summary>Second-order IIR section in transposed direct form II, double precision (normalised a0 = 1).</summary>
    internal struct Biquad
    {
        readonly double _b0, _b1, _b2, _a1, _a2;
        double _z1, _z2;

        public Biquad(double b0, double b1, double b2, double a1, double a2)
        {
            _b0 = b0;
            _b1 = b1;
            _b2 = b2;
            _a1 = a1;
            _a2 = a2;
            _z1 = 0;
            _z2 = 0;
        }

        public double Process(double x)
        {
            double y = _b0 * x + _z1;
            _z1 = _b1 * x - _a1 * y + _z2;
            _z2 = _b2 * x - _a2 * y;
            return y;
        }

        public void Reset()
        {
            _z1 = 0;
            _z2 = 0;
        }

        /// <summary>BS.1770-4 K-weighting stage 1 (high-shelf pre-filter), 48 kHz coefficients.</summary>
        public static Biquad KWeightingShelf()
        {
            return new Biquad(1.53512485958697, -2.69169618940638, 1.19839281085285, -1.69065929318241, 0.73248077421585);
        }

        /// <summary>BS.1770-4 K-weighting stage 2 (RLB high-pass), 48 kHz coefficients.</summary>
        public static Biquad KWeightingHighPass()
        {
            return new Biquad(1.0, -2.0, 1.0, -1.99004745483398, 0.99007225036621);
        }
    }
}
