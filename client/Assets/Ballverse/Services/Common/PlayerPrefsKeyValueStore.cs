using UnityEngine;

namespace Ballverse.Services
{
    /// <summary><see cref="IKeyValueStore"/> over <see cref="PlayerPrefs"/>. Main thread only.</summary>
    public sealed class PlayerPrefsKeyValueStore : IKeyValueStore
    {
        public string GetString(string key)
        {
            return PlayerPrefs.HasKey(key) ? PlayerPrefs.GetString(key) : null;
        }

        public void SetString(string key, string value)
        {
            if (value == null) PlayerPrefs.DeleteKey(key);
            else PlayerPrefs.SetString(key, value);
        }

        public void Delete(string key)
        {
            PlayerPrefs.DeleteKey(key);
        }

        public void Save()
        {
            PlayerPrefs.Save();
        }
    }
}
