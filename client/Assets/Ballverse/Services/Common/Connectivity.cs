namespace Ballverse.Services
{
    /// <summary>Network reachability as far as the services care (spec §5.4: offline → export without ad, interstitials skipped).</summary>
    public interface IConnectivity
    {
        bool IsOffline { get; }
    }

#if !BALLVERSE_STORE // fakes are not compiled into Store builds (spec §8.9)
    /// <summary>Settable connectivity (tests, editor).</summary>
    public sealed class FakeConnectivity : IConnectivity
    {
        public bool IsOffline { get; set; }
    }
#endif
}
