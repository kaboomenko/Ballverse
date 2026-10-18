using System;
using Ballverse.Core.RenderStream;

namespace Ballverse.Core.RenderGeometry
{
    /// <summary>Camera modes of spec §2.1.</summary>
    public enum CameraMode : byte
    {
        /// <summary>Arena centre, zoom 1 (default).</summary>
        Fixed = 0,
        /// <summary>Follows the leader ball (M05 Helix Drop: vertically only, never upwards).</summary>
        Follow = 1,
        /// <summary>
        /// Smoothly fits all visible bodies (spec §2.1), or only the bodies flagged <see cref="RenderBodyFlags.CameraTarget"/>
        /// when a frame has any (M01 «Отдаление камеры»: the mode flags the ball and the next 4 rings).
        /// </summary>
        AutoZoom = 2,
    }

    /// <summary>Camera configuration of a mode / view. Mutable holder; see <see cref="ForHelix"/> for the M05 camera.</summary>
    public sealed class CameraSettings
    {
        public CameraMode Mode { get; set; } = CameraMode.Fixed;
        /// <summary>Follow: keep the arena's horizontal centre and follow only the height.</summary>
        public bool FollowVerticalOnly { get; set; } = true;
        /// <summary>Follow: where the target sits, as a fraction of the view height from the top (0.5 = centre).</summary>
        public float FollowAnchorFromTop { get; set; } = 0.5f;
        /// <summary>Follow: the camera never moves up (M05 "камера не поднимается при отскоке — только опускается").</summary>
        public bool FollowOnlyDown { get; set; }
        /// <summary>Exponential smoothing time constant, seconds (0 = none).</summary>
        public float SmoothingSeconds { get; set; } = 0.15f;
        /// <summary>AutoZoom: margin around the fitted bodies, metres.</summary>
        public float AutoZoomMargin { get; set; } = 1.5f;
        /// <summary>AutoZoom: keep the arena centre at the view centre and fit symmetrically (concentric rings).</summary>
        public bool AutoZoomCentered { get; set; }
        /// <summary>AutoZoom: smallest view height, metres (0 = the zoom-1 view: never closer than the arena).</summary>
        public float AutoZoomMinHeight { get; set; }
        /// <summary>AutoZoom: largest view height, metres.</summary>
        public float AutoZoomMaxHeight { get; set; } = 40f;
        /// <summary>Zoom-1 view height override, metres (0 = from <see cref="ViewMapping"/>).</summary>
        public float ViewHeight { get; set; }

        /// <summary>The M05 Helix Drop camera: vertical follow, ball at 38 % from the top, smoothing 0.15 s, only down.</summary>
        public static CameraSettings ForHelix()
        {
            return new CameraSettings
            {
                Mode = CameraMode.Follow,
                FollowVerticalOnly = true,
                FollowAnchorFromTop = 0.38f,
                FollowOnlyDown = true,
                SmoothingSeconds = 0.15f,
            };
        }

        public void CopyFrom(CameraSettings o)
        {
            if (o == null) throw new ArgumentNullException(nameof(o));
            Mode = o.Mode;
            FollowVerticalOnly = o.FollowVerticalOnly;
            FollowAnchorFromTop = o.FollowAnchorFromTop;
            FollowOnlyDown = o.FollowOnlyDown;
            SmoothingSeconds = o.SmoothingSeconds;
            AutoZoomMargin = o.AutoZoomMargin;
            AutoZoomCentered = o.AutoZoomCentered;
            AutoZoomMinHeight = o.AutoZoomMinHeight;
            AutoZoomMaxHeight = o.AutoZoomMaxHeight;
            ViewHeight = o.ViewHeight;
        }
    }

    /// <summary>
    /// Deterministic camera (spec §2.1 camera modes, §7.4 "превью = экспорт"). Per tick it records the follow target and
    /// the AutoZoom bounds (see <see cref="Record"/>); <see cref="Evaluate"/> recomputes the smoothed camera of a tick from the recorded
    /// window only — never from the previous rendered frame — so live preview, scrubbing to any tick and export give the
    /// same view. Smoothing is an exponential filter over the last ≈ 6 time constants (residual &lt; 0.3 %); "only down" is
    /// the minimum of the filtered height over the last 2 s (longer than a bounce). Allocation-free.
    /// </summary>
    public sealed class CameraTrack
    {
        /// <summary>Window of the "only down" minimum, ticks (2 s).</summary>
        public const int MinWindowTicks = 120;
        /// <summary>Longest smoothing window, ticks (6 × 0.5 s).</summary>
        public const int MaxFilterTicks = 180;
        /// <summary>Ticks of history the track keeps.</summary>
        public const int Capacity = MinWindowTicks + MaxFilterTicks + 1;

