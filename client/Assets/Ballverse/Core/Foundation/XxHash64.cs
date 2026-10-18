using System;

namespace Ballverse.Core
{
    /// <summary>
    /// XXH64 (Yann Collet), one-shot and streaming. Used for replay state hashes and parameter hashes (spec §7.4).
    /// </summary>
    public sealed class XxHash64
    {
        const ulong P1 = 11400714785074694791UL;
        const ulong P2 = 14029467366897019727UL;
        const ulong P3 = 1609587929392839161UL;
        const ulong P4 = 9650029242287828579UL;
        const ulong P5 = 2870177450012600261UL;

        readonly ulong _seed;
        ulong _v1, _v2, _v3, _v4;
        readonly byte[] _buffer = new byte[32];
        int _bufferLength;
        ulong _totalLength;

        public XxHash64(ulong seed = 0UL)
        {
            _seed = seed;
            Reset();
        }

        public void Reset()
        {
            _v1 = unchecked(_seed + P1 + P2);
            _v2 = unchecked(_seed + P2);
            _v3 = _seed;
            _v4 = unchecked(_seed - P1);
            _bufferLength = 0;
            _totalLength = 0;
        }

        public static ulong Hash(byte[] data, ulong seed = 0UL)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            return Hash(new ReadOnlySpan<byte>(data), seed);
        }

        public static ulong Hash(ReadOnlySpan<byte> data, ulong seed = 0UL)
        {
            var h = new XxHash64(seed);
            h.Append(data);
            return h.Digest();
        }

        public void Append(ReadOnlySpan<byte> data)
        {
            _totalLength += (ulong)data.Length;
            int offset = 0;

            if (_bufferLength > 0)
            {
                int take = Math.Min(32 - _bufferLength, data.Length);
                data.Slice(0, take).CopyTo(new Span<byte>(_buffer, _bufferLength, take));
                _bufferLength += take;
                offset = take;
                if (_bufferLength < 32) return;
                ConsumeStripe(_buffer, 0);
                _bufferLength = 0;
            }

            while (data.Length - offset >= 32)
            {
                _v1 = Round(_v1, ReadU64(data, offset));
                _v2 = Round(_v2, ReadU64(data, offset + 8));
                _v3 = Round(_v3, ReadU64(data, offset + 16));
                _v4 = Round(_v4, ReadU64(data, offset + 24));
                offset += 32;
            }

            int rest = data.Length - offset;
            if (rest > 0)
            {
                data.Slice(offset, rest).CopyTo(new Span<byte>(_buffer, 0, rest));
                _bufferLength = rest;
            }
        }

        public void AppendULong(ulong value)
        {
            Span<byte> tmp = stackalloc byte[8];
            for (int i = 0; i < 8; i++) tmp[i] = (byte)(value >> (8 * i));
            Append(tmp);
        }

        public void AppendInt(int value)
        {
            Span<byte> tmp = stackalloc byte[4];
            for (int i = 0; i < 4; i++) tmp[i] = (byte)(value >> (8 * i));
            Append(tmp);
        }

        public ulong Digest()
        {
            ulong h;
            if (_totalLength >= 32)
            {
                h = RotL(_v1, 1) + RotL(_v2, 7) + RotL(_v3, 12) + RotL(_v4, 18);
                h = MergeRound(h, _v1);
                h = MergeRound(h, _v2);
                h = MergeRound(h, _v3);
                h = MergeRound(h, _v4);
            }
            else
            {
                h = unchecked(_seed + P5);
            }

            h = unchecked(h + _totalLength);

            var tail = new ReadOnlySpan<byte>(_buffer, 0, _bufferLength);
            int i = 0;
            while (tail.Length - i >= 8)
            {
                ulong k1 = Round(0, ReadU64(tail, i));
                h ^= k1;
                h = unchecked(RotL(h, 27) * P1 + P4);
                i += 8;
            }
            if (tail.Length - i >= 4)
            {
                h ^= unchecked(ReadU32(tail, i) * P1);
                h = unchecked(RotL(h, 23) * P2 + P3);
                i += 4;
            }
            while (i < tail.Length)
            {
                h ^= unchecked(tail[i] * P5);
                h = unchecked(RotL(h, 11) * P1);
                i++;
            }

            h ^= h >> 33;
            h = unchecked(h * P2);
            h ^= h >> 29;
            h = unchecked(h * P3);
            h ^= h >> 32;
            return h;
        }

        void ConsumeStripe(byte[] b, int offset)
        {
            var s = new ReadOnlySpan<byte>(b, offset, 32);
            _v1 = Round(_v1, ReadU64(s, 0));
            _v2 = Round(_v2, ReadU64(s, 8));
            _v3 = Round(_v3, ReadU64(s, 16));
            _v4 = Round(_v4, ReadU64(s, 24));
        }

        static ulong Round(ulong acc, ulong input)
        {
            acc = unchecked(acc + input * P2);
            acc = RotL(acc, 31);
            return unchecked(acc * P1);
        }

        static ulong MergeRound(ulong acc, ulong val)
        {
            val = Round(0, val);
            acc ^= val;
            return unchecked(acc * P1 + P4);
        }

        static ulong RotL(ulong x, int r)
        {
            return (x << r) | (x >> (64 - r));
        }

        static ulong ReadU64(ReadOnlySpan<byte> s, int o)
        {
            return s[o] | ((ulong)s[o + 1] << 8) | ((ulong)s[o + 2] << 16) | ((ulong)s[o + 3] << 24)
                 | ((ulong)s[o + 4] << 32) | ((ulong)s[o + 5] << 40) | ((ulong)s[o + 6] << 48) | ((ulong)s[o + 7] << 56);
        }

        static ulong ReadU32(ReadOnlySpan<byte> s, int o)
        {
            return s[o] | ((ulong)s[o + 1] << 8) | ((ulong)s[o + 2] << 16) | ((ulong)s[o + 3] << 24);
        }
    }
}
