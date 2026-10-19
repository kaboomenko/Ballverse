using Xunit;

// The Media layer keeps its main-thread binding in static state (MainThread captures the pump's synchronization context),
// so harness test classes that drive the fake backend must not run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
