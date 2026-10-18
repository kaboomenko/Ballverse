using System;
using System.Linq;
using Ballverse.Core;
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
            for (int i = 0; i < 100; i++) f.AddBody(new BodyState(i, 0, 0, 0, 0, 0, 0, BodyFlags.None));
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
            f.AddBody(new BodyState(5, 0, 1, 0, 0, 0, 0, BodyFlags.None));
            f.AddBody(new BodyState(-3, 1, 2, 0, 0, 0, 0, BodyFlags.None));
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
            a.AddBody(new BodyState(1, 9, 1, 2, 3, 4, 5, BodyFlags.Visible));
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
        public void SortMatchesReferenceOrderAndDoesNotAllocate()
        {
            var rng = Rng.FromSeed(31, 1);
            var f = new RenderFrame();
            for (int iter = 0; iter < 200; iter++)
            {
                f.Clear();
                int n = iter == 0 ? 0 : rng.NextInt(1, iter < 100 ? 40 : 3000);
                for (int i = 0; i < n; i++)
                {
                    int id = iter % 3 == 0 ? rng.NextInt(-50, 50) : (int)rng.NextUInt();   // duplicates in every third iteration
                    f.AddBody(new BodyState(id, i, 0, 0, 0, 0, 0, BodyFlags.None));
                }
                int h = rng.NextInt(0, 30);
                for (int i = 0; i < h; i++) f.AddHud(rng.NextInt(0, 1000), i);
                var expectedIds = f.Bodies.ToArray().Select(b => b.Id).OrderBy(x => x).ToArray();
                var expectedKeys = f.Hud.ToArray().Select(e => e.Key).OrderBy(x => x).ToArray();
                var bodiesBefore = f.Bodies.ToArray().OrderBy(b => b.Id).ThenBy(b => b.ShapeId).ToArray();
                f.Sort();
                Assert.Equal(expectedIds, f.Bodies.ToArray().Select(b => b.Id).ToArray());
                Assert.Equal(expectedKeys, f.Hud.ToArray().Select(e => e.Key).ToArray());
                Assert.Equal(bodiesBefore, f.Bodies.ToArray().OrderBy(b => b.Id).ThenBy(b => b.ShapeId).ToArray()); // a permutation
            }

            // Reversed 2500-body frame: sorting in place allocates nothing.
            var big = new RenderFrame(2500, 8);
            for (int round = 0; round < 3; round++)
            {
                big.Clear();
                for (int i = 2500; i > 0; i--) big.AddBody(new BodyState(i, 0, 0, 0, 0, 0, 0, BodyFlags.None));
                for (int k = 7; k >= 0; k--) big.AddHud(k, k);
                long before = GC.GetAllocatedBytesForCurrentThread();
                big.Sort();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                if (round > 0) Assert.Equal(0, allocated);
            }
            Assert.Equal(1, big.GetBody(0).Id);
            Assert.Equal(2500, big.GetBody(2499).Id);
        }

        [Fact]
        public void BodyStateEquality()
        {
            var a = new BodyState(1, 0, 1, 2, 3, 4, 5, BodyFlags.Visible);
            var b = a;
            Assert.True(a == b);
            Assert.Equal(a.GetHashCode(), b.GetHashCode());
            b.Flags = BodyFlags.None;
            Assert.True(a != b);
            var c = a;
            c.ShapeId = 2;
            Assert.NotEqual(a, c);
            c = a;
            c.LabelId = 3;
            Assert.NotEqual(a, c);
            Assert.NotEqual(a.GetHashCode(), c.GetHashCode());
            Assert.Equal(BodyState.NoLabel, a.LabelId);
            Assert.Contains("label 3", c.ToString());
            Assert.DoesNotContain("label", a.ToString());
            c = a;
            c.SkinId = 4;
            Assert.NotEqual(a, c);
            Assert.NotEqual(a.GetHashCode(), c.GetHashCode());
            Assert.Equal(BodyState.NoSkin, a.SkinId);
            Assert.Contains("skin 4", c.ToString());
            Assert.DoesNotContain("skin", a.ToString());
            Assert.False(a.Equals((object)"x"));
            Assert.Equal(new HudEntry(1, 2), new HudEntry(1, 2));
            Assert.True(new HudEntry(1, 2) != new HudEntry(1, 3));
        }
    }
}
