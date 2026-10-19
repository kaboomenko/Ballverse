using System;
using System.Collections.Generic;
using Ballverse.Core.RenderStream;
using Ballverse.Core.RenderStream.Internal;

namespace Ballverse.Core.Modes
{
    /// <summary>
    /// Append-only shape and label table of one run (spec §7.4 "таблица форм", "таблица строк"). Ids follow exactly the rules of
    /// <see cref="RenderStreamWriter.DefineShape"/> / <see cref="RenderStreamWriter.DefineLabel"/> (equal definitions share an id,
    /// shapes 0, 1, 2…, labels 1, 2, 3… in definition order), so a frame written with these ids is identical whether or not a
    /// render-stream writer is attached: the session replays the table into the writer in the same order before each frame.
    /// Definitions may be added at any time (new rings of a round, a new polygon of an evolution); an id is stable for the run.
    /// </summary>
    public sealed class ModeVisualTable
    {
        readonly List<ShapeDefinition> _shapes = new List<ShapeDefinition>();
        readonly Dictionary<ShapeDefinition, int> _shapeIds = new Dictionary<ShapeDefinition, int>();
        readonly List<string> _labels = new List<string>();
        readonly Dictionary<string, int> _labelIds = new Dictionary<string, int>(StringComparer.Ordinal);

        public int ShapeCount => _shapes.Count;

        /// <summary>Number of labels; valid label ids are 1..LabelCount (0 = <see cref="BodyState.NoLabel"/>).</summary>
        public int LabelCount => _labels.Count;

        /// <summary>Id of <paramref name="shape"/>, defining it on first use (allocates only then).</summary>
        /// <exception cref="InvalidOperationException">More than <see cref="RenderStreamLimits.MaxShapes"/> shapes.</exception>
        public int DefineShape(ShapeDefinition shape)
        {
            if (shape == null) throw new ArgumentNullException(nameof(shape));
            if (_shapeIds.TryGetValue(shape, out int id)) return id;
            if (_shapes.Count == RenderStreamLimits.MaxShapes) throw new InvalidOperationException("Shape table is full");
            id = _shapes.Count;
            _shapes.Add(shape);
            _shapeIds.Add(shape, id);
            return id;
        }

        /// <summary>Id (≥ 1) of a label or skin key string, defining it on first use.</summary>
        /// <exception cref="ArgumentException">Not a valid render-stream label (1–32 UTF-16 units of well-formed text).</exception>
        /// <exception cref="InvalidOperationException">More than <see cref="RenderStreamLimits.MaxLabels"/> labels.</exception>
        public int DefineLabel(string label)
        {
            if (label == null) throw new ArgumentNullException(nameof(label));
            if (_labelIds.TryGetValue(label, out int id)) return id;
            string error = TableCodec.ValidateLabel(label);
            if (error != null) throw new ArgumentException(error, nameof(label));
            if (_labels.Count == RenderStreamLimits.MaxLabels) throw new InvalidOperationException("Label table is full");
            _labels.Add(label);
            id = _labels.Count;
            _labelIds.Add(label, id);
            return id;
        }

        /// <summary>Labels that can still be defined before the table is full.</summary>
        public int FreeLabelSlots => RenderStreamLimits.MaxLabels - _labels.Count;

        /// <summary>
        /// Like <see cref="DefineLabel"/>, but never throws for data that comes from the user or a stored file (live label
        /// names, PARM records): returns false and <see cref="Ballverse.Core.RenderStream.BodyState.NoLabel"/> when the string is
        /// not a valid label or the table is full. Labels are never released, so a mode that defines labels from live edits
        /// must use this (the table keeps <see cref="RenderStreamLimits.MaxLabels"/> entries for the whole run).
        /// </summary>
        public bool TryDefineLabel(string label, out int id)
        {
            id = BodyState.NoLabel;
            if (string.IsNullOrEmpty(label)) return false;
            if (_labelIds.TryGetValue(label, out id)) return true;
            id = BodyState.NoLabel;
            if (_labels.Count == RenderStreamLimits.MaxLabels) return false;
            if (TableCodec.ValidateLabel(label) != null) return false;
            _labels.Add(label);
            id = _labels.Count;
            _labelIds.Add(label, id);
            return true;
        }

        public ShapeDefinition GetShape(int shapeId)
        {
            if ((uint)shapeId >= (uint)_shapes.Count) throw new ArgumentOutOfRangeException(nameof(shapeId));
            return _shapes[shapeId];
        }

        public string GetLabel(int labelId)
        {
            if (labelId < 1 || labelId > _labels.Count) throw new ArgumentOutOfRangeException(nameof(labelId));
            return _labels[labelId - 1];
        }
    }
}
