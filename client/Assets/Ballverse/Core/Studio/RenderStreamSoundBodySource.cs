using System;
using Ballverse.Core.Integration;
using Ballverse.Core.RenderStream;
using Ballverse.Core.Replay;

namespace Ballverse.Core.Studio
{
    /// <summary>
    /// <see cref="ISoundBodySource"/> over a clip's render stream (spec §7.4: realtime and export read size and pan from the same
    /// quantized source, see <see cref="ISoundBodySource"/>). Convention (release-1 modes on Core.Sim): the replay EVNT body id is
    /// the render-stream body id. The event tick's frame is decoded (events come in tick order, so the reader moves forward);
    /// the body's size is its diameter for circles, else the shape's largest extent times the body scale; pan is its x position
    /// over half the arena width, clamped to −1…1. A body missing from the frame makes the event bodyless (returns false) with
    /// <see cref="DefaultSize"/> and centre pan. Not thread-safe (owns a frame buffer); use one instance per mix.
    /// </summary>
    public sealed class RenderStreamSoundBodySource : ISoundBodySource
    {
        /// <summary>Size used for events without a body in the frame, m.</summary>
        public const float DefaultSize = 0.5f;

        readonly RenderStreamReader _reader;
        readonly RenderQuantization _q;
        readonly RenderFrame _frame = new RenderFrame();
        int _frameTick = -1;

        public RenderStreamSoundBodySource(RenderStreamReader reader)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _q = reader.Header.Quantization;
        }

        public bool TryGetBody(in ReplayEvent e, out float bodySize, out float pan)
        {
            bodySize = DefaultSize;
            pan = 0f;
            if (_reader.TickCount <= 0) return false;
            long t = Math.Min((long)e.Tick, _reader.TickCount - 1L);
            int tick = (int)Math.Max(0, t);
            if (tick != _frameTick)
            {
                _reader.ReadFrame(tick, _frame);
                _frameTick = tick;
            }
            if (!_frame.TryGetBody(e.BodyId, out BodyState body)) return false;
            float scale = _q.DequantizeScale(body.Scale);
            ShapeDefinition shape = body.ShapeId >= 0 && body.ShapeId < _reader.Shapes.Count ? _reader.Shapes[body.ShapeId] : ShapeDefinition.Circle;
            bodySize = shape.Kind == ShapeKind.Circle ? 2f * scale : Extent(shape) * scale;
            if (!(bodySize > 0f) || float.IsInfinity(bodySize)) bodySize = DefaultSize;
            float half = SimConstants.ArenaWidth * 0.5f;
            pan = Math.Max(-1f, Math.Min(1f, _q.DequantizePosition(body.X) / half));
            return true;
        }

        /// <summary>Largest body-local extent of a non-circle shape at scale 1, m.</summary>
        float Extent(ShapeDefinition shape)
        {
            switch (shape.Kind)
            {
                case ShapeKind.Ring:
                    return 2f * _q.DequantizeScale(shape.Radius) + _q.DequantizeScale(shape.Thickness);
                case ShapeKind.Capsule:
                    return _q.DequantizeScale(shape.Length) + _q.DequantizeScale(shape.Thickness);
                default:
                {
                    ReadOnlySpan<ShapePoint> pts = shape.Points;
                    if (pts.Length == 0) return DefaultSize;
                    float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
                    for (int i = 0; i < pts.Length; i++)
                    {
                        float x = _q.DequantizePosition(pts[i].X), y = _q.DequantizePosition(pts[i].Y);
                        if (x < minX) minX = x;
                        if (x > maxX) maxX = x;
                        if (y < minY) minY = y;
                        if (y > maxY) maxY = y;
                    }
                    float extent = Math.Max(maxX - minX, maxY - minY);
                    if (shape.Kind == ShapeKind.Polyline) extent += _q.DequantizeScale(shape.Thickness);
                    return extent;
                }
            }
        }
    }
}
