using System;
using System.Collections.Generic;
using Ballverse.Core.RenderGeometry;
using Ballverse.Core.RenderStream;
using UnityEngine;

namespace Ballverse.Render
{
    /// <summary>
    /// Everything besides the <see cref="RenderFrame"/> that one rendered frame depends on (spec §7.7): the stream's static
    /// tables, the render events, the look, the quality profile and the view. Reuse one instance and update its fields; the
    /// renderer detects a new stream (shape table / quantization / grid reference change) and rebinds its caches.
    /// <para>
    /// Preview and export use the same context type; export sets <see cref="Quality"/> to
    /// <see cref="QualityProfile.Export"/> (or calls <see cref="BallverseRenderer.RenderOffline"/>).
    /// </para>
    /// </summary>
    public sealed class RenderContext
    {
        /// <summary>Quantization of the stream (header).</summary>
        public RenderQuantization Quantization { get; set; } = RenderQuantization.Default;

        /// <summary>The stream's shape table (<see cref="RenderStreamReader.Shapes"/>, or the live writer's list).</summary>
        public IReadOnlyList<ShapeDefinition> Shapes { get; set; }

        /// <summary>The stream's grid layer declaration (header), or null.</summary>
        public RenderGridSpec Grid { get; set; }

        /// <summary>
        /// Label / skin string lookup by id (<see cref="RenderStreamReader.GetLabel"/>). Assign a cached delegate once
        /// (e.g. <c>ctx.Labels = reader.GetLabel;</c>) — creating it every frame would allocate.
        /// </summary>
        public Func<int, string> Labels { get; set; }

        /// <summary>Mode string id (<c>ring_escape</c>, <c>geometry_bounce</c>, <c>helix_drop</c>…): selects the view.</summary>
        public string ModeId { get; set; } = "";

        /// <summary>Tick of the frame being rendered.</summary>
        public int Tick { get; set; }

        /// <summary>
        /// Random access to past frames for trails and camera smoothing (a <see cref="RenderStreamFrameSource"/> in the studio
        /// and export, a <see cref="RenderFrameRing"/> in a live run). Null: only the frames passed to Render are remembered.
        /// </summary>
        public IRenderFrameSource FrameSource { get; set; }

        /// <summary>Render events of the run (VFX, flashes, splats, shakes). Null = no effects.</summary>
        public RenderEventLog Events { get; set; }

        /// <summary>Look parameters (theme, colours, skin, trail, background, glow, impactFx, labels, hud).</summary>
        public LookSettings Look { get; set; } = new LookSettings();

        /// <summary>Quality profile (A/B/C real time, Export offline).</summary>
        public QualityProfile Quality { get; set; } = QualityProfile.A;

        /// <summary>Camera mode; null = the view's default (fixed 2D, follow for Helix Drop).</summary>
        public CameraSettings Camera { get; set; }

        /// <summary>Helix Drop view settings (tilt from the mode's <c>viewTilt</c> parameter…).</summary>
        public HelixViewSettings Helix { get; set; } = new HelixViewSettings();

        /// <summary>"Reduce motion" accessibility setting (no camera shake).</summary>
        public bool ReduceMotion { get; set; }

        /// <summary>Emoji / flag images for the skins; null = those skins fall back to solid.</summary>
        public BallverseSkinAtlas Skins { get; set; }

        /// <summary>Font for labels; null = the theme font from <see cref="Fonts"/>, else Unity's built-in runtime font.</summary>
        public Font LabelFont { get; set; }

        /// <summary>Optional theme font set (font key → font, see <see cref="RenderTheme.FontKey"/>).</summary>
        public BallverseFontSet Fonts { get; set; }

        /// <summary>The view selected by <see cref="ModeId"/>.</summary>
        public ModeView View => ModeViews.ForMode(ModeId);
    }
}
