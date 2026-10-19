using System;
using System.Globalization;
using Ballverse.Core.Projects;

namespace Ballverse.Core.Studio
{
    /// <summary>Entry animation of a text layer (spec §3.5 "появление, выезд, «печатная машинка», «подпрыгивание»", project <c>texts[].anim</c>).</summary>
    public enum TextAnimKind
    {
        /// <summary>Shown at once.</summary>
        None = 0,
        /// <summary>"appear" / "fade": alpha 0 → 1.</summary>
        Fade = 1,
        /// <summary>"pop": scale 0.6 → ≈1.04 (ease-out-back overshoot) → 1 with a fade (the §7.4 example).</summary>
        Pop = 2,
        /// <summary>"slide": rises from 60 reference px below with a fade ("выезд").</summary>
        Slide = 3,
        /// <summary>"typewriter": characters appear one by one.</summary>
        Typewriter = 4,
        /// <summary>"bounce": drops from 80 reference px above and bounces twice ("подпрыгивание").</summary>
        Bounce = 5,
    }

    /// <summary>How a text layer is drawn at one output time (see <see cref="TextLayerTiming.Evaluate"/>).</summary>
    public readonly struct TextLayerState
    {
        public bool Visible { get; }
        /// <summary>Opacity multiplier 0..1.</summary>
        public float Alpha { get; }
        /// <summary>Uniform scale around the layer anchor (1 = as measured).</summary>
        public float Scale { get; }
        /// <summary>Vertical offset in 1080-reference pixels (positive = down), scaled to the canvas like the text.</summary>
        public float OffsetYRef { get; }
        /// <summary>Characters to show (typewriter), or −1 for all.</summary>
        public int VisibleCharacters { get; }

        public TextLayerState(bool visible, float alpha, float scale, float offsetYRef, int visibleCharacters)
        {
            Visible = visible;
            Alpha = alpha;
            Scale = scale;
            OffsetYRef = offsetYRef;
            VisibleCharacters = visibleCharacters;
        }

        public static readonly TextLayerState Hidden = new TextLayerState(false, 0f, 1f, 0f, -1);
        public static readonly TextLayerState Full = new TextLayerState(true, 1f, 1f, 0f, -1);
    }

    /// <summary>
    /// Show time and entry animation of text layers on the reel timeline (spec §3.5): a layer is visible on output times
    /// [<see cref="TextLayer.InUs"/>, <see cref="TextLayer.OutUs"/>) and plays its entry animation from <c>inUs</c>. The result is a
    /// pure function of the output time, so preview, scrubbing and export show the same text frame. Also resolves the live
    /// counter placeholder <c>{n}</c> of the "remaining" template (spec §3.5 "счётчик «Осталось: N» (живой, связан с HUD)").
    /// </summary>
    public static class TextLayerTiming
    {
        /// <summary>Length of the fade, pop, slide and bounce entries, µs.</summary>
        public const long EntryUs = 400000;
        /// <summary>Bounce entry length, µs.</summary>
        public const long BounceUs = 700000;
        /// <summary>Typewriter speed: one character per 50 ms, the whole text within at most 2 s.</summary>
        public const long TypewriterCharUs = 50000;
        public const long TypewriterMaxUs = 2000000;
        /// <summary>The live counter placeholder (Config <c>TextTemplate</c>).</summary>
        public const string CounterPlaceholder = "{n}";

        /// <summary>Animation id → kind ("pop", "slide", "typewriter", "bounce", "appear"/"fade"); unknown or null → None.</summary>
        public static TextAnimKind ParseAnim(string id)
        {
            if (string.IsNullOrEmpty(id)) return TextAnimKind.None;
            switch (id.Trim().ToLowerInvariant())
            {
                case "appear":
                case "fade":
                    return TextAnimKind.Fade;
                case "pop":
                    return TextAnimKind.Pop;
                case "slide":
                    return TextAnimKind.Slide;
                case "typewriter":
                    return TextAnimKind.Typewriter;
                case "bounce":
                    return TextAnimKind.Bounce;
                default:
                    return TextAnimKind.None;
            }
        }

