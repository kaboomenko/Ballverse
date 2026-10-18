namespace Ballverse.Media
{
    /// <summary>
    /// Device thermal state, unified across platforms. iOS maps 1:1 to <c>NSProcessInfoThermalState</c>.
    /// Android (API 29+): NONE → Nominal, LIGHT/MODERATE → Fair, SEVERE → Serious, CRITICAL/EMERGENCY/SHUTDOWN → Critical
    /// (spec §3.8: iOS <c>serious</c> ≙ Android <c>THERMAL_STATUS_SEVERE</c>).
    /// </summary>
    public enum ThermalState
    {
        Unknown = -1,
        Nominal = 0,
        Fair = 1,
        Serious = 2,
        Critical = 3,
    }

    /// <summary>Snapshot of device signals used by tiering, export pacing and the glass UI (spec §3.8, §4).</summary>
    public sealed class DeviceSignals
    {
        public ThermalState Thermal { get; }
        /// <summary>iOS Low Power Mode / Android Battery Saver.</summary>
        public bool LowPowerMode { get; }
        /// <summary>iOS Reduce Motion / Android "Remove animations" (animator duration scale 0).</summary>
        public bool ReduceMotion { get; }
        /// <summary>iOS Reduce Transparency. Android has no equivalent setting: always false there.</summary>
        public bool ReduceTransparency { get; }
        /// <summary>Free bytes on the app data volume (iOS: capacity for important usage); -1 if unknown.</summary>
        public long FreeSpaceBytes { get; }

        public DeviceSignals(ThermalState thermal, bool lowPowerMode, bool reduceMotion, bool reduceTransparency, long freeSpaceBytes)
        {
            Thermal = thermal;
            LowPowerMode = lowPowerMode;
            ReduceMotion = reduceMotion;
            ReduceTransparency = reduceTransparency;
            FreeSpaceBytes = freeSpaceBytes;
        }

        /// <summary>Export should slow down (pause between segments) at Serious or worse (spec §3.8).</summary>
        public bool ShouldThrottleExport => Thermal == ThermalState.Serious || Thermal == ThermalState.Critical;

        public static readonly DeviceSignals Unknown = new DeviceSignals(ThermalState.Unknown, false, false, false, -1);

        public override string ToString()
        {
            return "DeviceSignals(thermal " + Thermal + ", lowPower " + LowPowerMode + ", reduceMotion " + ReduceMotion
                + ", reduceTransparency " + ReduceTransparency + ", free " + FreeSpaceBytes + ")";
        }
    }
}
