using System;
using System.Linq;
using Ballverse.Core.Layout;
using Ballverse.Core.Projects;
using Ballverse.Core.Studio;
using Ballverse.Core.Timeline;
using Xunit;

namespace Ballverse.Core.Tests.Studio
{
    /// <summary>Monospace test font: every glyph 50 px wide at size 100, ink 40×70 above the baseline, space without ink.</summary>
    sealed class MonoFont : IGlyphSource
    {
        public float Size => 100f;
        public float Ascent => 80f;
        public float LineHeight => 120f;

        public bool TryGetGlyph(char c, out GlyphMetrics metrics)
        {
            if (c == '\u0001')
            {
                metrics = default(GlyphMetrics);
                return false;
            }
            metrics = c == ' ' ? new GlyphMetrics(50f, 0f, 0f, 0f, 0f) : new GlyphMetrics(50f, 5f, 45f, 0f, 70f);
            return true;
        }
    }

    public class TextBlockLayoutTests
    {
        static TextLayer Layer(string text, int size = 100, TextAlign align = TextAlign.Center, double x = 0.5, double y = 0.5)
        {
            return new TextLayer { Id = "t1", Text = text, Size = size, Align = align, Pos = new[] { x, y } };
        }

        [Fact]
        public void SingleLineIsCenteredOnTheAnchor()
        {
            TextBlock b = TextBlockLayout.Layout(Layer("ABCD"), "ABCD", new MonoFont());
            Assert.Equal(4, b.Glyphs.Count);
            Assert.Equal(new FloatRect(440f, 900f, 200f, 120f), b.TextRect);
            // first glyph: pen 440, ink 445..485, baseline 900 + 80 → top 910, bottom 980
            Assert.Equal(FloatRect.FromEdges(445f, 910f, 485f, 980f), b.Glyphs[0].Rect);
            Assert.Equal(b.TextRect, b.Bounds);
            Assert.Equal(new FloatVec2(540f, 960f), b.Center);
            FloatRect n = b.NormalizedBounds;
            Assert.Equal(440f / 1080f, n.X, 5);
        }

        [Fact]
        public void LinesAlignInsideTheBlock()
        {
            TextBlock left = TextBlockLayout.Layout(Layer("AAAA\nBB", align: TextAlign.Left), "AAAA\nBB", new MonoFont());
            Assert.Equal(6, left.Glyphs.Count);
            Assert.Equal(445f, left.Glyphs[4].Rect.X); // second line starts at the block's left edge
            Assert.Equal(240f, left.TextRect.Height);
            TextBlock right = TextBlockLayout.Layout(Layer("AAAA\nBB", align: TextAlign.Right), "AAAA\nBB", new MonoFont());
            Assert.Equal(545f, right.Glyphs[4].Rect.X);
            TextBlock center = TextBlockLayout.Layout(Layer("AAAA\nBB"), "AAAA\nBB", new MonoFont());
            Assert.Equal(495f, center.Glyphs[4].Rect.X);
        }

        [Fact]
        public void LongTextWrapsAtSpacesAndLongWordsBreak()
        {
            // 972 px max → 19 characters of 50 px per line
            string words = "AAAAAAAAAA BBBBBBBBBB CCCCC";
            TextBlock b = TextBlockLayout.Layout(Layer(words), words, new MonoFont());
            Assert.Equal(240f, b.TextRect.Height); // "AAAAAAAAAA" / "BBBBBBBBBB CCCCC"
            Assert.Equal(800f, b.TextRect.Width);
            Assert.Equal('B', b.Glyphs[10].Character);
            Assert.Equal(b.TextRect.X + 5f, b.Glyphs[10].Rect.X); // second line (the wider one) starts at the block edge
            Assert.True(b.TextRect.Width <= TextBlockLayout.MaxLineWidth);
            string longWord = new string('W', 30);
            TextBlock w = TextBlockLayout.Layout(Layer(longWord), longWord, new MonoFont());
            Assert.Equal(240f, w.TextRect.Height);
            Assert.Equal(30, w.Glyphs.Count);
        }

        [Fact]
        public void PlateStrokeAndShadowGrowTheBounds()
        {
            TextLayer l = Layer("AB");
            l.Plate = TextPlate.Glass;
            l.Stroke = new TextStroke { Px = 8, Color = "#FF000080" };
            l.Shadow = true;
            l.Fill = "#00FF00";
            TextBlock b = TextBlockLayout.Layout(l, "AB", new MonoFont());
            Assert.Equal(TextPlate.Glass, b.Plate);
            Assert.Equal(FloatRect.FromEdges(490f - 35f, 900f - 18f, 590f + 35f, 1020f + 18f), b.PlateRect);
            Assert.Equal(30f, b.PlateRadius, 3);
            Assert.Equal(8f, b.StrokePx);
            Assert.Equal(new StudioColor(0f, 1f, 0f, 1f), b.Fill);
            Assert.Equal(128f / 255f, b.StrokeColor.A, 4);
            Assert.Equal(new FloatVec2(5f, 5f), b.ShadowOffset);
            Assert.True(b.Bounds.X <= b.PlateRect.X && b.Bounds.Right >= b.PlateRect.Right);
        }

