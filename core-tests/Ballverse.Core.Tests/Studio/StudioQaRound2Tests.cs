using System;
using System.Collections.Generic;
using Ballverse.Core.Layout;
using Ballverse.Core.Projects;
using Ballverse.Core.Studio;
using Ballverse.Core.Timeline;
using Xunit;

namespace Ballverse.Core.Tests.Studio
{
    /// <summary>Regression tests for the Core parts of the studio QA round-2 fixes.</summary>
    public class StudioQaRound2Tests
    {
        // ---- AudioMixKey covers every input of the user-audio placement --------------------------------------------------------

        [Fact]
        public void AudioMixKeyChangesWithTheVideoAfterEndBehaviour()
        {
            ProjectDocument doc = ProjectDefaults.NewProject("p", "P", "2026-10-19T10:00:00Z", "ring_escape", 1800); // 30 s clip
            doc.UserVideo = UserVideoDefaults.Create("media/a.mp4", 10_000_000);                                    // 10 s video
            doc.UserVideo.AfterEnd = VideoAfterEnd.Freeze;
            StudioTimeline freeze = ProjectComposition.BuildTimeline(doc, 60, 1800);
            string key = ProjectComposition.AudioMixKey(doc, freeze);

            doc.UserVideo.AfterEnd = VideoAfterEnd.Loop;
            StudioTimeline loop = ProjectComposition.BuildTimeline(doc, 60, 1800);
            Assert.Equal(freeze.FrameCount, loop.FrameCount); // the output length does not change, only the placed audio
            Assert.NotEqual(key, ProjectComposition.AudioMixKey(doc, loop));

            foreach (VideoAfterEnd other in new[] { VideoAfterEnd.Black, VideoAfterEnd.ExpandSim })
            {
                doc.UserVideo.AfterEnd = other;
                Assert.NotEqual(key, ProjectComposition.AudioMixKey(doc, ProjectComposition.BuildTimeline(doc, 60, 1800)));
            }
            doc.UserVideo.AfterEnd = VideoAfterEnd.Freeze;
            Assert.Equal(key, ProjectComposition.AudioMixKey(doc, ProjectComposition.BuildTimeline(doc, 60, 1800)));
        }

        // ---- Imported media survive undo / redo; only unreferenced files are deleted at open / close ---------------------------

        sealed class Clock : IStudioClock
        {
            public long Ms;
            public string NowIso() => "2026-10-19T10:00:00Z";
            public long NowMs() => Ms += 2000; // every edit is its own undo step
        }

        sealed class Ids : IIdSource
        {
            int _n;
            public string NewId() => "id" + (++_n);
        }

        [Fact]
        public void UndoAfterReplacingOrRemovingTheVideoFindsThePreviousCopy()
        {
            // QA round 2: ImportUserVideoAsync / RemoveUserVideo deleted the previous copy right after the undoable edit, so Undo
            // pointed the project at a deleted file. The studio now keeps every copy during the session and deletes, at open and
            // close, only what ProjectMediaCleanup reports for the saved document.
            var store = new InMemoryFileStore();
            var repo = new ProjectRepository(store);
            repo.Create(ProjectDefaults.NewProject("p1", "Reel", "2026-10-19T10:00:00Z", "ring_escape", 600));
            var lib = new ProjectLibrary(new ClipStore(new InMemoryFileStore(), store, null, repo), new Clock(), new Ids());
            ProjectEditSession s = lib.Edit(repo.Load("p1").Document);
            var files = new List<string>();

            void Import(string file)
            {
                store.WriteAllBytes("projects/p1/" + file, new byte[] { 1 });
                files.Add(file);
                s.Apply(d => d.UserVideo = UserVideoDefaults.Create(file, 10_000_000));
                s.SaveNow();
            }

            Import("media/a.mp4");
            Import("media/b.mp4");
            Assert.True(s.Undo());
            Assert.Equal("media/a.mp4", s.Document.UserVideo.File);
            Assert.True(store.FileExists("projects/p1/media/a.mp4"));
            Assert.True(s.Redo());

            s.Apply(d => d.UserVideo = null); // RemoveUserVideo
            s.SaveNow();
            Assert.True(s.Undo());
            Assert.Equal("media/b.mp4", s.Document.UserVideo.File);
            Assert.True(store.FileExists("projects/p1/media/b.mp4"));

            // Close: the saved document is the only one that can come back.
            s.SaveNow();
            ProjectDocument saved = repo.Load("p1").Document;
            Assert.Equal(new[] { "media/a.mp4" }, ProjectMediaCleanup.Unreferenced(new[] { saved }, files));
        }

