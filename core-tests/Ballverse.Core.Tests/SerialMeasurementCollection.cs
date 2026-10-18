using Xunit;

namespace Ballverse.Core.Tests
{
    /// <summary>
    /// xUnit collection for tests whose verdict is a measurement of the run itself: wall-clock timing ratios and per-thread
    /// allocation counts (<c>GC.GetAllocatedBytesForCurrentThread</c>). xUnit runs a collection with
    /// <c>DisableParallelization = true</c> alone, after the parallel collections have finished, so the measured code does not
    /// compete with the rest of the suite for CPU, memory bandwidth, GC and tiered JIT.
    /// QA core-integration round 2: under the parallel run a timing ratio of a linear parser reached 10.7–19.1× for 4× the input,
    /// and zero-allocation checks failed in about 1 of 12 full runs (never in isolation).
    /// The assertions themselves are unchanged by this collection.
    /// </summary>
    [CollectionDefinition(Name, DisableParallelization = true)]
    public sealed class SerialMeasurementCollection
    {
        public const string Name = "Serial measurement";
    }
}
