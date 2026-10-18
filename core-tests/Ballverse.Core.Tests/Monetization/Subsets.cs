using System.Collections.Generic;
using Ballverse.Core.Monetization;

namespace Ballverse.Core.Tests.Monetization
{
    /// <summary>Helpers for enumerating owned-product combinations (bit i = ProductIds.All[i]).</summary>
    static class Subsets
    {
        public const int Count = 1 << 15;

        public static List<string> Ids(int mask)
        {
            var list = new List<string>();
            for (int i = 0; i < ProductIds.All.Count; i++)
            {
                if ((mask & (1 << i)) != 0) list.Add(ProductIds.All[i]);
            }
            return list;
        }

        public static bool Has(int mask, string productId)
        {
            for (int i = 0; i < ProductIds.All.Count; i++)
            {
                if (ProductIds.All[i] == productId) return (mask & (1 << i)) != 0;
            }
            return false;
        }

        public static EntitlementSet Mode(params ModeCode[] modes)
        {
            var set = EntitlementSet.Empty;
            foreach (var m in modes)
            {
                EntitlementInfo.TryGetModeEntitlement(m, out var e);
                set = set.With(e);
            }
            return set;
        }
    }
}