        [Fact]
        public void BlockIsShiftedInsideTheCanvas()
        {
            TextBlock b = TextBlockLayout.Layout(Layer("ABCDEFGH", x: 1.0, y: 0.0), "ABCDEFGH", new MonoFont());
            Assert.Equal(1080f, b.Bounds.Right, 3);
            Assert.Equal(0f, b.Bounds.Y, 3);
            Assert.True(b.Glyphs.All(g => g.Rect.Right <= 1080f && g.Rect.Y >= 0f));
        }

        [Fact]
        public void SizesAreClampedAndMissingGlyphsSkipped()
        {
            TextBlock b = TextBlockLayout.Layout(Layer("A\u0001B", size: 500), "A\u0001B", new MonoFont());
            Assert.Equal(160f, b.FontSize);
            Assert.Equal(2, b.Glyphs.Count);
            Assert.Equal(2, b.Glyphs[1].CharIndex);
            TextBlock empty = TextBlockLayout.Layout(Layer(""), "", new MonoFont());
            Assert.Empty(empty.Glyphs);
        }

        [Fact]
        public void MeasureAllUsesTheCounterValueAndSkipsEmptyLayers()
        {
            var layers = new[] { Layer("Left: {n}"), Layer(""), null };
            var boxes = TextBlockLayout.MeasureAll(layers, _ => new MonoFont());
            Assert.Single(boxes);
            Assert.Equal("Left: 888", TextBlockLayout.MeasureText(layers[0]));
            Assert.Equal(9 * 50f / 1080f, boxes[0].Width, 4);
        }

        [Fact]
        public void ColorsParse()
        {
            Assert.True(StudioColor.TryParse("#FFFFFF33", out StudioColor c));
            Assert.Equal(0x33 / 255f, c.A, 5);
            Assert.False(StudioColor.TryParse("FFFFFF", out _));
            Assert.False(StudioColor.TryParse("#GG0000", out _));
            Assert.Equal(StudioColor.Black, StudioColor.Parse(null, StudioColor.Black));
        }
    }

    sealed class TestClock : IStudioClock
    {
        public long Ms;
        public int Minute;
        public string NowIso() => "2026-10-18T12:" + Minute.ToString("D2") + ":00Z";
        public long NowMs() => Ms;
    }

    sealed class SeqIds : IIdSource
    {
        int _n;
        public string NewId() => "id" + (++_n);
    }

    public class ProjectLibraryTests
    {
        readonly InMemoryFileStore _runs = new InMemoryFileStore();
        readonly InMemoryFileStore _library = new InMemoryFileStore();
        readonly TestClock _clock = new TestClock();
        readonly ProjectLibrary _lib;

        public ProjectLibraryTests()
        {
            _lib = new ProjectLibrary(new ClipStore(_runs, _library), _clock, new SeqIds());
            string dir = _lib.Clips.BeginRun("run1");
            _runs.WriteAllBytes(dir + "/clip.bvr", new byte[] { 1 });
            _runs.WriteAllBytes(dir + "/clip.bvs", new byte[] { 2 });
            _lib.Clips.CommitRun("run1", "ring_escape", 600, _clock.NowIso());
        }

        [Fact]
        public void CreateNamesProjectsAfterTheModeWithNumbers()
        {
            ProjectDocument a = _lib.CreateFromRun("run1");
            ProjectDocument b = _lib.CreateFromRun("run1");
            Assert.Equal("Ring Escape", a.Name);
            Assert.Equal("Ring Escape 2", b.Name);
            Assert.Equal("id1", a.Id);
            _lib.Clips.SaveRunAsClip("run1", "clipA", _clock.NowIso(), "Favourite");
            Assert.Equal("Favourite", _lib.CreateFromClip("clipA").Name);
            Assert.Equal(3, _lib.List().Projects.Count);
        }

        [Fact]
        public void DuplicateRenameDeleteAndUsage()
        {
            ProjectDocument a = _lib.CreateFromRun("run1", "Mine");
            _clock.Minute = 5;
            ProjectDocument copy = _lib.Duplicate(a.Id, "Copy");
            Assert.Equal("Copy", copy.Name);
            Assert.True(_library.FileExists("projects/" + copy.Id + "/clip.bvr"));
            Assert.Equal("Renamed", _lib.Rename(a.Id, "Renamed").Name);
            Assert.True(_lib.StorageUsage(copy.Id).TotalBytes > 0);
            Assert.True(_lib.Delete(a.Id));
            Assert.False(_lib.Open(a.Id).Success);
        }

