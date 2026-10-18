using System;

namespace Ballverse.Core.RenderGeometry
{
    /// <summary>Quality levels of spec §7.7 (A/B/C for real time, Export for offline rendering).</summary>
    public enum QualityLevel : byte
    {
        A = 0,
        B = 1,
        C = 2,
        Export = 3,
    }

    /// <summary>Bloom variants of the §7.7 quality table.</summary>
    public enum BloomMode : byte
    {
        Off = 0,
        /// <summary>Bloom chain starting at a quarter-resolution buffer ("половинный буфер").</summary>
        Half = 1,
        /// <summary>Bloom chain starting at half resolution.</summary>
        Full = 2,
    }

    /// <summary>
    /// One row of the quality table of spec §7.7 plus the renderer's derived budgets. Immutable.
    /// <code>
    /// Profile | Bloom | Trails | Render scale | MSAA | Particles | Tolerance
    /// A       | full  | 100 %  | 1.0          | 4×   | 100 % / 600 | 0.25 px
    /// B       | half  |  70 %  | 1.0          | 2×   |  75 % / 400 | 0.35 px
    /// C       | off   |  40 %  | 0.8          | 2×   |  50 % / 200 | 0.5 px
    /// Export  | full  | 100 %  | 1.0 of output| 4×   | 100 % / 600 | 0.2 px
    /// </code>
    /// The particle caps follow the M05 card (VFX ≤ 600 / 400 / 200). Export never depends on the device tier (§7.5).
    /// </summary>
    public sealed class QualityProfile
    {
        public static readonly QualityProfile A = new QualityProfile(QualityLevel.A, BloomMode.Full, 1f, 1f, 4, 1f, 600, 0.25f, false);
        public static readonly QualityProfile B = new QualityProfile(QualityLevel.B, BloomMode.Half, 0.7f, 1f, 2, 0.75f, 400, 0.35f, false);
        public static readonly QualityProfile C = new QualityProfile(QualityLevel.C, BloomMode.Off, 0.4f, 0.8f, 2, 0.5f, 200, 0.5f, true);
        public static readonly QualityProfile Export = new QualityProfile(QualityLevel.Export, BloomMode.Full, 1f, 1f, 4, 1f, 600, 0.2f, false);

        public QualityProfile(QualityLevel level, BloomMode bloom, float trailFraction, float renderScale, int msaa, float particleFraction,
            int maxParticles, float tolerancePixels, bool simplifiedGlass)
        {
            if (!(trailFraction >= 0f && trailFraction <= 1f)) throw new ArgumentOutOfRangeException(nameof(trailFraction));
            if (!(renderScale >= 0.25f && renderScale <= 1f)) throw new ArgumentOutOfRangeException(nameof(renderScale));
            if (msaa != 1 && msaa != 2 && msaa != 4 && msaa != 8) throw new ArgumentOutOfRangeException(nameof(msaa));
            if (!(particleFraction >= 0f && particleFraction <= 1f)) throw new ArgumentOutOfRangeException(nameof(particleFraction));
            if (maxParticles < 0) throw new ArgumentOutOfRangeException(nameof(maxParticles));
            if (!(tolerancePixels > 0f)) throw new ArgumentOutOfRangeException(nameof(tolerancePixels));
            Level = level;
            Bloom = bloom;
            TrailFraction = trailFraction;
            RenderScale = renderScale;
            Msaa = msaa;
            ParticleFraction = particleFraction;
            MaxParticles = maxParticles;
            TolerancePixels = tolerancePixels;
            SimplifiedGlass = simplifiedGlass;
        }

