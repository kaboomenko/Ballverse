using System;
using System.Collections.Generic;

namespace Ballverse.Core.Layout
{
    /// <summary>
    /// Result of <see cref="LayoutCalculator.ComputeSlots"/>: everything the studio compositor (spec §7.5, step c) needs to
    /// place the two content slots for one canvas size. Immutable.
    /// </summary>
    public sealed class FrameLayout
    {
        readonly SlotGeometry[] _slotsBackToFront;

        public int CanvasWidth { get; }
        public int CanvasHeight { get; }

        /// <summary>Layout actually drawn (L3/L5 resolved through swap, see <see cref="LayoutSpec.EffectiveType"/>).</summary>
        public LayoutType EffectiveType { get; }

        public SlotGeometry Simulation { get; }

        /// <summary>False for L4 (simulation only).</summary>
        public bool HasVideo { get; }

        /// <summary>Video slot; meaningless when <see cref="HasVideo"/> is false.</summary>
        public SlotGeometry Video { get; }

        /// <summary>True for L1/L2 with a divider of at least 1 px at this canvas size.</summary>
        public bool HasDivider { get; }

        /// <summary>Divider rect between the L1/L2 slots, <see cref="IntRect.Empty"/> otherwise. Drawn above the slots.</summary>
        public IntRect Divider { get; }

        /// <summary>Area inside the outer padding (L1/L2); the full canvas for the other layouts.</summary>
        public IntRect Content { get; }

        /// <summary>True for L3/L5: one slot floats in a window above the full-frame one.</summary>
        public bool HasWindow { get; }

        /// <summary>The floating window slot for L3/L5 (video in L3, simulation in L5).</summary>
        public SlotGeometry Window => HasWindow ? _slotsBackToFront[1] : throw new InvalidOperationException("layout has no window");

        /// <summary>Slots in draw order (ascending z-index; ties in S, V order).</summary>
        public IReadOnlyList<SlotGeometry> SlotsBackToFront => _slotsBackToFront;

        internal FrameLayout(int canvasWidth, int canvasHeight, LayoutType effectiveType, SlotGeometry simulation,
            bool hasVideo, SlotGeometry video, IntRect divider, IntRect content, bool hasWindow)
        {
            CanvasWidth = canvasWidth;
            CanvasHeight = canvasHeight;
            EffectiveType = effectiveType;
            Simulation = simulation;
            HasVideo = hasVideo;
            Video = video;
            Divider = divider;
            HasDivider = !divider.IsEmpty;
            Content = content;
            HasWindow = hasWindow;
            if (!hasVideo)
            {
                _slotsBackToFront = new[] { simulation };
            }
            else if (video.ZIndex < simulation.ZIndex)
            {
                _slotsBackToFront = new[] { video, simulation };
            }
            else
            {
                _slotsBackToFront = new[] { simulation, video };
            }
        }
    }
}
