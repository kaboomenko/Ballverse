using System.Collections.Generic;

namespace Ballverse.Services
{
    /// <summary>
    /// Small persistent string store for service state (ad policy state, entitlement cache, device key).
    /// The Unity implementation is <see cref="PlayerPrefsKeyValueStore"/>; tests use <see cref="MemoryKeyValueStore"/>.
    /// </summary>
    public interface IKeyValueStore
    {
        /// <summary>The stored value or null.</summary>
        string GetString(string key);

        void SetString(string key, string value);

        void Delete(string key);

        /// <summary>Flushes pending writes to disk.</summary>
        void Save();
    }

    /// <summary>In-memory store.</summary>
    public sealed class MemoryKeyValueStore : IKeyValueStore
    {
        readonly Dictionary<string, string> _values = new Dictionary<string, string>();

        /// <summary>Number of <see cref="Save"/> calls (tests).</summary>
        public int SaveCount { get; private set; }

        public string GetString(string key)
        {
            return _values.TryGetValue(key, out string v) ? v : null;
        }

        public void SetString(string key, string value)
        {
            if (value == null) _values.Remove(key);
            else _values[key] = value;
        }

        public void Delete(string key)
        {
            _values.Remove(key);
        }

        public void Save()
        {
            SaveCount++;
        }
    }
}
