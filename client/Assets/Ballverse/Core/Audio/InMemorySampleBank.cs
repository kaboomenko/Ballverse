using System;
using System.Collections.Generic;

namespace Ballverse.Core.Audio
{
    /// <summary>
    /// Simple <see cref="ISampleBank"/> over a dictionary (spec §7.8). Fill it before handing it to a mixer; lookups are
    /// allocation-free. Only lookups are performed, so dictionary order never reaches any output.
    /// </summary>
    public sealed class InMemorySampleBank : ISampleBank
    {
        /// <summary>Bank budget from spec §7.8: 24 MB of decoded PCM.</summary>
        public const long DefaultBudgetBytes = 24L * 1024 * 1024;

        readonly Dictionary<string, AudioSample> _samples = new Dictionary<string, AudioSample>(StringComparer.Ordinal);
        long _bytes;

        /// <summary>Total decoded size, bytes (4 per float).</summary>
        public long SizeBytes => _bytes;

        public int Count => _samples.Count;

        /// <summary>Adds or replaces a sample.</summary>
        public void Add(string id, AudioSample sample)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("id is empty", nameof(id));
            if (sample == null) throw new ArgumentNullException(nameof(sample));
            if (_samples.TryGetValue(id, out AudioSample old)) _bytes -= 4L * old.Data.Length;
            _samples[id] = sample;
            _bytes += 4L * sample.Data.Length;
        }

        public bool TryGetSample(string id, out AudioSample sample)
        {
            if (id == null)
            {
                sample = null;
                return false;
            }
            return _samples.TryGetValue(id, out sample);
        }
    }
}