        struct Sample
        {
            public int Tick;
            public bool HasTarget;
            public float TargetX;
            public float TargetY;
            public bool HasBounds;
            public float MinX;
            public float MinY;
            public float MaxX;
            public float MaxY;
        }

        readonly Sample[] _samples = new Sample[Capacity];

        public CameraTrack()
        {
            Reset();
        }

        public void Reset()
        {
            for (int i = 0; i < _samples.Length; i++) _samples[i].Tick = -1;
        }

        /// <summary>
        /// Records the camera inputs of <paramref name="tick"/>: the target is the highlighted visible circle with the lowest
        /// id, else the visible circle with the lowest id (frames are id-sorted). The AutoZoom bounds cover the extents
        /// (bounding circle: <see cref="ShapeTessellator.BoundingRadius"/> at the body scale, rotation-invariant) of the
        /// visible bodies flagged <see cref="RenderBodyFlags.CameraTarget"/> when the frame has any, otherwise of every
        /// visible body — balls and shapes alike («плавно вписывает все активные тела», §2.1). Allocation-free.
        /// </summary>
        public void Record(int tick, RenderFrame frame, RenderQuantization quantization, System.Collections.Generic.IReadOnlyList<ShapeDefinition> shapes)
        {
            if (frame == null) throw new ArgumentNullException(nameof(frame));
            if (quantization == null) throw new ArgumentNullException(nameof(quantization));
            if (tick < 0) return;
            ref Sample s = ref _samples[tick % Capacity];
            s.Tick = tick;
            s.HasTarget = false;
            s.HasBounds = false;
            bool highlighted = false;
            bool targetsOnly = false;
            ReadOnlySpan<BodyState> bodies = frame.Bodies;
            for (int i = 0; i < bodies.Length; i++)
            {
                BodyState b = bodies[i];
                if ((b.Flags & BodyFlags.Visible) == 0) continue;
                if ((b.Flags & RenderBodyFlags.CameraTarget) != 0) targetsOnly = true;
                if (!IsCircle(b, shapes)) continue;
                bool hl = (b.Flags & BodyFlags.Highlight) != 0;
                if (!s.HasTarget || (hl && !highlighted))
                {
                    s.HasTarget = true;
                    s.TargetX = quantization.DequantizePosition(b.X);
                    s.TargetY = quantization.DequantizePosition(b.Y);
                    highlighted = hl;
                }
            }
            for (int i = 0; i < bodies.Length; i++)
            {
                BodyState b = bodies[i];
                if ((b.Flags & BodyFlags.Visible) == 0) continue;
                if (targetsOnly && (b.Flags & RenderBodyFlags.CameraTarget) == 0) continue;
                float r = Extent(b, quantization, shapes);
                if (!(r >= 0f)) continue;
                float x = quantization.DequantizePosition(b.X);
                float y = quantization.DequantizePosition(b.Y);
                if (!s.HasBounds)
                {
                    s.HasBounds = true;
                    s.MinX = x - r;
                    s.MaxX = x + r;
                    s.MinY = y - r;
                    s.MaxY = y + r;
                }
                else
                {
                    s.MinX = Math.Min(s.MinX, x - r);
                    s.MaxX = Math.Max(s.MaxX, x + r);
                    s.MinY = Math.Min(s.MinY, y - r);
                    s.MaxY = Math.Max(s.MaxY, y + r);
                }
            }
        }

        /// <summary>Bounding radius of a body around its position, metres; negative when the shape id is unknown.</summary>
        static float Extent(in BodyState b, RenderQuantization q, System.Collections.Generic.IReadOnlyList<ShapeDefinition> shapes)
        {
            float scale = q.DequantizeScale(b.Scale);
            if (shapes == null) return scale;
            if ((uint)b.ShapeId >= (uint)shapes.Count) return -1f;
            return ShapeTessellator.BoundingRadius(shapes[b.ShapeId], q, scale);
        }

        static bool IsCircle(in BodyState b, System.Collections.Generic.IReadOnlyList<ShapeDefinition> shapes)
        {
            if (shapes == null) return true;
            return (uint)b.ShapeId < (uint)shapes.Count && shapes[b.ShapeId].Kind == ShapeKind.Circle;
        }

