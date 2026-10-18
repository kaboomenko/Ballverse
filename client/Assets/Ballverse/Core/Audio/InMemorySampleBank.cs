using System;
using System.Collections.Generic;

namespace Ballverse.Core.Audio
{
    /// <summary>
    /// Simple <see cref="ISampleBank"/> over a dictionary (spec §7.8). Fill it before handing it to a mixer; lookups are
    /// allocation-free. Only lookups are performed, so dictionary order never reaches any output. The decoded size is
    /// limited to a budget (spec §7.8: 24 MB): <see cref="TryAdd"/> refuses and <see cref="Add"/> throws when a sample would
    /// exceed it.
    /// </summary>
    public sealed class InMemorySampleBank : ISampleBank
    {
        /// <summary>Bank budget from spec §7.8: 24 MB of decoded PCM.</summary>
        public const long DefaultBudgetBytes = 24L * 1024 * 1024;

        readonly Dictionary<string, AudioSample> _samples = new Dictionary<string, AudioSample>(StringComparer.Ordinal);
        readonly long _budget;
        long _bytes;

        /// <param name="budgetBytes">Largest total decoded size, bytes (default 24 MB, spec §7.8).</param>
        public InMemorySampleBank(long budgetBytes = DefaultBudgetBytes)
        {
            if (budgetBytes <= 0) throw new ArgumentOutOfRangeException(nameof(budgetBytes));
            _budget = budgetBytes;
        }

        /// <summary>Total decoded size, bytes (4 per float).</summary>
        public long SizeBytes => _bytes;

        /// <summary>Largest total decoded size, bytes.</summary>
        public long BudgetBytes => _budget;

        public int Count => _samples.Count;

        /// <summary>Adds or replaces a sample; throws <see cref="InvalidOperationException"/> when it would exceed the budget.</summary>
        public void Add(string id, AudioSample sample)
        {
            if (!TryAdd(id, sample)) throw new InvalidOperationException("sample bank budget of " + _budget + " bytes exceeded by '" + id + "'");
        }

        /// <summary>Adds or replaces a sample; returns false (bank unchanged) when it would exceed the budget.</summary>
        public bool TryAdd(string id, AudioSample sample)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("id is empty", nameof(id));
            if (sample == null) throw new ArgumentNullException(nameof(sample));
            long size = 4L * sample.Data.Length;
            long old = _samples.TryGetValue(id, out AudioSample previous) ? 4L * previous.Data.Length : 0;
            if (_bytes - old + size > _budget) return false;
            _samples[id] = sample;
            _bytes += size - old;
            return true;
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
