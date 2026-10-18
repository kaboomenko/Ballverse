using System;
using Ballverse.Core.RenderStream;

namespace Ballverse.Core.RenderGeometry
{
    /// <summary>How a mode's stream is drawn.</summary>
    public enum ModeView : byte
    {
        /// <summary>Flat 2D view of the arena (Ring Escape, Geometry Bounce and every planar mode).</summary>
        Standard2D = 0,
        /// <summary>Pseudo-3D tower of Helix Drop (M05 card "Камера и рендер").</summary>
        Helix = 1,
    }

    /// <summary>Mode id → view (release 1: only <c>helix_drop</c> has its own view).</summary>
    public static class ModeViews
    {
        public const string HelixDropId = "helix_drop";

        public static ModeView ForMode(string modeId)
        {
            return string.Equals(modeId, HelixDropId, StringComparison.Ordinal) ? ModeView.Helix : ModeView.Standard2D;
        }
    }

    /// <summary>One SDF ball to draw (spec §7.7 "Шары — один инстансированный draw call"), in drawing-plane metres.</summary>
    public struct BallInstance
    {
        public float X;
        public float Y;
        /// <summary>Half-axis along <see cref="SquashAngle"/> (radius × squash), metres.</summary>
        public float RadiusX;
        /// <summary>Half-axis across <see cref="SquashAngle"/>, metres.</summary>
        public float RadiusY;
        /// <summary>Orientation of the ellipse axes, radians (0 when round).</summary>
        public float SquashAngle;
        /// <summary>Body angle: rotates gradient, gloss and textured skins with the ball.</summary>
        public float SkinAngle;
        /// <summary>Base colour (0xRRGGBBAA, alpha &lt; 255 for dead bodies).</summary>
        public uint Color;
        public float Emission;
        public BallSkin Skin;
        public BallFinish Finish;
        /// <summary>Skin key string id (stream string table) for Emoji / Flag skins.</summary>
        public int SkinId;
        public int LabelId;
        public int BodyId;
        public BodyFlags Flags;
    }

    /// <summary>A label (name / number) drawn on a ball, centred at (X, Y).</summary>
    public struct LabelInstance
    {
        public float X;
        public float Y;
        /// <summary>Cap height of the text, metres.</summary>
        public float Height;
        /// <summary>Largest text width, metres (the renderer shrinks longer labels to fit).</summary>
        public float MaxWidth;
        public int LabelId;
        public uint Color;
        public int BodyId;
        /// <summary>Letter skin: the label drawn large as the ball's face.</summary>
        public bool Letter;
    }

    /// <summary>One particle to draw, in drawing-plane metres.</summary>
    public struct ParticleInstance
    {
        public float X;
        public float Y;
        public float SizeX;
        public float SizeY;
        public float Rotation;
        public uint Color;
        public float Emission;
        public ParticleShape Shape;
    }

    /// <summary>Background of the frame (procedural, drawn by one full-screen pass).</summary>
    public struct BackgroundParams
    {
        public BackgroundStyle Style;
        public uint Top;
        public uint Bottom;
        public uint GridColor;
        /// <summary>World grid spacing, metres.</summary>
        public float GridSpacing;
        public uint StarColor;
        /// <summary>Seconds since the clip start (tick / 60): star twinkle and bokeh drift.</summary>
        public float Time;
        public float Vignette;
    }

    /// <summary>Per-frame inputs of <see cref="RenderComposer.Compose"/> (reuse one instance; set fields before each frame).</summary>
    public sealed class ComposeSettings
    {
        public ModeView View { get; set; }
        public LookSettings Look { get; set; } = new LookSettings();
        public QualityProfile Quality { get; set; } = QualityProfile.A;
        /// <summary>Camera; null = the view's default (fixed for 2D, <see cref="CameraSettings.ForHelix"/> for Helix).</summary>
        public CameraSettings Camera { get; set; }
        public HelixViewSettings Helix { get; set; } = new HelixViewSettings();
        /// <summary>"Reduce motion" accessibility setting (spec §4.8): no camera shake.</summary>
        public bool ReduceMotion { get; set; }
        /// <summary>Target width in pixels (the scene buffer before render scale).</summary>
        public int CanvasWidth { get; set; } = 1080;
        public int CanvasHeight { get; set; } = 1920;

        public void Validate()
        {
            if (Look == null) throw new InvalidOperationException("ComposeSettings.Look is null");
            if (Quality == null) throw new InvalidOperationException("ComposeSettings.Quality is null");
            if (Helix == null) throw new InvalidOperationException("ComposeSettings.Helix is null");
            if (CanvasWidth <= 0 || CanvasHeight <= 0) throw new InvalidOperationException("Canvas size must be positive");
        }
    }
}
