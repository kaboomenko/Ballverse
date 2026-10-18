using System;

namespace Ballverse.Core.Sim
{
    public sealed partial class PhysicsWorld
    {
        /// <summary>Fixed-point scale of hashed floats: 2^16 units per metre / radian / (m/s) — same scheme as the replay HASH record.</summary>
        public const int HashUnitsPerUnit = 65536;

        readonly XxHash64 _hasher = new XxHash64();

        /// <summary>
        /// XXH64 of the quantized world state (spec §7.4 HASH): tick, every ball (alive flag; position, velocity, angle, spin,
        /// radius), every body (enabled; pose, velocities, scale, spin), collider enabled bits, in id order, and the spin-platform
        /// transits (sorted pairs). No allocations.
        /// </summary>
        public ulong ComputeStateHash()
        {
            _hasher.Reset();
            AppendStateHash(_hasher);
            return _hasher.Digest();
        }

        /// <summary>Feeds the quantized state into an external hash (e.g. combined with mode rule state by the runner).</summary>
        public void AppendStateHash(XxHash64 hash)
        {
            if (hash == null) throw new ArgumentNullException(nameof(hash));
            hash.AppendInt(_tick);
            hash.AppendInt(_ballCount);
            for (int i = 0; i < _ballCount; i++)
            {
                ref BallState b = ref _balls[i];
                hash.AppendInt(b.Alive ? 1 : 0);
                if (!b.Alive) continue;
                AppendQ(hash, b.Position.X);
                AppendQ(hash, b.Position.Y);
                AppendQ(hash, b.Velocity.X);
                AppendQ(hash, b.Velocity.Y);
                AppendQ(hash, b.Angle);
                AppendQ(hash, b.AngularVelocity);
                AppendQ(hash, b.Radius);
            }
            hash.AppendInt(_bodyCount);
            for (int i = 0; i < _bodyCount; i++)
            {
                ref PhysicsBodyState b = ref _bodies[i];
                hash.AppendInt(b.Enabled ? 1 : 0);
                AppendQ(hash, b.Position.X);
                AppendQ(hash, b.Position.Y);
                AppendQ(hash, b.Angle);
                AppendQ(hash, b.LinearVelocity.X);
                AppendQ(hash, b.LinearVelocity.Y);
                AppendQ(hash, b.AngularVelocity);
                AppendQ(hash, b.Scale);
                AppendQ(hash, b.Spin);
                AppendQ(hash, b.SpinVelocity);
            }
            hash.AppendInt(_colliderCount);
            int bits = 0;
            for (int i = 0; i < _colliderCount; i++)
            {
                if (_colliders[i].Enabled) bits |= 1 << (i & 31);
                if ((i & 31) == 31 || i == _colliderCount - 1)
                {
                    hash.AppendInt(bits);
                    bits = 0;
                }
            }
            hash.AppendInt(_transitCount);
            for (int i = 0; i < _transitCount; i++)
            {
                hash.AppendInt(_transits[i].Ball);
                hash.AppendInt(_transits[i].Collider);
            }
        }

        /// <summary>
        /// <c>round(value · 2^16)</c> half away from zero, saturating; NaN hashes as a sentinel. Must stay bit-identical to
        /// <c>Ballverse.Core.Replay.StateHashBuilder.AddQuantized</c> (on main) so world hashes match the replay HASH record;
        /// once both modules are merged the quantizer belongs in Foundation, shared by both (QuantizeForHashRoundsHalfAwayAndSaturates
        /// pins the scheme here).
        /// </summary>
        public static long QuantizeForHash(float value)
        {
            if (float.IsNaN(value)) return long.MinValue;
            double scaled = Math.Round((double)value * HashUnitsPerUnit, MidpointRounding.AwayFromZero);
            if (scaled >= 9.2e18) return long.MaxValue;
            if (scaled <= -9.2e18) return long.MinValue + 1;
            return (long)scaled;
        }

        static void AppendQ(XxHash64 hash, float value)
        {
            hash.AppendULong((ulong)QuantizeForHash(value));
        }

        /// <summary>Copies render-relevant poses into <paramref name="snapshot"/> (its arrays grow once, then are reused).</summary>
        public void WriteSnapshot(SimSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            snapshot.Tick = _tick;
            snapshot.EnsureCapacity(_ballCount, _bodyCount, _colliderCount);
            snapshot.BallCount = _ballCount;
            for (int i = 0; i < _ballCount; i++)
            {
                ref BallState b = ref _balls[i];
                snapshot.Balls[i] = new BallPose
                {
                    Position = b.Position,
                    Velocity = b.Velocity,
                    Angle = b.Angle,
                    Radius = b.Radius,
                    Tag = b.Tag,
                    Alive = b.Alive,
                };
            }
            snapshot.BodyCount = _bodyCount;
            for (int i = 0; i < _bodyCount; i++)
            {
                ref PhysicsBodyState b = ref _bodies[i];
                snapshot.Bodies[i] = new BodyPose
                {
                    Position = b.Position,
                    Angle = b.Angle,
                    Scale = b.Scale,
                    Spin = b.Spin,
                    Tag = b.Tag,
                    Enabled = b.Enabled,
                };
            }
            snapshot.ColliderCount = _colliderCount;
            for (int i = 0; i < _colliderCount; i++)
            {
                snapshot.ColliderEnabled[i] = _colliders[i].Enabled && _bodies[_colliders[i].Body].Enabled;
            }
            snapshot.EnsureTransitCapacity(_transits.Length);
            snapshot.TransitCount = _transitCount;
            Array.Copy(_transits, snapshot.Transits, _transitCount);
        }
    }
}
