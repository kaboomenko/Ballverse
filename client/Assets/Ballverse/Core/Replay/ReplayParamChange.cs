using System;

namespace Ballverse.Core.Replay
{
    /// <summary>
    /// A "live" parameter change made during a run (PARM record, spec §7.4): tick, parameter id and the new value as compact JSON.
    /// Immutable.
    /// </summary>
    public sealed class ReplayParamChange : IEquatable<ReplayParamChange>
    {
        /// <param name="tick">Tick at whose boundary the change applies.</param>
        /// <param name="paramId">Parameter id from the mode's parameter schema (spec §7.6). Non-empty.</param>
        /// <param name="valueJson">New value as a JSON text, e.g. "1.5", "true", "\"red\"". Non-empty.</param>
        public ReplayParamChange(uint tick, string paramId, string valueJson)
        {
            if (paramId == null) throw new ArgumentNullException(nameof(paramId));
            if (paramId.Length == 0) throw new ArgumentException("paramId must not be empty", nameof(paramId));
            if (valueJson == null) throw new ArgumentNullException(nameof(valueJson));
            if (valueJson.Length == 0) throw new ArgumentException("valueJson must not be empty", nameof(valueJson));
            Tick = tick;
            ParamId = paramId;
            ValueJson = valueJson;
        }

        public uint Tick { get; }

        public string ParamId { get; }

        public string ValueJson { get; }

        public bool Equals(ReplayParamChange other)
        {
            return other != null && other.Tick == Tick
                && string.Equals(other.ParamId, ParamId, StringComparison.Ordinal)
                && string.Equals(other.ValueJson, ValueJson, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ReplayParamChange);
        }

        public override int GetHashCode()
        {
            return ((int)Tick * 31 + StringComparer.Ordinal.GetHashCode(ParamId)) * 31 + StringComparer.Ordinal.GetHashCode(ValueJson);
        }

        public override string ToString()
        {
            return "Param@" + Tick + " " + ParamId + "=" + ValueJson;
        }
    }
}
