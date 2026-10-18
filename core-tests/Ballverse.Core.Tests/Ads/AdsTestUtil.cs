using System;
using System.IO;
using Ballverse.Core.Ads;

namespace Ballverse.Core.Tests.Ads
{
    static class AdsTestUtil
    {
        /// <summary>2025-10-09T08:53:20Z, an arbitrary unix-ms origin.</summary>
        public const long T0 = 1_760_000_000_000L;
        public const long Sec = 1000L;
        public const long Min = 60_000L;

        public static string TestDataPath(string name)
        {
            return Path.Combine(AppContext.BaseDirectory, "TestData", "Ads", name);
        }

        public static GameRunSummary Game(int ticks = 600)
        {
            return GameRunSummary.FinishedRegular(ticks);
        }

        public static void CompleteGames(InterstitialPolicy p, int count)
        {
            for (int i = 0; i < count; i++)
            {
                if (!p.OnGameCompleted(Game())) throw new InvalidOperationException("game did not count");
            }
        }

        public static NaturalBreakContext Break(NaturalBreakKind kind, long nowMs, bool adReady = true)
        {
            return new NaturalBreakContext(kind, nowMs, adReady);
        }

        /// <summary>Simulates an app restart: state goes through JSON and a new policy instance.</summary>
        public static InterstitialPolicy Restart(InterstitialPolicy p)
        {
            return InterstitialPolicy.FromJson(p.Config, p.SerializeState());
        }

        public static RewardedExportGate Restart(RewardedExportGate g)
        {
            return RewardedExportGate.FromJson(g.Config, g.SerializeState());
        }
    }
}
