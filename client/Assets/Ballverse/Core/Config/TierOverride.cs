using System;

namespace Ballverse.Core.Config
{
    /// <summary>Forced device tier for one device model (spec §8.1, risk R5). JSON form: <c>{"SM-A155F":"C"}</c>.</summary>
    public sealed class TierOverride
    {
        /// <summary>Device model string (trimmed, non-empty). Matching ignores case.</summary>
        public string DeviceModel { get; }
        public DeviceTier Tier { get; }

        /// <exception cref="ArgumentException">Empty model or undefined tier.</exception>
        public TierOverride(string deviceModel, DeviceTier tier)
        {
            if (string.IsNullOrWhiteSpace(deviceModel)) throw new ArgumentException("device model must be non-empty", nameof(deviceModel));
            if (tier != DeviceTier.A && tier != DeviceTier.B && tier != DeviceTier.C) throw new ArgumentException("undefined tier", nameof(tier));
            DeviceModel = deviceModel.Trim();
            Tier = tier;
        }
    }
}
