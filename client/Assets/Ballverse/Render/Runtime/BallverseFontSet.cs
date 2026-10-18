using System;
using System.Collections.Generic;
using Ballverse.Core.RenderGeometry;
using UnityEngine;

namespace Ballverse.Render
{
    /// <summary>Fonts for labels per theme font key (<see cref="RenderTheme.FontKey"/>: "default", "rounded", "pixel"…).</summary>
    [CreateAssetMenu(menuName = "Ballverse/Render/Font Set", fileName = "BallverseFontSet")]
    public sealed class BallverseFontSet : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public string key;
            public Font font;
        }

        [SerializeField] List<Entry> fonts = new List<Entry>();

        public Font Get(string key)
        {
            Font fallback = null;
            for (int i = 0; i < fonts.Count; i++)
            {
                if (fonts[i].font == null) continue;
                if (string.Equals(fonts[i].key, key, StringComparison.Ordinal)) return fonts[i].font;
                if (fallback == null && string.Equals(fonts[i].key, "default", StringComparison.Ordinal)) fallback = fonts[i].font;
            }
            return fallback;
        }
    }
}
