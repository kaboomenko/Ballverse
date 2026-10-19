using System;
using Ballverse.Core.Integration;
using Ballverse.Core.RenderStream;
using Ballverse.Core.Replay;

namespace Ballverse.Core.Tests.Modes
{
    /// <summary>
    /// Test mirror of the Studio's mode-agnostic <c>Ballverse.Core.Studio.RenderStreamSoundBodySource</c> (integration branch;
    /// <c>Core/Studio</c> is not part of this track's tree). Same convention: EVNT body id = render-stream body id, size = the
    /// body's diameter for circles (largest extent × scale otherwise), pan = x over half the arena width, a body missing from
    /// the event tick's frame makes the event bodyless with size 0.5 m and centre pan. Keep in step with the Studio class.
    /// </summary>
    public sealed class StreamSoundBodySourceMirror : ISoundBodySource
    {
        public const float DefaultSize = 0.5f;

        readonly RenderStreamReader _reader;
        readonly RenderQuantization _q;
        readonly RenderFrame _frame = new RenderFrame();
        int _frameTick = -1;

        public StreamSoundBodySourceMirror(RenderStreamReader reader)
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
            bodySize = shape.Kind == ShapeKind.Circle ? 2f * scale
                : shape.Kind == ShapeKind.Ring ? (2f * _q.DequantizeScale(shape.Radius) + _q.DequantizeScale(shape.Thickness)) * scale
                : DefaultSize;
            if (!(bodySize > 0f) || float.IsInfinity(bodySize)) bodySize = DefaultSize;
            float half = SimConstants.ArenaWidth * 0.5f;
            pan = Math.Max(-1f, Math.Min(1f, _q.DequantizePosition(body.X) / half));
            return true;
        }
    }
}
