using System;
using System.Collections.Generic;
using System.Text;

namespace Ballverse.Core.Monetization
{
    /// <summary>
    /// Immutable set of <see cref="Entitlement"/> values (spec §5.1), stored as a 64-bit mask.
    /// Enumeration is always in ascending enum order, so it never depends on construction order.
    /// </summary>
    public readonly struct EntitlementSet : IEquatable<EntitlementSet>
    {
        readonly ulong _mask;

        EntitlementSet(ulong mask)
        {
            _mask = mask;
        }

        public static readonly EntitlementSet Empty = default;

        /// <summary>Every defined entitlement: what a user with nothing left to buy has (spec §5.2 "Всё куплено").</summary>
        public static readonly EntitlementSet AllDefined = Of(EntitlementInfo.All);

        /// <summary>MODE_M06…MODE_M15: the paid modes of the 1.0 catalog (spec §5.1).</summary>
        public static readonly EntitlementSet PaidModes10 = Of(
            Entitlement.ModeM06, Entitlement.ModeM07, Entitlement.ModeM08, Entitlement.ModeM09, Entitlement.ModeM10,
            Entitlement.ModeM11, Entitlement.ModeM12, Entitlement.ModeM13, Entitlement.ModeM14, Entitlement.ModeM15);

        /// <summary>NO_EXPORT_ADS + NO_INTERSTITIALS: the No Ads Bundle contents (spec §5.1).</summary>
        public static readonly EntitlementSet AdFree = Of(Entitlement.NoExportAds, Entitlement.NoInterstitials);

        /// <summary>Raw bit mask (bit index = enum value). Useful for fast comparisons and tests.</summary>
        public ulong Mask => _mask;

        public bool IsEmpty => _mask == 0UL;

        public int Count
        {
            get
            {
                int n = 0;
                ulong m = _mask;
                while (m != 0UL)
                {
                    m &= m - 1UL;
                    n++;
                }
                return n;
            }
        }

        /// <summary>Number of MODE_* entitlements in the set (spec §5.2 "У вас уже N режимов").</summary>
        public int ModeCount => Intersect(PaidModes10).Count;

        public static EntitlementSet Of(params Entitlement[] entitlements)
        {
            return Of((IEnumerable<Entitlement>)entitlements);
        }

        public static EntitlementSet Of(IEnumerable<Entitlement> entitlements)
        {
            if (entitlements == null) throw new ArgumentNullException(nameof(entitlements));
            ulong mask = 0UL;
            foreach (var e in entitlements) mask |= Bit(e);
            return new EntitlementSet(mask);
        }

        public bool Contains(Entitlement e)
        {
            return (_mask & Bit(e)) != 0UL;
        }

        /// <summary>True when the set holds the MODE_* entitlement of <paramref name="mode"/>; false for modes without one.</summary>
        public bool ContainsMode(ModeCode mode)
        {
            return EntitlementInfo.TryGetModeEntitlement(mode, out var e) && Contains(e);
        }

        public EntitlementSet With(Entitlement e)
        {
            return new EntitlementSet(_mask | Bit(e));
        }

        public EntitlementSet Without(Entitlement e)
        {
            return new EntitlementSet(_mask & ~Bit(e));
        }

        public EntitlementSet Union(EntitlementSet other)
        {
            return new EntitlementSet(_mask | other._mask);
        }

        public EntitlementSet Intersect(EntitlementSet other)
        {
            return new EntitlementSet(_mask & other._mask);
        }

        public EntitlementSet Except(EntitlementSet other)
        {
            return new EntitlementSet(_mask & ~other._mask);
        }

        public bool IsSupersetOf(EntitlementSet other)
        {
            return (other._mask & ~_mask) == 0UL;
        }

        public bool IsSubsetOf(EntitlementSet other)
        {
            return other.IsSupersetOf(this);
        }

        /// <summary>Members in ascending enum order.</summary>
        public Entitlement[] ToArray()
        {
            var list = new List<Entitlement>();
            foreach (var e in EntitlementInfo.All)
            {
                if (Contains(e)) list.Add(e);
            }
            return list.ToArray();
        }

        public bool Equals(EntitlementSet other)
        {
            return _mask == other._mask;
        }

        public override bool Equals(object obj)
        {
            return obj is EntitlementSet other && Equals(other);
        }

        public override int GetHashCode()
        {
            return _mask.GetHashCode();
        }

        public static bool operator ==(EntitlementSet a, EntitlementSet b)
        {
            return a._mask == b._mask;
        }

        public static bool operator !=(EntitlementSet a, EntitlementSet b)
        {
            return a._mask != b._mask;
        }

        /// <summary>Spec names joined with ", ", e.g. "MODE_M06, NO_WATERMARK"; "{}" when empty.</summary>
        public override string ToString()
        {
            if (IsEmpty) return "{}";
            var sb = new StringBuilder();
            foreach (var e in ToArray())
            {
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(EntitlementInfo.SpecName(e));
            }
            return sb.ToString();
        }

        static ulong Bit(Entitlement e)
        {
            if (!EntitlementInfo.IsDefined(e)) throw new ArgumentOutOfRangeException(nameof(e), "Unknown entitlement " + (int)e);
            return 1UL << (int)e;
        }
    }
}