        [Fact]
        public void CleanupKeepsReferencedWorkAndForeignFiles()
        {
            ProjectDocument doc = ProjectDefaults.NewProject("p1", "Reel", "2026-10-19T10:00:00Z", "ring_escape", 600);
            doc.UserVideo = UserVideoDefaults.Create("media/Import_1.mp4", 10_000_000);
            doc.Exports.Add(new ExportRecord { At = "2026-10-19T10:00:00Z", Res = 1080, Fps = 60, File = "media/kept.mp4" });
            var files = new[]
            {
                "media/import_1.mp4",       // same file on a case-insensitive file system: kept
                "media/kept.mp4",           // export kept with the project
                "media/old.mp4",            // replaced earlier: deleted
                "media/pick.mp4.part",      // a copy in progress: never touched
                "media/x.tmp",
                "exports/other.mp4",        // outside media/
                "media/../project.json",    // unsafe
                "",
            };
            Assert.Equal(new[] { "media/old.mp4" }, ProjectMediaCleanup.Unreferenced(new[] { doc }, files));
            // Several documents (e.g. an undo snapshot) keep the union.
            ProjectDocument other = ProjectJson.Clone(doc);
            other.UserVideo = UserVideoDefaults.Create("media/old.mp4", 10_000_000);
            Assert.Empty(ProjectMediaCleanup.Unreferenced(new[] { doc, other }, files));
            Assert.Throws<ArgumentNullException>(() => ProjectMediaCleanup.Unreferenced(null, files));
        }

        // ---- Export: the zoomed simulation stays sharp without a viewport renderer ---------------------------------------------

        [Fact]
        public void ExportSimTargetGrowsWithZoomUpToTheBudget()
        {
            const int slotW = 1080, slotH = 1920;
            SlotContent content = SlotContent.Default;
            foreach (float zoom in new[] { 1f, 1.5f, 2f })
            {
                SimSlotRender r = SimSlotSizing.Compute(slotW, slotH, content.WithZoom(zoom), maxZoomDensity: SimSlotSizing.ExportMaxZoomDensity);
                Assert.InRange(r.Mapping.Scale, 0.99f, 1.01f); // one output pixel per texel up to the budget
            }
            SimSlotRender z2 = SimSlotSizing.Compute(slotW, slotH, content.WithZoom(2f), maxZoomDensity: SimSlotSizing.ExportMaxZoomDensity);
            Assert.Equal(new IntVec2(2160, 3840), z2.TargetSize);
            // Beyond the budget the target stops growing (memory, §7.11) and the picture is magnified.
            SimSlotRender z8 = SimSlotSizing.Compute(slotW, slotH, content.WithZoom(8f), maxZoomDensity: SimSlotSizing.ExportMaxZoomDensity);
            Assert.Equal(z2.TargetSize, z8.TargetSize);
            Assert.InRange(z8.Mapping.Scale, 3.99f, 4.01f);
            // The preview (density 1) keeps the zoom-1 size; the cap of the longer side still applies.
            Assert.Equal(new IntVec2(1080, 1920), SimSlotSizing.Compute(slotW, slotH, content.WithZoom(2f)).TargetSize);
            SimSlotRender capped = SimSlotSizing.Compute(slotW, slotH, content.WithZoom(2f), maxSide: 2048, maxZoomDensity: SimSlotSizing.ExportMaxZoomDensity);
            Assert.True(capped.TargetSize.Y <= 2048);
            // Invalid budgets fall back to 1.
            Assert.Equal(new IntVec2(1080, 1920), SimSlotSizing.Compute(slotW, slotH, content.WithZoom(2f), maxZoomDensity: float.NaN).TargetSize);
        }

        // ---- Preview loop keeps its sound after the first wrap ----------------------------------------------------------------

