using System;
using Ballverse.Core.RenderStream;
using Xunit;

namespace Ballverse.Core.Tests.RenderStream
{
    public class RenderFrameTests
    {
        [Fact]
        public void AddGrowsAndClearKeepsCapacity()
        {
            var f = new RenderFrame(0, 0);
            for (int i = 0; i < 100; i++) f.AddBody(new BodyState(i, BodyKind.Circle, 0, 0, 0, 0, 0, BodyFlags.None));
            for (int i = 0; i < 10; i++) f.AddHud(i, i);
            Assert.Equal(100, f.BodyCount);
            Assert.Equal(10, f.HudCount);
            Assert.Equal(99, f.Bodies[99].Id);
            f.Clear();
            Assert.Equal(0, f.BodyCount);
            Assert.Equal(0, f.HudCount);
            Assert.True(f.Bodies.IsEmpty);
        }

        [Fact]
        public void SortAndLookup()
        {
            var f = new RenderFrame();
            f.AddBody(new BodyState(5, BodyKind.Circle, 1, 0, 0, 0, 0, BodyFlags.None));
            f.AddBody(new BodyState(-3, BodyKind.Polygon, 2, 0, 0, 0, 0, BodyFlags.None));
            f.AddHud(9, 90);
            f.AddHud(1, 10);
            f.Sort();
            Assert.Equal(-3, f.GetBody(0).Id);
            Assert.Equal(1, f.GetHud(0).Key);
            Assert.True(f.TryGetBody(5, out var b));
            Assert.Equal(1, b.X);
            Assert.False(f.TryGetBody(6, out _));
            Assert.True(f.TryGetHud(9, out int v));
            Assert.Equal(90, v);
            Assert.False(f.TryGetHud(2, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => f.GetBody(2));
            Assert.Throws<ArgumentOutOfRangeException>(() => f.GetHud(-1));
        }

        [Fact]
        public void CopyFromAndContentEquals()
        {
            var a = new RenderFrame();
            a.AddBody(new BodyState(1, BodyKind.Segment, 1, 2, 3, 4, 5, BodyFlags.Visible));
            a.AddHud(0, 7);
            var b = new RenderFrame(0, 0);
            b.CopyFrom(a);
            Assert.True(a.ContentEquals(b));
            b.AddHud(1, 1);
            Assert.False(a.ContentEquals(b));
            Assert.False(a.ContentEquals(null));
            a.CopyFrom(a);
            Assert.Equal(1, a.BodyCount);
            Assert.Throws<ArgumentNullException>(() => a.CopyFrom(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RenderFrame(-1, 0));
        }

        [Fact]
        public void BodyStateEquality()
        {
            var a = new BodyState(1, BodyKind.Circle, 1, 2, 3, 4, 5, BodyFlags.Visible);
            var b = a;
            Assert.True(a == b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            b.Flags = BodyFlags.None;
            Assert.True(a != b);
            Assert.False(a.Equals((object)"x"));
            Assert.Equal(new HudEntry(1, 2), new HudEntry(1, 2));
            Assert.True(new HudEntry(1, 2) != new HudEntry(1, 3));
        }
    }
}
