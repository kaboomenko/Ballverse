using System;

namespace Ballverse.Core.Layout
{
    /// <summary>
    /// Split-screen layout settings (spec §3.3, project "layout" block of §7.4). Immutable; numeric values are clamped
    /// to their ranges on construction (so a hand-edited or older project never breaks the compositor), NaN/infinity throw.
    /// Pixel values are defined at the 1080-wide reference canvas (see <see cref="PixelMath"/>).
    /// </summary>
    public sealed class LayoutSpec
    {
        public const float MinRatio = 0.30f;
        public const float MaxRatio = 0.70f;
        public const float DefaultRatio = 0.50f;
        public const int MaxDividerPx = 16;
        public const int MaxCornerRadiusPx = 48;
        public const int MaxPaddingPx = 48;

        /// <summary>Default layout: L1, S on top, 50/50, 6 px divider, 24 px corners, no padding.</summary>
        public static readonly LayoutSpec Default = new LayoutSpec(LayoutType.L1, false, DefaultRatio, 6, 24, 0, null);

        public LayoutType Type { get; }

        /// <summary>
        /// "Поменять местами" (spec §3.3). L1: V on top. L2: V on the left. L3/L5: exchanges which content is full frame
        /// and which is in the window (L3+swap renders as L5 and vice versa). Ignored for L4.
        /// </summary>
        public bool Swap { get; }

        /// <summary>
        /// Share of the simulation slot S along the split axis (L1: height, L2: width) of the space left after padding and
        /// divider, 0.30–0.70. It stays the share of S when <see cref="Swap"/> is on. Snapping to magnets is a UI step,
        /// see <see cref="RatioMagnets"/>.
        /// </summary>
        public float Ratio { get; }

        /// <summary>Divider thickness between the L1/L2 slots, 0–16 px at the reference width.</summary>
        public int DividerPx { get; }

        /// <summary>Slot corner radius, 0–48 px at the reference width (L1/L2 slots and the L3/L5 rounded window).</summary>
        public int CornerRadiusPx { get; }

        /// <summary>Outer padding around the L1/L2 slots, 0–48 px at the reference width.</summary>
        public int PaddingPx { get; }

        /// <summary>Floating window settings for L3/L5. Never null (defaults to <see cref="PipSpec.Default"/>).</summary>
        public PipSpec Pip { get; }

        public LayoutSpec(LayoutType type, bool swap = false, float ratio = DefaultRatio, int dividerPx = 0,
            int cornerRadiusPx = 0, int paddingPx = 0, PipSpec pip = null)
        {
            if (type < LayoutType.L1 || type > LayoutType.L5)
                throw new ArgumentOutOfRangeException(nameof(type), type, "unknown layout");
            PixelMath.RequireFinite(ratio, nameof(ratio));
            Type = type;
            Swap = swap;
            Ratio = PixelMath.ClampF(ratio, MinRatio, MaxRatio);
            DividerPx = PixelMath.Clamp(dividerPx, 0, MaxDividerPx);
            CornerRadiusPx = PixelMath.Clamp(cornerRadiusPx, 0, MaxCornerRadiusPx);
            PaddingPx = PixelMath.Clamp(paddingPx, 0, MaxPaddingPx);
            Pip = pip ?? PipSpec.Default;
        }

        /// <summary>Layout actually rendered: L3 with swap is L5 and L5 with swap is L3; other layouts unchanged.</summary>
        public LayoutType EffectiveType
        {
            get
            {
                if (!Swap) return Type;
                if (Type == LayoutType.L3) return LayoutType.L5;
                if (Type == LayoutType.L5) return LayoutType.L3;
                return Type;
            }
        }

        public LayoutSpec WithType(LayoutType type) => new LayoutSpec(type, Swap, Ratio, DividerPx, CornerRadiusPx, PaddingPx, Pip);
        public LayoutSpec WithSwap(bool swap) => new LayoutSpec(Type, swap, Ratio, DividerPx, CornerRadiusPx, PaddingPx, Pip);
        public LayoutSpec WithRatio(float ratio) => new LayoutSpec(Type, Swap, ratio, DividerPx, CornerRadiusPx, PaddingPx, Pip);
        public LayoutSpec WithDivider(int px) => new LayoutSpec(Type, Swap, Ratio, px, CornerRadiusPx, PaddingPx, Pip);
        public LayoutSpec WithCornerRadius(int px) => new LayoutSpec(Type, Swap, Ratio, DividerPx, px, PaddingPx, Pip);
        public LayoutSpec WithPadding(int px) => new LayoutSpec(Type, Swap, Ratio, DividerPx, CornerRadiusPx, px, Pip);
        public LayoutSpec WithPip(PipSpec pip) => new LayoutSpec(Type, Swap, Ratio, DividerPx, CornerRadiusPx, PaddingPx, pip);
    }
}