        /// <summary>True when the layer is on screen at <paramref name="outputUs"/>.</summary>
        public static bool IsVisible(TextLayer layer, long outputUs)
        {
            if (layer == null) return false;
            return outputUs >= layer.InUs && outputUs < layer.OutUs;
        }

        /// <summary>State of <paramref name="layer"/> at <paramref name="outputUs"/>; <paramref name="characterCount"/> is the length of the displayed text (for the typewriter).</summary>
        public static TextLayerState Evaluate(TextLayer layer, long outputUs, int characterCount)
        {
            if (!IsVisible(layer, outputUs)) return TextLayerState.Hidden;
            long age = outputUs - layer.InUs;
            switch (ParseAnim(layer.Anim))
            {
                case TextAnimKind.Fade:
                    return new TextLayerState(true, Progress(age, EntryUs), 1f, 0f, -1);
                case TextAnimKind.Pop:
                {
                    float p = Progress(age, EntryUs);
                    // Ease-out-back: 0.6 → overshoot ≈ 1.04 → 1.
                    const double c1 = 1.70158, c3 = c1 + 1;
                    double e = 1 + c3 * Math.Pow(p - 1, 3) + c1 * Math.Pow(p - 1, 2);
                    float scale = (float)(0.6 + 0.4 * e);
                    return new TextLayerState(true, Math.Min(1f, p * 2f), scale, 0f, -1);
                }
                case TextAnimKind.Slide:
                {
                    float p = Progress(age, EntryUs);
                    float eased = 1f - (1f - p) * (1f - p) * (1f - p);
                    return new TextLayerState(true, p, 1f, 60f * (1f - eased), -1);
                }
                case TextAnimKind.Bounce:
                {
                    float p = Progress(age, BounceUs);
                    return new TextLayerState(true, Math.Min(1f, p * 3f), 1f, -80f * (1f - BounceOut(p)), -1);
                }
                case TextAnimKind.Typewriter:
                {
                    if (characterCount <= 0) return TextLayerState.Full;
                    long perChar = Math.Min(TypewriterCharUs, TypewriterMaxUs / characterCount);
                    if (perChar <= 0) perChar = 1;
                    long shown = age / perChar + 1;
                    return new TextLayerState(true, 1f, 1f, 0f, shown >= characterCount ? -1 : (int)shown);
                }
                default:
                    return TextLayerState.Full;
            }
        }

        /// <summary>
        /// Text to draw: <paramref name="text"/> with every <c>{n}</c> replaced by <paramref name="counter"/> (invariant digits), or
        /// by an empty string when the counter is unknown (a clip without the HUD value).
        /// </summary>
        public static string ResolveText(string text, int? counter)
        {
            if (string.IsNullOrEmpty(text) || text.IndexOf(CounterPlaceholder, StringComparison.Ordinal) < 0) return text ?? string.Empty;
            string value = counter.HasValue ? counter.Value.ToString(CultureInfo.InvariantCulture) : string.Empty;
            return text.Replace(CounterPlaceholder, value);
        }

        /// <summary>True when the text uses the live counter (its look changes with the HUD value).</summary>
        public static bool UsesCounter(string text)
        {
            return !string.IsNullOrEmpty(text) && text.IndexOf(CounterPlaceholder, StringComparison.Ordinal) >= 0;
        }

        static float Progress(long age, long length)
        {
            if (age <= 0) return 0f;
            if (age >= length) return 1f;
            return (float)((double)age / length);
        }

        static float BounceOut(float x)
        {
            const float n1 = 7.5625f, d1 = 2.75f;
            if (x < 1f / d1) return n1 * x * x;
            if (x < 2f / d1)
            {
                x -= 1.5f / d1;
                return n1 * x * x + 0.75f;
            }
            if (x < 2.5f / d1)
            {
                x -= 2.25f / d1;
                return n1 * x * x + 0.9375f;
            }
            x -= 2.625f / d1;
            return n1 * x * x + 0.984375f;
        }
    }
}
