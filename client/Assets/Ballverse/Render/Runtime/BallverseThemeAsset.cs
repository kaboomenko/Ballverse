using System;
using System.Collections.Generic;
using Ballverse.Core.RenderGeometry;
using UnityEngine;

namespace Ballverse.Render
{
    /// <summary>
    /// A theme authored in the editor or delivered as JSON (spec §2.10 "новые темы поставляются данными"). With
    /// <see cref="json"/> set the JSON wins (same format as <see cref="RenderThemeJson"/>); otherwise the fields are used.
    /// Register the result in a <see cref="RenderThemeCatalog"/> next to the built-in themes.
    /// </summary>
    [CreateAssetMenu(menuName = "Ballverse/Render/Theme", fileName = "BallverseTheme")]
    public sealed class BallverseThemeAsset : ScriptableObject
    {
        [SerializeField] TextAsset json = null;
        [SerializeField] string id = "Custom";
        [SerializeField] Color32[] palette = { new Color32(0, 240, 255, 255), new Color32(255, 43, 214, 255), new Color32(57, 255, 20, 255) };
        [SerializeField] Color32 arena = new Color32(125, 249, 255, 255);
        [SerializeField] Color32 arenaAlt = new Color32(176, 38, 255, 255);
        [SerializeField] Color32 danger = new Color32(255, 23, 68, 255);
        [SerializeField] Color32 accent = new Color32(255, 230, 0, 255);
        [SerializeField] Color32 muted = new Color32(58, 58, 90, 255);
        [SerializeField] Color32 text = new Color32(255, 255, 255, 255);
        [SerializeField] Color32 gold = new Color32(255, 213, 74, 255);
        [SerializeField] Color32 backgroundTop = new Color32(11, 11, 26, 255);
        [SerializeField] Color32 backgroundBottom = new Color32(26, 11, 46, 255);
        [SerializeField] BackgroundStyle background = BackgroundStyle.Gradient;
        [SerializeField] Color32 gridColor = new Color32(255, 255, 255, 20);
        [SerializeField] Color32 starColor = new Color32(255, 255, 255, 255);
        [SerializeField] BallFinish ballFinish = BallFinish.None;
        [SerializeField, Range(0f, 4f)] float bloomIntensity = 1f;
        [SerializeField, Range(0f, 8f)] float bloomThreshold = 1f;
        [SerializeField, Range(0f, 8f)] float ballEmission = 1.2f;
        [SerializeField, Range(0f, 8f)] float shapeEmission = 1.2f;
        [SerializeField, Range(0f, 8f)] float trailEmission = 1.2f;
        [SerializeField, Tooltip("Pixel rows of the retro look; 0 = off, otherwise 64–2160.")] int pixelGrid = 0;
        [SerializeField, Range(0f, 1f)] float vignette = 0f;
        [SerializeField, Range(0f, 1f)] float helixBackShade = 0.5f;
        [SerializeField] string fontKey = "default";

        /// <summary>Builds the theme; throws <see cref="FormatException"/> / <see cref="ArgumentException"/> for invalid data.</summary>
        public RenderTheme ToRenderTheme()
        {
            if (json != null) return RenderThemeJson.Parse(json.text);
            var p = new uint[palette.Length];
            for (int i = 0; i < p.Length; i++) p[i] = Pack(palette[i]);
            return new RenderTheme(id, p, Pack(arena), Pack(arenaAlt), Pack(danger), Pack(accent), Pack(muted), Pack(text), Pack(gold),
                Pack(backgroundTop), Pack(backgroundBottom), background, Pack(gridColor), Pack(starColor), ballFinish, bloomIntensity,
                bloomThreshold, ballEmission, shapeEmission, trailEmission, pixelGrid, vignette, helixBackShade, fontKey);
        }

        static uint Pack(Color32 c) => ColorMath.Pack(c.r, c.g, c.b, c.a);
    }
}
