using System;
using Ballverse.Core.Replay;

namespace Ballverse.Core.RenderGeometry
{
    /// <summary>
    /// Kinds of render events (see RenderEventContract.md). Values are persisted in <see cref="RenderEventLog"/> files and
    /// must stay stable; new kinds get new values. The kinds that also exist as simulation events use the numbers of
    /// <see cref="ReplayEventType"/> (Hit 0, Break 1, Eliminate 2, Finish 3, Spawn 4); render-only kinds start at 16 so the
    /// two enums can never be confused. <see cref="RenderEventTypes.ToReplayType"/> is the explicit mapping.
    /// </summary>
    public enum RenderEventType : byte
    {
        /// <summary>Ball hits a wall / ring / platform / ball: impact particles (<c>impactFx</c>), ripple, ball squash, target flash.</summary>
        Hit = 0,
        /// <summary>A body breaks (ring, platform): shards along the broken shape (<c>breakFx</c>) and a light wave.</summary>
        Break = 1,
        /// <summary>A participant is eliminated / leaves through an opening: streak along the exit direction and a puff.</summary>
        Eliminate = 2,
        /// <summary>Finale burst at the winner / finish (<c>finaleStyle</c> confetti): confetti and a shockwave.</summary>
        Finish = 3,
        /// <summary>A ball is born (M02 «Размножение»): bubble ring + dots and a birth pulse of the new ball.</summary>
        Spawn = 4,
        /// <summary>Colour blot left on the target body at the contact (M05), fades in 3 s. Render-only.</summary>
        Splat = 16,
        /// <summary>The ball escaped the last ring (M01): big shockwave and confetti.</summary>
        Escape = 17,
        /// <summary>Fireball smash (M05 pierce): fiery burst and a short camera shake.</summary>
        Pierce = 18,
        /// <summary>
        /// A ball grew (M02 «Рост»): only the radius pulse of <see cref="RenderEvent.BodyId"/> («пульсация шара при росте»),
        /// no particles — the bubble belongs to birth. Render-only (its sound is the wall Hit that caused the growth).
        /// </summary>
        Grow = 19,
    }

