using System;

namespace Ballverse.Core.Replay
{
    /// <summary>
    /// One simulation event (EVNT record, spec §7.4) used to drive sound and VFX identically in preview and export (spec §2.7).
    /// </summary>
    public readonly struct ReplayEvent : IEquatable<ReplayEvent>
    {
        /// <param name="tick">Tick of the event.</param>
        /// <param name="type">Event kind; must be a defined <see cref="ReplayEventType"/>.</param>
        /// <param name="bodyId">Id of the body involved (non-negative). Modes choose a convention for bodiless events.</param>
        /// <param name="impulse">Quantized impulse, see <see cref="ReplayQuantization.QuantizeImpulse"/>.</param>
        /// <param name="extra">Mode-defined extra value (other body id, score delta, custom code…), zigzag varint.</param>
        public ReplayEvent(uint tick, ReplayEventType type, int bodyId, ushort impulse, long extra)
        {
            if (type > ReplayEventType.Custom) throw new ArgumentOutOfRangeException(nameof(type), "Undefined event type " + (int)type);
            if (bodyId < 0) throw new ArgumentOutOfRangeException(nameof(bodyId), "bodyId must be non-negative");
            Tick = tick;
            Type = type;
            BodyId = bodyId;
            Impulse = impulse;
            Extra = extra;
        }

        public uint Tick { get; }

        public ReplayEventType Type { get; }

        public int BodyId { get; }

        public ushort Impulse { get; }

        public long Extra { get; }

        public bool Equals(ReplayEvent other)
        {
            return Tick == other.Tick && Type == other.Type && BodyId == other.BodyId && Impulse == other.Impulse && Extra == other.Extra;
        }

        public override bool Equals(object obj)
        {
            return obj is ReplayEvent other && Equals(other);
        }

        public override int GetHashCode()
        {
            return (((((int)Tick * 31) + (int)Type) * 31 + BodyId) * 31 + Impulse) * 31 + Extra.GetHashCode();
        }

        public override string ToString()
        {
            return "Event@" + Tick + " " + Type + " body=" + BodyId + " impulse=" + Impulse + " extra=" + Extra;
        }
    }
}
