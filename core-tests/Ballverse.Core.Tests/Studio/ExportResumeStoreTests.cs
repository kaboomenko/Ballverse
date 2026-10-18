using System.Collections.Generic;
using System.Linq;
using Ballverse.Core.Layout;
using Ballverse.Core.Projects;
using Ballverse.Core.Studio;
using Ballverse.Core.Timeline;
using Xunit;

namespace Ballverse.Core.Tests.Studio
{
    public class ExportResumeStoreTests
    {
        readonly InMemoryFileStore _store = new InMemoryFileStore();

        internal static ExportResumeState State(string jobId = "job1", int frames = 300, int fps = 60, int segments = 0)
        {
            var s = new ExportResumeState
            {
                JobId = jobId,
                ProjectId = "p1",
                CreatedAt = "2026-10-18T12:00:00Z",
                Project = ProjectDefaults.NewProject("p1", "Project", "2026-10-18T12:00:00Z", "ring_escape", 600),
                Resolution = 1080,
                Fps = fps,
                VideoBitrateBps = 16000000,
                AudioBitrateBps = 192000,
                Watermark = true,
                MaskRects = ExportResumeState.FromRects(new[] { new FloatRect(0.8f, 0.3f, 0.2f, 0.4f) }),
                TextBounds = ExportResumeState.FromRects(new[] { new FloatRect(0.1f, 0.1f, 0.5f, 0.05f) }),
                StreamTicks = 600,
                FrameCount = frames,
                SegmentFrames = 2 * fps,
                OutputName = "Ballverse_Project_20261018_120000.mp4",
            };
            for (int i = 0; i < segments; i++)
            {
                s.Segments.Add(new ExportSegmentRecord { Index = i, File = StudioFiles.SegmentsDir + "/" + StudioFiles.SegmentFileName(i), FrameCount = System.Math.Min(2 * fps, frames - i * 2 * fps) });
            }
            return s;
        }

        [Fact]
        public void SaveLoadRoundTrip()
        {
            ExportResumeState s = State(segments: 2);
            ExportResumeStore.Save(_store, s);
            Assert.False(_store.FileExists("job1/export_state.json.tmp"));
            ExportResumeState back = ExportResumeStore.Load(_store, "job1", out string error);
            Assert.Null(error);
            Assert.Equal(2, back.Segments.Count);
            Assert.Equal(240, back.NextFrame);
            Assert.False(back.AllSegmentsDone);
            Assert.Equal("p1", back.Project.Id);
            Assert.Equal(new FloatRect(0.8f, 0.3f, 0.2f, 0.4f), back.GetMaskRects().Single());
            Assert.Equal(new FloatRect(0.1f, 0.1f, 0.5f, 0.05f), back.GetTextBounds().Single());
            ExportSettings settings = back.ToSettings();
            Assert.Equal(1080, settings.Width);
            Assert.Equal(16000000, settings.VideoBitrateBps);
            Assert.Equal(192000, settings.AudioBitrateBps);
        }

        [Fact]
        public void LastPartialSegmentCompletesTheJob()
        {
            ExportResumeState s = State(frames: 250, segments: 3); // 120 + 120 + 10
            Assert.Equal(10, s.Segments[2].FrameCount);
            Assert.True(s.AllSegmentsDone);
            Assert.Equal(250, s.NextFrame);
            Assert.Null(ExportResumeStore.Validate(s));
        }