    /// <summary>Helpers for <see cref="RenderEventType"/>: validity and the mapping to the replay's simulation event kinds.</summary>
    public static class RenderEventTypes
    {
        /// <summary>True for the values defined by <see cref="RenderEventType"/> (allocation-free).</summary>
        public static bool IsDefined(RenderEventType type)
        {
            switch (type)
            {
                case RenderEventType.Hit:
                case RenderEventType.Break:
                case RenderEventType.Eliminate:
                case RenderEventType.Finish:
                case RenderEventType.Spawn:
                case RenderEventType.Splat:
                case RenderEventType.Escape:
                case RenderEventType.Pierce:
                case RenderEventType.Grow:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// The simulation event kind (<c>.bvr</c> EVNT record, sound and haptics) a render event corresponds to: shared kinds
        /// map to themselves, <see cref="RenderEventType.Escape"/> is the run's <see cref="ReplayEventType.Finish"/>,
        /// <see cref="RenderEventType.Pierce"/> breaks platforms (<see cref="ReplayEventType.Break"/>). Returns false for
        /// <see cref="RenderEventType.Splat"/> and <see cref="RenderEventType.Grow"/>, which are purely visual (their sound
        /// is the accompanying / causing Hit).
        /// </summary>
        public static bool TryGetReplayType(RenderEventType type, out ReplayEventType replayType)
        {
            switch (type)
            {
                case RenderEventType.Hit: replayType = ReplayEventType.Hit; return true;
                case RenderEventType.Break: replayType = ReplayEventType.Break; return true;
                case RenderEventType.Eliminate: replayType = ReplayEventType.Eliminate; return true;
                case RenderEventType.Finish: replayType = ReplayEventType.Finish; return true;
                case RenderEventType.Spawn: replayType = ReplayEventType.Spawn; return true;
                case RenderEventType.Escape: replayType = ReplayEventType.Finish; return true;
                case RenderEventType.Pierce: replayType = ReplayEventType.Break; return true;
                default: replayType = default; return false;
            }
        }

        /// <summary><see cref="TryGetReplayType"/> that throws for kinds without a simulation counterpart.</summary>
        public static ReplayEventType ToReplayType(RenderEventType type)
        {
            if (TryGetReplayType(type, out ReplayEventType r)) return r;
            throw new ArgumentException("Render event kind " + type + " has no simulation event counterpart", nameof(type));
        }
    }

    /// <summary>
    /// A cosmetic event emitted by mode / simulation code for the renderer (spec §7.4 rule 7, §7.7 VFX). Particles,
    /// flashes and shakes are derived from these events analytically by age with a random stream seeded from
    /// (<see cref="Tick"/>, <see cref="Id"/>), so any frame can be evaluated without history and preview, scrubbing and
    /// export draw identical effects. Positions are world metres in the render stream's space (for Helix Drop: stream
    /// space, i.e. x along the vertical cut, y = height). See RenderEventContract.md for the per-type field usage.
    /// </summary>
    public struct RenderEvent : IEquatable<RenderEvent>
    {
        /// <summary>Simulation tick at which the event happened (effects start at this tick).</summary>
        public int Tick;
        public RenderEventType Type;
        /// <summary>Id unique among the events of the same tick (e.g. the event's index in the sorted event list). Seeds the effect.</summary>
        public int Id;
        /// <summary>World position, metres.</summary>
        public float X;
        public float Y;
        /// <summary>Unit direction of the effect (Hit: contact normal towards the ball; Break: optional outward); zero = none.</summary>
        public float NormalX;
        public float NormalY;
        /// <summary>Palette index of the effect colour (usually the ball's colour), see <see cref="PaletteSlots"/>.</summary>
        public byte ColorIndex;
        /// <summary>Strength in [0, 1] (Hit: impulse normalized by the mode; scales particle count, speed, squash).</summary>
        public float Intensity;
        /// <summary>Characteristic size, metres (Hit / Spawn / Grow: ball radius; Splat: blot radius; Finish / Escape: burst radius).</summary>
        public float Size;
        /// <summary>The ball the effect attaches to (Hit squash, Spawn / Grow pulse), or −1.</summary>
        public int BodyId;
        /// <summary>The body that was hit / receives a splat (Hit flash, Splat), or −1.</summary>
        public int TargetBodyId;
        /// <summary>Feature of the target: polyline segment index or ring arc index (−1 = unknown).</summary>
        public int Feature;
        /// <summary>Body-local angle of the contact on a ring / platform target, radians (gap convention).</summary>
        public float LocalAngle;
        /// <summary>Break: shape table id of the broken body (its geometry seeds the shards), or −1.</summary>
        public int ShapeId;
        /// <summary>Break: angle of the broken body at the tick, radians.</summary>
        public float Angle;
        /// <summary>Break: dequantized scale of the broken body at the tick.</summary>
        public float Scale;

        /// <summary>An event with every optional field set to "none".</summary>
        public static RenderEvent Create(int tick, RenderEventType type, int id, float x, float y, byte colorIndex, float intensity)
        {
            return new RenderEvent
            {
                Tick = tick,
                Type = type,
                Id = id,
                X = x,
                Y = y,
                ColorIndex = colorIndex,
                Intensity = intensity,
                BodyId = -1,
                TargetBodyId = -1,
                Feature = -1,
                ShapeId = -1,
                Scale = 1f,
            };
        }

        /// <summary>A ball (<paramref name="ballId"/>, radius <paramref name="ballRadius"/>) hits <paramref name="targetId"/> at (x, y).</summary>
        public static RenderEvent Hit(int tick, int id, float x, float y, float normalX, float normalY, byte colorIndex, float intensity,
            int ballId, float ballRadius, int targetId, int feature, float localAngle)
        {
            RenderEvent e = Create(tick, RenderEventType.Hit, id, x, y, colorIndex, intensity);
            e.NormalX = normalX;
            e.NormalY = normalY;
            e.BodyId = ballId;
            e.Size = ballRadius;
            e.TargetBodyId = targetId;
            e.Feature = feature;
            e.LocalAngle = localAngle;
            return e;
        }

        /// <summary>
        /// Ball <paramref name="ballId"/> at (x, y) grew to <paramref name="newRadius"/> (M02 «Рост»): radius pulse only,
        /// no particles.
        /// </summary>
        public static RenderEvent Grow(int tick, int id, int ballId, float x, float y, float newRadius, byte colorIndex)
        {
            RenderEvent e = Create(tick, RenderEventType.Grow, id, x, y, colorIndex, 1f);
            e.BodyId = ballId;
            e.Size = newRadius;
            return e;
        }

        /// <summary>A ball <paramref name="ballId"/> of radius <paramref name="radius"/> is born at (x, y) (M02 «Размножение»).</summary>
        public static RenderEvent Spawn(int tick, int id, int ballId, float x, float y, float radius, byte colorIndex)
        {
            RenderEvent e = Create(tick, RenderEventType.Spawn, id, x, y, colorIndex, 1f);
            e.BodyId = ballId;
            e.Size = radius;
            return e;
        }

        /// <summary>Body <paramref name="bodyId"/> (shape <paramref name="shapeId"/> at the given transform) breaks.</summary>
        public static RenderEvent Break(int tick, int id, int bodyId, int shapeId, float x, float y, float angle, float scale, byte colorIndex, float intensity)
        {
            RenderEvent e = Create(tick, RenderEventType.Break, id, x, y, colorIndex, intensity);
            e.TargetBodyId = bodyId;
            e.ShapeId = shapeId;
            e.Angle = angle;
            e.Scale = scale;
            return e;
        }

        public bool Equals(RenderEvent o)
        {
            return Tick == o.Tick && Type == o.Type && Id == o.Id && X.Equals(o.X) && Y.Equals(o.Y) && NormalX.Equals(o.NormalX)
                && NormalY.Equals(o.NormalY) && ColorIndex == o.ColorIndex && Intensity.Equals(o.Intensity) && Size.Equals(o.Size)
                && BodyId == o.BodyId && TargetBodyId == o.TargetBodyId && Feature == o.Feature && LocalAngle.Equals(o.LocalAngle)
                && ShapeId == o.ShapeId && Angle.Equals(o.Angle) && Scale.Equals(o.Scale);
        }

        public override bool Equals(object obj) => obj is RenderEvent o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Tick * 31 + (int)Type) * 31 + Id) * 31 + X.GetHashCode() * 17 + Y.GetHashCode();
            }
        }

        public override string ToString() => "RenderEvent@" + Tick + " " + Type + " #" + Id + " (" + X + ", " + Y + ")";
    }
}
