using System;
using System.Collections.Generic;
using System.Linq;
using Ballverse.Core.Params;
using Ballverse.Core.Sim;
using Xunit;

namespace Ballverse.Core.Tests.Sim
{
    /// <summary>
    /// Mode code imports Ballverse.Core.Sim (physics) together with other Core namespaces: Params (body budgets), RenderStream
    /// (render frames, spec §7.3 "запись кадра потока рендера"), Replay, Audio. Any public type name shared with Sim makes such a
    /// file fail with CS0104 (ambiguous reference) — as Params.BodyKind vs Sim.BodyKind and RenderStream.BodyState vs
    /// Sim.BodyState did (now Sim.BodyMotion and Sim.PhysicsBodyState).
    /// </summary>
    public class NamespaceCoexistenceTests
    {
        [Fact]
        public void ParamsAndSimNamespacesCanBeImportedTogether()
        {
            var def = new BodyDef { Motion = BodyMotion.Kinematic, Scale = 1f };
            BodyKind budgetKind = BodyKind.Balls;
            var w = new PhysicsWorld();
            int body = w.AddBody(def);
            ref readonly PhysicsBodyState state = ref w.GetBody(body);
            Assert.Equal(BodyMotion.Kinematic, state.Motion);
            Assert.Equal(BodyKind.Balls, budgetKind);
        }

        static string[] SimPublicTypeNames()
        {
            return typeof(PhysicsWorld).Assembly.GetExportedTypes()
                .Where(t => t.Namespace == "Ballverse.Core.Sim" && !t.IsNested)
                .Select(t => t.Name)
                .OrderBy(n => n, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>Every Core namespace compiled into this assembly (after the module merges: RenderStream, Replay, Audio… too).</summary>
        [Fact]
        public void NoPublicSimTypeSharesItsNameWithAnotherCoreNamespace()
        {
            var clashes = new List<string>();
            var simNames = new HashSet<string>(SimPublicTypeNames(), StringComparer.Ordinal);
            foreach (Type t in typeof(PhysicsWorld).Assembly.GetExportedTypes())
            {
                if (t.IsNested || t.Namespace == null || t.Namespace == "Ballverse.Core.Sim" || !t.Namespace.StartsWith("Ballverse.Core", StringComparison.Ordinal)) continue;
                if (simNames.Contains(t.Name)) clashes.Add(t.FullName);
            }
            Assert.Empty(clashes);
        }

        /// <summary>
        /// Public top-level type names of the Core modules merged to main after this branch was cut (RenderStream e5932fc,
        /// Replay) and of Audio, as of 2026-10-18. The reflection test above covers them automatically once merged.
        /// </summary>
        static readonly string[] OtherModuleTypeNames =
        {
            // RenderStream
            "BodyFlags", "BodyState", "GridTopology", "HudEntry", "RenderFrame", "RenderGridSpec", "RenderQuantization",
            "RenderStreamBlockInfo", "RenderStreamHeader", "RenderStreamLimits", "RenderStreamReader", "RenderStreamRetention",
            "RenderStreamWriter", "RingGap", "ShapeDefinition", "ShapeKind", "ShapePoint",
            // Replay
            "BvrFormat", "HashMismatchKind", "HashVerificationResult", "HashVerifier", "PhysicsBackend", "ReplayEvent",
            "ReplayEventType", "ReplayFile", "ReplayHeader", "ReplayInput", "ReplayParamChange", "ReplayQuantization",
            "ReplayReader", "ReplayResult", "ReplayStateHash", "ReplayWriter", "StateHashBuilder",
            "UnsupportedReplayVersionException",
            // Audio
            "AudioConstants", "AudioMath", "AudioSample", "BodyRateLimiter", "EventMixer", "EventMixerOptions", "HapticsLimiter",
            "ISampleBank", "InMemorySampleBank", "LoudnessMeter", "LoudnessNormalizer", "LoudnessResult", "Melody", "MelodyCursor",
            "MelodyJson", "MelodyNote", "NormalizationReport", "NormalizationSettings", "PeakLimiter", "PitchMode",
            "PlaybackTimeMap", "SampleSelection", "ScheduledSoundEvent", "SlidingRateWindow", "SoundDecision", "SoundEvent",
            "SoundEventProcessor", "SoundEventQueue", "SoundEventType", "SoundPack", "SoundPackCatalog", "SoundPackId",
            "SoundPackJson", "SoundPitch", "SoundRule", "TrackMixSettings", "TrackMixer", "TruePeak", "VoiceInfo", "VoiceRequest",
        };

        [Fact]
        public void NoPublicSimTypeSharesItsNameWithRenderStreamReplayOrAudio()
        {
            var others = new HashSet<string>(OtherModuleTypeNames, StringComparer.Ordinal);
            string[] clashes = SimPublicTypeNames().Where(others.Contains).ToArray();
            Assert.True(clashes.Length == 0, "Sim types clashing with other Core modules: " + string.Join(", ", clashes));
        }
    }
}