        [Fact]
        public void ValidateRejectsBrokenStates()
        {
            Assert.NotNull(ExportResumeStore.Validate(null));
            var bad = State(); bad.Fps = 25; Assert.NotNull(ExportResumeStore.Validate(bad));
            bad = State(); bad.Resolution = 900; Assert.NotNull(ExportResumeStore.Validate(bad));
            bad = State(); bad.SegmentFrames = 100; Assert.NotNull(ExportResumeStore.Validate(bad));
            bad = State(); bad.JobId = "../x"; Assert.NotNull(ExportResumeStore.Validate(bad));
            bad = State(); bad.Project = null; Assert.NotNull(ExportResumeStore.Validate(bad));
            bad = State(segments: 2); bad.Segments[1].Index = 5; Assert.NotNull(ExportResumeStore.Validate(bad));
            bad = State(segments: 2); bad.Segments[0].FrameCount = 7; Assert.NotNull(ExportResumeStore.Validate(bad));
            bad = State(segments: 1); bad.Segments[0].File = "/abs"; Assert.NotNull(ExportResumeStore.Validate(bad));
            bad = State(frames: 100, segments: 1); bad.Segments.Add(new ExportSegmentRecord { Index = 1, File = "x", FrameCount = 1 });
            Assert.NotNull(ExportResumeStore.Validate(bad));
            bad = State(); bad.FrameCount = 181 * 60; Assert.NotNull(ExportResumeStore.Validate(bad));
            bad = State(); bad.SchemaVersion = 2; Assert.NotNull(ExportResumeStore.Validate(bad));
            Assert.Throws<System.ArgumentException>(() => ExportResumeStore.Save(_store, bad));
        }

        [Fact]
        public void LoadReportsMissingCorruptAndMismatchedStates()
        {
            Assert.Null(ExportResumeStore.Load(_store, "none", out string e1));
            Assert.NotNull(e1);
            _store.WriteAllText("bad/export_state.json", "{nope");
            Assert.Null(ExportResumeStore.Load(_store, "bad", out string e2));
            Assert.NotNull(e2);
            ExportResumeStore.Save(_store, State("job2"));
            _store.Copy("job2/export_state.json", "other/export_state.json");
            Assert.Null(ExportResumeStore.Load(_store, "other", out string e3));
            Assert.Equal("job id mismatch", e3);
            Assert.Null(ExportResumeStore.Load(_store, "..", out _));
        }

        [Fact]
        public void ListIsNewestFirst()
        {
            ExportResumeState a = State("a"); a.CreatedAt = "2026-10-18T10:00:00Z";
            ExportResumeState b = State("b"); b.CreatedAt = "2026-10-18T11:00:00Z";
            ExportResumeStore.Save(_store, a);
            ExportResumeStore.Save(_store, b);
            _store.WriteAllText("junk/file.bin", "x");
            Assert.Equal(new[] { "b", "a" }, ExportResumeStore.List(_store).Select(s => s.JobId));
        }

        [Fact]
        public void UsableSegmentsStopAtTheFirstMissingFile()
        {
            ExportResumeState s = State(segments: 3);
            _store.WriteAllBytes("job1/" + s.Segments[0].File, new byte[] { 1 });
            _store.CreateDirectory("job1/" + s.Segments[1].File); // fake backend writes a folder
            Assert.Equal(2, ExportResumeStore.CountUsableSegments(_store, s));
            _store.DeleteFile("job1/" + s.Segments[0].File);
            Assert.Equal(0, ExportResumeStore.CountUsableSegments(_store, s));
        }

        [Fact]
        public void StartupCleanupKeepsResumableJobsOnRequest()
        {
            ExportResumeStore.Save(_store, State("keepme"));
            _store.WriteAllBytes("stale/segments/seg_0000.mp4", new byte[] { 1 });
            _store.WriteAllBytes("outputs/x.mp4", new byte[] { 1 });
            _store.WriteAllText("loose.tmp", "x");
            IReadOnlyList<string> deleted = ExportResumeStore.CleanupAtStartup(_store, true);
            Assert.Equal(new[] { "outputs", "stale" }, deleted.OrderBy(x => x, System.StringComparer.Ordinal));
            Assert.True(_store.DirectoryExists("keepme"));
            Assert.False(_store.FileExists("loose.tmp"));
            ExportResumeStore.CleanupAtStartup(_store, false);
            Assert.False(_store.DirectoryExists("keepme"));
        }

        [Fact]
        public void CleanupHonoursTheKeepList()
        {
            _store.WriteAllBytes("busy/a", new byte[] { 1 });
            ExportResumeStore.CleanupAtStartup(_store, false, new HashSet<string> { "busy" });
            Assert.True(_store.DirectoryExists("busy"));
            ExportResumeStore.DeleteJob(_store, "busy");
            Assert.False(_store.DirectoryExists("busy"));
        }
    }
}
