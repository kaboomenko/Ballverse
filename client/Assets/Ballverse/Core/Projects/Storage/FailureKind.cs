namespace Ballverse.Core.Projects
{
    /// <summary>How an injected <see cref="InMemoryFileStore"/> failure behaves.</summary>
    public enum FailureKind
    {
        /// <summary>The operation throws without any effect.</summary>
        Throw = 0,
        /// <summary>Writes and copies leave a truncated (half-length) destination, then throw; other operations behave like <see cref="Throw"/>.</summary>
        TornWrite = 1,
        /// <summary>The operation throws and the store then loses every unflushed write (<see cref="InMemoryFileStore.SimulateCrash"/>).</summary>
        Crash = 2,
    }
}
