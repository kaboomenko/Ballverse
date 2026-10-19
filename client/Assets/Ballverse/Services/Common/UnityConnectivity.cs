using UnityEngine;

namespace Ballverse.Services
{
    /// <summary>
    /// <see cref="Application.internetReachability"/>: only "no network interface at all" counts as offline; a captive
    /// portal still looks online and is handled by the ad timeout (RW3).
    /// </summary>
    public sealed class UnityConnectivity : IConnectivity
    {
        public bool IsOffline => Application.internetReachability == NetworkReachability.NotReachable;
    }
}