        [Fact]
        public void RecordExportAddsHistoryAndReExportUsesIt()
        {
            ProjectDocument a = _lib.CreateFromRun("run1");
            var result = new StudioExportResult { Resolution = 720, Fps = 30, ElapsedMs = 1234, ProjectFile = null };
            ProjectDocument saved = _lib.RecordExport(a.Id, result);
            Assert.Single(saved.Exports);
            Assert.Equal(720, saved.Output.Resolution);
            ExportSettings again = ProjectLibrary.ReExportSettings(saved, DeviceTier.A);
            Assert.Equal(ExportResolution.P720, again.Resolution);
            Assert.Equal(30, again.Fps);
            ExportSettings other = ProjectLibrary.ReExportSettings(saved, DeviceTier.A, null, ExportResolution.P1080, 60);
            Assert.Equal(60, other.Fps);
            Assert.Null(_lib.RecordExport(a.Id, StudioExportResult.Failure(ExportErrorCode.Cancelled, "", a.Id, "j")));
            Assert.Null(_lib.RecordExport("gone", result));
        }

        [Fact]
        public void EditSessionAutosavesAfterTheDebounce()
        {
            ProjectDocument a = _lib.CreateFromRun("run1");
            ProjectEditSession s = _lib.Edit(a);
            _clock.Ms = 1000;
            s.Apply(d => d.Name = "Edited");
            Assert.True(s.IsDirty);
            Assert.False(s.TryBeginSave(out _, out _));
            _clock.Ms = 2000;
            Assert.True(s.TryBeginSave(out ProjectDocument snapshot, out long token));
            Assert.False(s.TryBeginSave(out _, out _)); // one save at a time
            s.Apply(d => d.Name = "Edited again");
            s.WriteSnapshot(snapshot);
            s.CompleteSave(token);
            Assert.True(s.IsDirty); // the second edit is not covered
            Assert.Equal("Edited", _lib.Open(a.Id).Document.Name);
            Assert.True(s.SaveNow());
            Assert.Equal("Edited again", _lib.Open(a.Id).Document.Name);
            Assert.False(s.SaveNow());
        }

        [Fact]
        public void FailedSaveStaysDirtyAndRetries()
        {
            ProjectEditSession s = _lib.Edit(_lib.CreateFromRun("run1"));
            s.Apply(d => d.Name = "x");
            _clock.Ms = 5000;
            Assert.True(s.TryBeginSave(out _, out long token));
            s.FailSave(token, new System.IO.IOException("disk"));
            Assert.NotNull(s.LastSaveError);
            Assert.True(s.IsDirty);
            Assert.False(s.TryBeginSave(out _, out _));
            _clock.Ms = 6000;
            Assert.True(s.TryBeginSave(out _, out _));
        }

        [Fact]
        public void UndoRedoAndCoalescing()
        {
            ProjectEditSession s = _lib.Edit(_lib.CreateFromRun("run1"));
            int rev = s.Revision;
            s.Apply(d => d.Layout.Ratio = 0.4, "ratio");
            _clock.Ms = 100;
            s.Apply(d => d.Layout.Ratio = 0.45, "ratio");
            _clock.Ms = 200;
            s.Apply(d => d.Layout.Ratio = 0.6, "ratio");
            _clock.Ms = 5000;
            s.Apply(d => d.Layout.Swap = true);
            Assert.True(s.Revision > rev);
            Assert.True(s.Undo());
            Assert.False(s.Document.Layout.Swap);
            Assert.Equal(0.6, s.Document.Layout.Ratio, 6);
            Assert.True(s.Undo());
            Assert.Equal(0.5, s.Document.Layout.Ratio, 6); // the drag was one step
            Assert.False(s.Undo());
            Assert.True(s.Redo());
            Assert.Equal(0.6, s.Document.Layout.Ratio, 6);
            s.Apply(d => d.Layout.Ratio = 5.0); // clamped by the sanitizer
            Assert.Equal(0.7, s.Document.Layout.Ratio, 6);
            Assert.False(s.CanRedo);
        }

        [Fact]
        public void UndoDepthIsBounded()
        {
            ProjectEditSession s = _lib.Edit(_lib.CreateFromRun("run1"));
            for (int i = 0; i < 60; i++) s.Apply(d => d.Name = "n" + i);
            int undos = 0;
            while (s.Undo()) undos++;
            Assert.Equal(ProjectEditSession.MaxUndoSteps, undos);
        }

        [Fact]
        public void UserVideoDefaultsCapTheFragment()
        {
            UserVideoSettings v = UserVideoDefaults.Create("media/import_1.mp4", 300000000);
            Assert.Equal(180000000, v.TrimOutUs);
            Assert.Equal(0, v.OffsetUs);
            Assert.Throws<ArgumentException>(() => UserVideoDefaults.Create("project.json", 1));
            Assert.Equal(5, UserVideoDefaults.Create("media/a.mov", 5).TrimOutUs);
        }
    }
}