        /// <summary>
        /// The view of <paramref name="tick"/> for a <paramref name="width"/> × <paramref name="height"/> canvas.
        /// <paramref name="targetY"/> returns the smoothed follow height (the Helix projection's focus height).
        /// </summary>
        public ViewRect Evaluate(int tick, CameraSettings settings, int width, int height, out float targetY)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            ViewRect baseView = ViewMapping.Fixed(width, height);
            if (settings.ViewHeight > 0f) baseView = ViewMapping.WithHeight(width, height, 0f, 0f, settings.ViewHeight);
            targetY = 0f;
            switch (settings.Mode)
            {
                case CameraMode.Follow:
                    return EvaluateFollow(tick, settings, baseView, out targetY);
                case CameraMode.AutoZoom:
                    return EvaluateAutoZoom(tick, settings, baseView, width, height);
                default:
                    return baseView;
            }
        }

        static float Alpha(float smoothingSeconds)
        {
            if (!(smoothingSeconds > 0f)) return 1f;
            return 1f - (float)Math.Exp(-1.0 / (SimConstants.TickRate * smoothingSeconds));
        }

        /// <summary>Ticks of the smoothing window for a time constant: ⌈6·τ·60⌉, at most <see cref="MaxFilterTicks"/>.</summary>
        public static int FilterTicks(float smoothingSeconds)
        {
            if (!(smoothingSeconds > 0f)) return 0;
            return Math.Min(MaxFilterTicks, (int)Math.Ceiling(6.0 * smoothingSeconds * SimConstants.TickRate - 1e-4));
        }

        ViewRect EvaluateFollow(int tick, CameraSettings s, ViewRect baseView, out float targetY)
        {
            float a = Alpha(s.SmoothingSeconds);
            int minWindow = s.FollowOnlyDown ? MinWindowTicks : 0;
            int start = tick - minWindow - FilterTicks(s.SmoothingSeconds);
            bool init = false;
            float fx = 0f, fy = 0f;
            bool haveOut = false;
            float outY = 0f;
            for (int t = Math.Max(0, start); t <= tick; t++)
            {
                ref Sample smp = ref _samples[t % Capacity];
                if (smp.Tick == t && smp.HasTarget)
                {
                    if (!init)
                    {
                        fx = smp.TargetX;
                        fy = smp.TargetY;
                        init = true;
                    }
                    else
                    {
                        fx += a * (smp.TargetX - fx);
                        fy += a * (smp.TargetY - fy);
                    }
                }
                if (init && t >= tick - minWindow)
                {
                    outY = !haveOut || !s.FollowOnlyDown ? fy : Math.Min(outY, fy);
                    haveOut = true;
                }
            }
            targetY = haveOut ? outY : 0f;
            float cx = s.FollowVerticalOnly || !init ? baseView.CenterX : fx;
            float anchor = Clamp(s.FollowAnchorFromTop, 0f, 1f);
            float cy = targetY + (anchor - 0.5f) * baseView.Height;
            return new ViewRect(cx, cy, baseView.Width, baseView.Height);
        }

        ViewRect EvaluateAutoZoom(int tick, CameraSettings s, ViewRect baseView, int width, int height)
        {
            float a = Alpha(s.SmoothingSeconds);
            int start = tick - FilterTicks(s.SmoothingSeconds);
            bool init = false;
            float minX = 0f, minY = 0f, maxX = 0f, maxY = 0f;
            for (int t = Math.Max(0, start); t <= tick; t++)
            {
                ref Sample smp = ref _samples[t % Capacity];
                if (smp.Tick != t || !smp.HasBounds) continue;
                if (!init)
                {
                    minX = smp.MinX; minY = smp.MinY; maxX = smp.MaxX; maxY = smp.MaxY;
                    init = true;
                }
                else
                {
                    minX += a * (smp.MinX - minX);
                    minY += a * (smp.MinY - minY);
                    maxX += a * (smp.MaxX - maxX);
                    maxY += a * (smp.MaxY - maxY);
                }
            }
            if (!init) return baseView;
            float cx, cy, hx, hy;
            float m = Math.Max(0f, s.AutoZoomMargin);
            if (s.AutoZoomCentered)
            {
                cx = baseView.CenterX;
                cy = baseView.CenterY;
                hx = Math.Max(Math.Abs(minX - cx), Math.Abs(maxX - cx)) + m;
                hy = Math.Max(Math.Abs(minY - cy), Math.Abs(maxY - cy)) + m;
            }
            else
            {
                cx = (minX + maxX) * 0.5f;
                cy = (minY + maxY) * 0.5f;
                hx = (maxX - minX) * 0.5f + m;
                hy = (maxY - minY) * 0.5f + m;
            }
            float h = Math.Max(2f * hy, 2f * hx * height / width);
            float minH = s.AutoZoomMinHeight > 0f ? s.AutoZoomMinHeight : baseView.Height;
            float maxH = Math.Max(minH, s.AutoZoomMaxHeight);
            h = Clamp(h, minH, maxH);
            return ViewMapping.WithHeight(width, height, cx, cy, h);
        }

        static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
    }
}
