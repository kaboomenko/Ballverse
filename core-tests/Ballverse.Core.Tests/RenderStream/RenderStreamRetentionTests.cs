using System;
using Ballverse.Core.RenderStream;
using Xunit;

namespace Ballverse.Core.Tests.RenderStream
{
    public class RenderStreamRetentionTests
    {
        const long MB = 1024 * 1024;

        [Fact]
        public void KeepsAtMostFiveStreams()
        {
            Assert.Equal(5, RenderStreamRetention.CountToKeep(new long[] { 1, 1, 1, 1, 1, 1, 1 }));
            Assert.Equal(3, RenderStreamRetention.CountToKeep(new long[] { 1, 1, 1 }));
            Assert.Equal(0, RenderStreamRetention.CountToKeep(new long[0]));
        }

        [Fact]
        public void KeepsNewestWithinTotalBudget()
        {
            Assert.Equal(2, RenderStreamRetention.CountToKeep(new[] { 200 * MB, 300 * MB, 1 * MB }));
            Assert.Equal(3, RenderStreamRetention.CountToKeep(new[] { 200 * MB, 299 * MB, 1 * MB, 1 * MB }));
            // An older small stream is not kept once an intermediate one broke the budget (ring order).
            Assert.Equal(1, RenderStreamRetention.CountToKeep(new[] { 400 * MB, 200 * MB, 1 * MB }));
        }

        [Fact]
        public void NewestIsAlwaysKept()
        {
            Assert.Equal(1, RenderStreamRetention.CountToKeep(new[] { 900 * MB, 1 * MB }));
        }

        [Fact]
        public void CustomLimitsAndValidation()
        {
            Assert.Equal(2, RenderStreamRetention.CountToKeep(new long[] { 10, 10, 10 }, 2, 100));
            Assert.Equal(2, RenderStreamRetention.CountToKeep(new long[] { 10, 10, 10 }, 5, 25));
            Assert.Throws<ArgumentNullException>(() => RenderStreamRetention.CountToKeep(null));
            Assert.Throws<ArgumentOutOfRangeException>(() => RenderStreamRetention.CountToKeep(new long[] { 1 }, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => RenderStreamRetention.CountToKeep(new long[] { 1 }, 5, -1));
            Assert.Throws<ArgumentException>(() => RenderStreamRetention.CountToKeep(new long[] { 1, -1 }));
        }
    }
}
