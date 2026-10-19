using System;
using System.Collections.Generic;
using Ballverse.Core.RenderGeometry;
using UnityEngine;

namespace Ballverse.Render
{
    /// <summary>
    /// Images for the emoji and flag ball skins (spec §2.2 <c>ballSkin</c>): one atlas texture and named UV rectangles.
    /// A body's skin key (stream string table, e.g. "flag/ua", "emoji/fire") selects the rectangle; unknown keys draw the
    /// solid colour. Flags must be the neutral set agreed in spec §2.2.
    /// </summary>
    [CreateAssetMenu(menuName = "Ballverse/Render/Skin Atlas", fileName = "BallverseSkinAtlas")]
    public sealed class BallverseSkinAtlas : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string key;
            [Tooltip("Normalized UV rectangle inside the atlas (x, y, width, height).")]
            public Rect uv;
        }

        [SerializeField] Texture2D atlas = null;
        [SerializeField] List<Entry> entries = new List<Entry>();

        Dictionary<string, Rect> _byKey;

        public Texture2D Atlas => atlas;

        public bool TryGetRect(string key, out Rect uv)
        {
            uv = default;
            if (key == null || atlas == null) return false;
            if (_byKey == null) Rebuild();
            return _byKey.TryGetValue(key, out uv);
        }

        void OnEnable()
        {
            _byKey = null;
        }

        void OnValidate()
        {
            _byKey = null;
        }

        void Rebuild()
        {
            _byKey = new Dictionary<string, Rect>(StringComparer.Ordinal);
            for (int i = 0; i < entries.Count; i++)
            {
                if (!string.IsNullOrEmpty(entries[i].key)) _byKey[entries[i].key] = entries[i].uv;
            }
        }
    }
}
