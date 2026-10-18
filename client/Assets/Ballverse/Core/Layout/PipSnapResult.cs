using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace Ballverse.Core.Layout
{
    /// <summary>Result of <see cref="PipMagnets.Snap"/>.</summary>
    public sealed class PipSnapResult
    {
        readonly ReadOnlyCollection<PipAnchor> _anchors;

        public PipCorner Corner { get; }

        /// <summary>Snapped window rect in canvas pixels.</summary>
        public IntRect Rect { get; }

        public long MaskOverlap { get; }

        /// <summary>The input spec with the snapped normalized center; feeding it to <see cref="LayoutCalculator"/> reproduces <see cref="Rect"/>.</summary>
        public PipSpec Pip { get; }

        /// <summary>All four corner candidates in <see cref="PipCorner"/> order (for UI hints). Read-only view.</summary>
        public IReadOnlyList<PipAnchor> Anchors => _anchors;

        internal PipSnapResult(PipAnchor chosen, PipSpec pip, PipAnchor[] anchors)
        {
            Corner = chosen.Corner;
            Rect = chosen.Rect;
            MaskOverlap = chosen.MaskOverlap;
            Pip = pip;
            _anchors = Array.AsReadOnly((PipAnchor[])anchors.Clone());
        }
    }
}
