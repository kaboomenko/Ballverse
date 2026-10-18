using System;
using System.Collections.Generic;
using Ballverse.Core.Audio;
using Ballverse.Core.Replay;

namespace Ballverse.Core.Integration
{
    /// <summary>
    /// Supplies what a replay EVNT record does not store but the mixer needs (spec §2.7, §7.8): the emitting body's size and
    /// stereo position. Realtime and export must read them from the same quantized source (the render stream's radius and
    /// x position at the event tick, spec §7.4), otherwise size-dependent pitch can round differently and the export would not
    /// sound like the app.
    /// </summary>
    public interface ISoundBodySource
    {
        /// <summary>
        /// Body size (diameter, m) and pan (−1 left … 1 right) for <paramref name="e"/>. Returns false when the event has no
        /// emitting body under the mode's convention (e.g. a reserved body id for a finale or score event); the sound event then
        /// gets body id −1 (exempt from the per-body rate limit) and still uses <paramref name="bodySize"/> / <paramref name="pan"/>.
        /// </summary>
        bool TryGetBody(in ReplayEvent e, out float bodySize, out float pan);
    }

    /// <summary>
    /// Fixed size and pan for every event, with an optional reserved body id meaning "no body". For modes or tests without a
    /// render stream; real modes should implement <see cref="ISoundBodySource"/> over their render stream.
    /// </summary>
    public sealed class UniformSoundBodySource : ISoundBodySource
    {
        readonly float _size;
        readonly float _pan;
        readonly int _bodylessId;

        /// <param name="bodySize">Diameter, m.</param>
        /// <param name="pan">−1 … 1.</param>
        /// <param name="bodylessId">Replay body id that marks events without a body, or −1 for none (replay ids are ≥ 0).</param>
        public UniformSoundBodySource(float bodySize, float pan = 0f, int bodylessId = -1)
        {
            _size = bodySize;
            _pan = pan;
            _bodylessId = bodylessId;
        }

        public bool TryGetBody(in ReplayEvent e, out float bodySize, out float pan)
        {
            bodySize = _size;
            pan = _pan;
            return e.BodyId != _bodylessId;
        }
    }

    /// <summary>
    /// Replay EVNT records → Audio <see cref="SoundEvent"/>s (spec §2.7: the same events drive realtime sound and the offline
    /// export mix). Type converts by value (<see cref="SoundEventType"/> codes equal <see cref="ReplayEventType"/> codes),
    /// the tick is kept, the impulse is dequantized (<see cref="ReplayQuantization.DequantizeImpulse"/>, N·s), and size / pan /
    /// bodylessness come from an <see cref="ISoundBodySource"/>.
    /// </summary>
    public static class ReplaySoundEvents
    {
        /// <summary>Event type by value; both enums share the stable EVNT codes 0–7.</summary>
        public static SoundEventType ToSoundEventType(ReplayEventType type)
        {
            if (type > ReplayEventType.Custom) throw new ArgumentOutOfRangeException(nameof(type), type, "Undefined event type");
            return (SoundEventType)(int)type;
        }

        /// <exception cref="ArgumentOutOfRangeException">The tick does not fit the mixer's int tick (beyond any 180 s run).</exception>
        public static SoundEvent ToSoundEvent(in ReplayEvent e, ISoundBodySource bodies)
        {
            if (bodies == null) throw new ArgumentNullException(nameof(bodies));
            if (e.Tick > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(e), e.Tick, "Event tick beyond the int range");
            bool hasBody = bodies.TryGetBody(in e, out float size, out float pan);
            return new SoundEvent((int)e.Tick, ToSoundEventType(e.Type), hasBody ? e.BodyId : -1,
                ReplayQuantization.DequantizeImpulse(e.Impulse), size, pan);
        }

        /// <summary>The event can be converted: its tick fits the mixer's int tick (<see cref="ToSoundEvent"/>).</summary>
        public static bool IsConvertible(in ReplayEvent e)
        {
            return e.Tick <= int.MaxValue;
        }

        /// <summary>
        /// Appends the converted events to <paramref name="output"/> in record (tick) order. Never throws on replay data:
        /// <see cref="ReplayReader"/> bounds record ticks only by the header's u32 <c>durationTicks</c>, so an event of a
        /// corrupted or hostile file whose tick does not fit an int (far beyond any 180 s run) is skipped and counted instead.
        /// </summary>
        /// <returns>The number of skipped events (0 for any replay the app writes).</returns>
        public static int Convert(IReadOnlyList<ReplayEvent> events, ISoundBodySource bodies, List<SoundEvent> output)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            if (bodies == null) throw new ArgumentNullException(nameof(bodies));
            if (output == null) throw new ArgumentNullException(nameof(output));
            int skipped = 0;
            for (int i = 0; i < events.Count; i++)
            {
                ReplayEvent e = events[i];
                if (!IsConvertible(in e))
                {
                    skipped++;
                    continue;
                }
                output.Add(ToSoundEvent(in e, bodies));
            }
            return skipped;
        }

        /// <summary>
        /// The converted events of a whole replay, in record (tick) order; events whose tick does not fit an int are skipped
        /// (see <see cref="Convert(IReadOnlyList{ReplayEvent}, ISoundBodySource, List{SoundEvent})"/>). Never throws on replay data.
        /// </summary>
        public static SoundEvent[] Convert(ReplayFile replay, ISoundBodySource bodies)
        {
            return Convert(replay, bodies, out _);
        }

        /// <summary>As <see cref="Convert(ReplayFile, ISoundBodySource)"/>, also reporting how many events were skipped.</summary>
        public static SoundEvent[] Convert(ReplayFile replay, ISoundBodySource bodies, out int skipped)
        {
            if (replay == null) throw new ArgumentNullException(nameof(replay));
            var list = new List<SoundEvent>(replay.Events.Count);
            skipped = Convert(replay.Events, bodies, list);
            return list.ToArray();
        }
    }
}
