using System.Collections.Generic;
using System.IO;
using Ballverse.Core;
using Ballverse.Core.Audio;
using Ballverse.Core.Integration;
using Ballverse.Core.Modes;
using Ballverse.Core.Params;
using Ballverse.Core.RenderStream;
using Ballverse.Core.Replay;
using NUnit.Framework;

namespace Ballverse.App.Tests
{
    public sealed partial class QaRegressionTests
    {
        /// <summary>
        /// QA round 3 (r2), integration step of Core/Modes CONTRACT §3 rule 4 (README "Placeholders and gaps", Studio track):
        /// the Studio mixer rebuilds a clip's sound from its replay and render stream. For M01 that equals the live run only
        /// when the generic render-stream source is wrapped by the mode's <see cref="IModeSoundMapping"/>, resolved from the
        /// replay header's modeId with the App's registry — the exact call the Studio's <c>StudioAudioMixer.Mix</c> must make
        /// (it makes the bare one today, which this test shows to differ in pitch).
        /// </summary>
        [Test]
        public void StudioSoundConversionMatchesTheLiveRunOnlyThroughTheModeMapping()
        {
            TestApp app = Boot();
            ModeRegistry modes = app.Context.Modes;
            Assert.IsTrue(modes.TryGet("ring_escape", out IModeDefinition mode));
            ParamSet values = ModeParams.Prepare(mode, ModeParams.Defaults(mode, DeviceTier.A), DeviceTier.A).Values;
            var replay = new MemoryStream();
            var stream = new MemoryStream();
            var live = new List<SoundEvent>();
            var session = new ModeSession(mode, values, 2026UL,
                new ModeSessionOptions { Replay = replay, RenderStream = stream, SoundEvents = live, AppVersion = "test" });
            session.Start();
            while (session.Tick < SimConstants.MaxDurationTicks && !session.IsFinished)
            {
                if (!session.Step()) break;
            }
            session.Complete();
            Assert.Greater(live.Count, 50, "the defaults pass rings and break them");

            ReplayFile file = ReplayReader.Read(new MemoryStream(replay.ToArray()));
            Assert.AreEqual("ring_escape", file.Header.ModeId);
            SoundEvent[] bare, mapped;
            using (var reader = new RenderStreamReader(new MemoryStream(stream.ToArray()), true))
            {
                bare = ReplaySoundEvents.Convert(file, new Ballverse.Core.Studio.RenderStreamSoundBodySource(reader));
            }
            using (var reader = new RenderStreamReader(new MemoryStream(stream.ToArray()), true))
            {
                mapped = ReplaySoundEvents.Convert(file,
                    ModeSoundMapping.ForReplay(modes, file.Header.ModeId, new Ballverse.Core.Studio.RenderStreamSoundBodySource(reader)));
            }

            Assert.AreEqual(live.Count, mapped.Length);
            for (int i = 0; i < live.Count; i++)
                Assert.AreEqual(live[i], mapped[i], "event " + i + ": the mapped conversion equals the live run field for field");

            int differing = 0;
            for (int i = 0; i < live.Count; i++)
            {
                if (!live[i].Equals(bare[i])) differing++;
            }
            Assert.Greater(differing, live.Count / 2, "the bare conversion (what Studio does today) loses the ring-progress pitch");
        }
    }
}