        public QualityLevel Level { get; }
        public BloomMode Bloom { get; }
        /// <summary>Fraction of the full trail length (spec §7.7 "Следы").</summary>
        public float TrailFraction { get; }
        /// <summary>Scene buffer size relative to the target (spec §7.7 "Рендер-масштаб"; C uses 0.8 within 0.75–0.85).</summary>
        public float RenderScale { get; }
        /// <summary>MSAA sample count of the scene buffer.</summary>
        public int Msaa { get; }
        /// <summary>Fraction of the nominal particle count spawned per event.</summary>
        public float ParticleFraction { get; }
        /// <summary>Hard cap of live particles per frame.</summary>
        public int MaxParticles { get; }
        /// <summary>Curve tessellation tolerance in pixels.</summary>
        public float TolerancePixels { get; }
        /// <summary>Glass theme drawn as plain translucency (spec §2.10 "на тире C упрощается").</summary>
        public bool SimplifiedGlass { get; }

        public bool IsRealtime => Level != QualityLevel.Export;

        public static QualityProfile For(QualityLevel level)
        {
            switch (level)
            {
                case QualityLevel.A: return A;
                case QualityLevel.B: return B;
                case QualityLevel.C: return C;
                case QualityLevel.Export: return Export;
                default: throw new ArgumentOutOfRangeException(nameof(level));
            }
        }

        /// <summary>The real-time starting profile for a device tier (spec §7.7 "автовыбор профиля — по тиру").</summary>
        public static QualityProfile ForTier(DeviceTier tier)
        {
            switch (tier)
            {
                case DeviceTier.A: return A;
                case DeviceTier.B: return B;
                default: return C;
            }
        }
    }

    /// <summary>
    /// Dynamic real-time quality fallback (spec §7.7): when the average FPS over a 3 s window is below 55, the profile
    /// steps down one level (A → B → C); never below C, never up again automatically, never for export. Feed it the real
    /// frame durations; pure and deterministic for a given input sequence. Allocation-free.
    /// </summary>
    public sealed class QualityGovernor
    {
        public const float DefaultWindowSeconds = 3f;
        public const float DefaultMinFps = 55f;

        readonly float _windowSeconds;
        readonly float _minFps;
        float _elapsed;
        int _frames;

        public QualityGovernor(QualityLevel start, float windowSeconds = DefaultWindowSeconds, float minFps = DefaultMinFps)
        {
            if (start == QualityLevel.Export) throw new ArgumentException("The governor only manages real-time profiles", nameof(start));
            if (!(windowSeconds > 0f)) throw new ArgumentOutOfRangeException(nameof(windowSeconds));
            if (!(minFps > 0f)) throw new ArgumentOutOfRangeException(nameof(minFps));
            Level = start;
            _windowSeconds = windowSeconds;
            _minFps = minFps;
        }

        public QualityLevel Level { get; private set; }

        public QualityProfile Profile => QualityProfile.For(Level);

        /// <summary>Number of automatic downgrades so far (for analytics / diagnostics).</summary>
        public int Downgrades { get; private set; }

        /// <summary>
        /// Records one displayed frame of <paramref name="deltaSeconds"/>. Returns true when the level changed. Frames longer
        /// than 0.5 s (app paused, loading hitch) restart the window instead of counting.
        /// </summary>
        public bool AddFrame(float deltaSeconds)
        {
            if (!(deltaSeconds > 0f)) return false;
            if (deltaSeconds > 0.5f)
            {
                ResetWindow();
                return false;
            }
            _elapsed += deltaSeconds;
            _frames++;
            if (_elapsed < _windowSeconds) return false;
            float fps = _frames / _elapsed;
            ResetWindow();
            if (fps >= _minFps || Level == QualityLevel.C) return false;
            Level = Level == QualityLevel.A ? QualityLevel.B : QualityLevel.C;
            Downgrades++;
            return true;
        }

        /// <summary>Forces a level (user choice in settings) and restarts the window.</summary>
        public void SetLevel(QualityLevel level)
        {
            if (level == QualityLevel.Export) throw new ArgumentException("The governor only manages real-time profiles", nameof(level));
            Level = level;
            ResetWindow();
        }

        public void ResetWindow()
        {
            _elapsed = 0f;
            _frames = 0;
        }
    }
}