        const long Tick = 16_667;
        const int BlockFrames = 1024;
        const long Latency = 4 * 21_333;

        /// <summary>OnTick of StudioPreviewPlayer with the real cursor pulled in 1024-frame blocks by a fake audio thread.</summary>
        sealed class PreviewHarness
        {
            public readonly PlaybackClock Clock;
            public readonly PreviewAudioSync Sync;
            public readonly PcmPlaybackCursor Cursor = new PcmPlaybackCursor();
            readonly float[] _block = new float[BlockFrames * 2];
            readonly long _blockUs = BlockFrames * 1_000_000L / 48_000;
            readonly long _mixUs;
            long _sinceBlock;
            public long FramesProduced;

            public PreviewHarness(long durationUs, bool loop)
            {
                Clock = new PlaybackClock(durationUs, 60) { Loop = loop };
                Sync = PreviewAudioSync.ForOutput(60, _blockUs, Latency);
                var mix = new float[durationUs * 48 / 1000 * 2];
                for (int i = 0; i < mix.Length; i++) mix[i] = 0.25f;
                Cursor.SetBuffer(mix);
                _mixUs = Cursor.FrameCount * 1_000_000L / 48_000;
            }

            public void Play(long atUs)
            {
                Clock.Seek(atUs);
                Clock.Play();
                StartAudioAtClock();
            }

            void StartAudioAtClock()
            {
                Cursor.SeekUs(Clock.PositionUs);
                Cursor.Playing = true;
                Sync.Start(Clock.PositionUs, Cursor.PositionUs);
            }

            public void Frame()
            {
                _sinceBlock += Tick;
                while (_sinceBlock >= _blockUs)
                {
                    _sinceBlock -= _blockUs;
                    FramesProduced += Cursor.Read(_block, 2, 48_000);
                }
                Sync.Advance(Clock, Tick, true, Cursor.PositionUs, Latency, _mixUs);
                if (Sync.NeedsAudioResync) StartAudioAtClock();
            }
        }

        [Fact]
        public void LoopingPreviewPlaysTheMixOnEveryLoopAndWrapsOnTime()
        {
            const long duration = 3_000_000;
            var h = new PreviewHarness(duration, loop: true);
            h.Play(0);
            var producedPerLoop = new List<long>();
            var wrapAtUs = new List<long>();
            int disc = h.Clock.Discontinuity;
            long now = 0, loopStartFrames = 0;
            for (int i = 0; i < 60 * 13; i++)
            {
                h.Frame();
                now += Tick;
                if (h.Clock.Discontinuity != disc)
                {
                    disc = h.Clock.Discontinuity;
                    wrapAtUs.Add(now);
                    producedPerLoop.Add(h.FramesProduced - loopStartFrames);
                    loopStartFrames = h.FramesProduced;
                }
            }
            Assert.True(wrapAtUs.Count >= 3, "wraps: " + wrapAtUs.Count);
            foreach (long frames in producedPerLoop) Assert.InRange(frames, 140_000L, 146_000L); // ≈ 144000 frames = 3 s every loop
            for (int i = 1; i < wrapAtUs.Count; i++)
            {
                // One loop = the duration plus at most the start hold (audio latency); never pinned near the end.
                Assert.InRange(wrapAtUs[i] - wrapAtUs[i - 1], duration - 2 * Tick, duration + h.Sync.MaxHoldUs + 2 * Tick);
            }
            Assert.True(h.Clock.IsPlaying);
            Assert.NotEqual(PreviewClockSource.WallClock, h.Sync.Source);
        }

        [Fact]
        public void PreviewReachesTheEndOnTimeWhenTheMixRunsOut()
        {
            const long duration = 3_000_000;
            var h = new PreviewHarness(duration, loop: false);
            h.Play(2_500_000);
            long now = 0;
            while (h.Clock.IsPlaying && now < 2_000_000)
            {
                h.Frame();
                now += Tick;
            }
            Assert.True(h.Clock.ReachedEnd);
            // 0.5 s of playback plus at most the start hold; the old code pinned the picture at the last audible sample first.
            Assert.InRange(now, 500_000 - 2 * Tick, 500_000 + h.Sync.MaxHoldUs + 2 * Tick);
        }
    }
}
