using System.Collections.Generic;
using Ballverse.Core.Ads;
using Xunit;
using static Ballverse.Core.Tests.Ads.AdsTestUtil;

namespace Ballverse.Core.Tests.Ads
{
    /// <summary>Rng-driven event sequences (deterministic seeds) checking the invariants of spec §5.3.1–§5.3.2.</summary>
    public class AdPolicyPropertyTests
    {
        const int Seeds = 60;
        const int Steps = 1500;

        static bool Chance(ref Rng rng, int percent)
        {
            return rng.NextInt(0, 100) < percent;
        }

        [Fact]
        public void InterstitialInvariantsHoldForRandomSequences()
        {
            int totalShows = 0;
            var suppressSeen = new HashSet<InterstitialSuppressReason>();
            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                var rng = Rng.FromSeed(seed, 0xAD5);
                var cfg = new AdPolicyConfig
                {
                    InterstitialEveryNGames = rng.NextInt(1, 6),
                    InterstitialMinIntervalSec = rng.NextInt(0, 121),
                    InterstitialGraceGames = rng.NextInt(0, 5),
                    InterstitialQuietAfterRewardedSec = rng.NextInt(0, 61),
                };
                var p = new InterstitialPolicy(cfg);
                long now = T0 + rng.NextInt(0, 1_000_000);
                long? lastShow = null;
                long? lastRewarded = null;
                int refCounter = 0;
                long refTotal = 0;

                for (int step = 0; step < Steps; step++)
                {
                    now += Chance(ref rng, 10) ? 0 : rng.NextInt(0, 45_000);
                    int ev = rng.NextInt(0, 100);
                    if (ev < 35)
                    {
                        var kind = (GameRunKind)rng.NextInt(0, 4);
                        var end = (GameRunEnd)rng.NextInt(0, 3);
                        int ticks = Chance(ref rng, 20) ? rng.NextInt(290, 310) : rng.NextInt(0, 6000);
                        var run = new GameRunSummary(kind, end, ticks);
                        bool counted = p.OnGameCompleted(run);
                        Assert.Equal(GameCompletedRule.IsGameCompleted(run), counted);
                        if (counted)
                        {
                            refTotal++;
                            if (refTotal > cfg.InterstitialGraceGames) refCounter++;
                        }
                    }
                    else if (ev < 85)
                    {
                        var ctx = new NaturalBreakContext((NaturalBreakKind)rng.NextInt(0, 5), now, Chance(ref rng, 80))
                        {
                            InStudio = Chance(ref rng, 10),
                            Exporting = Chance(ref rng, 10),
                            InPurchaseFlow = Chance(ref rng, 10),
                            HasNoInterstitialsEntitlement = Chance(ref rng, 5),
                        };
                        bool pendingBefore = refCounter >= cfg.InterstitialEveryNGames && refTotal > cfg.InterstitialGraceGames;
                        var d = p.OnNaturalBreak(ctx);
                        if (d.Kind == InterstitialDecisionKind.Suppressed) suppressSeen.Add(d.Reason);

                        if (ctx.HasNoInterstitialsEntitlement)
                        {
                            Assert.Equal(InterstitialSuppressReason.Entitlement, d.Reason);
                            refCounter = 0;
                        }
                        else if (!pendingBefore)
                        {
                            Assert.Equal(InterstitialDecisionKind.None, d.Kind);
                        }
                        else if (d.Reason == InterstitialSuppressReason.AdNotReady)
                        {
                            Assert.False(ctx.AdReady);
                            refCounter = 0;
                        }

                        if (d.IsShow)
                        {
                            totalShows++;
                            Assert.True(pendingBefore);
                            Assert.True(ctx.AdReady);
                            Assert.False(ctx.HasNoInterstitialsEntitlement);
                            Assert.False(ctx.InStudio);
                            Assert.False(ctx.Exporting);
                            Assert.False(ctx.InPurchaseFlow);
                            Assert.NotEqual(NaturalBreakKind.ToVideo, ctx.Kind);
                            if (lastShow.HasValue) Assert.True(now - lastShow.Value >= cfg.InterstitialMinIntervalSec * 1000L);
                            if (lastRewarded.HasValue) Assert.True(now - lastRewarded.Value >= cfg.InterstitialQuietAfterRewardedSec * 1000L);

                            if (Chance(ref rng, 90))
                            {
                                p.OnInterstitialShown(now);
                                lastShow = now;
                            }
                            else
                            {
                                p.OnInterstitialNotShown();
                            }
                            refCounter = 0;
                        }
                        else if (pendingBefore && d.Kind == InterstitialDecisionKind.Suppressed
                                 && d.Reason != InterstitialSuppressReason.AdNotReady
                                 && d.Reason != InterstitialSuppressReason.Entitlement)
                        {
                            Assert.True(p.IsPending); // IN3/IN5 keep it pending
                        }
                    }
                    else if (ev < 93)
                    {
                        p.OnRewardedShown(now);
                        lastRewarded = now;
                    }
                    else
                    {
                        p = Restart(p); // new session
                    }

                    Assert.True(p.CompletedGames >= 0);
                    Assert.Equal(refCounter, p.CompletedGames);
                    Assert.Equal(refTotal, p.TotalCompletedGames);
                }
            }
            Assert.True(totalShows > 100);
            // Every suppression reason was exercised.
            Assert.Equal(8, suppressSeen.Count);
        }

        [Fact]
        public void InterstitialClockJumpsDelayAtMostOneInterval()
        {
            for (ulong seed = 1; seed <= 20; seed++)
            {
                var rng = Rng.FromSeed(seed, 0xC10C);
                var p = new InterstitialPolicy(new AdPolicyConfig { InterstitialEveryNGames = 1 });
                long now = T0;
                long? modelLast = null;      // stored timestamp as the spec'd rollback rule should keep it
                long realSinceShow = long.MaxValue;
                bool jumpedSinceShow = false;
                for (int step = 0; step < 1000; step++)
                {
                    long real = rng.NextInt(0, 30_000);
                    if (realSinceShow != long.MaxValue) realSinceShow += real;
                    // The device clock usually follows real time but sometimes jumps either way.
                    if (Chance(ref rng, 3))
                    {
                        now = System.Math.Max(0, now + rng.NextInt(-3_600_000, 3_600_000));
                        jumpedSinceShow = true;
                    }
                    else
                    {
                        now += real;
                    }
                    p.OnGameCompleted(Game());
                    if (modelLast.HasValue && modelLast.Value > now) modelLast = now;
                    var d = p.OnNaturalBreak(Break(NaturalBreakKind.Retry, now));
                    Assert.Equal(modelLast, p.LastInterstitialShownMs);
                    bool expectShow = !modelLast.HasValue || now - modelLast.Value >= 60_000;
                    Assert.Equal(expectShow, d.IsShow);
                    // Without clock jumps, real elapsed time between shows is at least the interval.
                    if (d.IsShow && !jumpedSinceShow && realSinceShow != long.MaxValue) Assert.True(realSinceShow >= 60_000);
                    if (d.IsShow)
                    {
                        p.OnInterstitialShown(now);
                        modelLast = now;
                        realSinceShow = 0;
                        jumpedSinceShow = false;
                    }
                }
            }
        }

        [Fact]
        public void GateInvariantsHoldForRandomSequences()
        {
            string[] projects = { "p0", "p1", "p2", "p3" };
            int fundedSuccesses = 0;
            for (ulong seed = 1; seed <= Seeds; seed++)
            {
                var rng = Rng.FromSeed(seed, 0x6A7E);
                var cfg = new AdPolicyConfig
                {
                    RewardedExportEnabled = !Chance(ref rng, 10),
                    RewardedRetryWindowMin = Chance(ref rng, 15) ? 0 : rng.NextInt(1, 16),
                };
                long window = cfg.RewardedRetryWindowMin * 60_000L;
                var g = new RewardedExportGate(cfg);
                long now = T0;

                var rewards = new Dictionary<string, int>();
                var funded = new Dictionary<string, int>();
                // Reference model of each project's credit: absent / held (null) / timed (expiry).
                var model = new Dictionary<string, long?>();
                foreach (var pr in projects)
                {
                    rewards[pr] = 0;
                    funded[pr] = 0;
                }

                for (int step = 0; step < Steps / 3; step++)
                {
                    now += rng.NextInt(0, 5 * 60_000);
                    string pid = projects[rng.NextInt(0, projects.Length)];
                    bool ent = Chance(ref rng, 10);
                    PruneModel(model, now);

                    var d = g.Decide(pid, now, ent);
                    bool exportStarts;
                    bool fundedByCredit = false;
                    if (ent)
                    {
                        Assert.Equal(ExportWithoutAdReason.Entitlement, d.Reason);
                        exportStarts = true;
                    }
                    else if (!cfg.RewardedExportEnabled)
                    {
                        Assert.Equal(ExportWithoutAdReason.GateDisabled, d.Reason);
                        exportStarts = true;
                    }
                    else if (model.TryGetValue(pid, out var exp))
                    {
                        // Credits are found only for the project that earned them, within the window.
                        Assert.True(d.Reason == ExportWithoutAdReason.RetryCredit || d.Reason == ExportWithoutAdReason.EarnedCredit);
                        Assert.True(rewards[pid] > funded[pid]);
                        model[pid] = null;
                        exportStarts = true;
                        fundedByCredit = true;
                    }
                    else
                    {
                        Assert.True(d.IsShowOffer);
                        exportStarts = false;
                        int attempts = 0;
                        while (true)
                        {
                            var outcome = (RewardedOfferOutcome)rng.NextInt(0, 7);
                            var stepRes = g.OnOfferOutcome(pid, outcome, now);
                            if (stepRes.Kind == RewardedGateStepKind.ShowOfferAgain && attempts++ < 3)
                            {
                                now += rng.NextInt(1000, 20_000);
                                continue;
                            }
                            if (stepRes.Kind == RewardedGateStepKind.ShowOfferAgain)
                            {
                                g.OnOfferOutcome(pid, RewardedOfferOutcome.Cancelled, now);
                                break;
                            }
                            if (stepRes.Kind == RewardedGateStepKind.StartExport)
                            {
                                exportStarts = true;
                                if (stepRes.Funding == ExportFunding.RewardEarned)
                                {
                                    rewards[pid]++;
                                    model[pid] = null;
                                    fundedByCredit = true;
                                }
                            }
                            break;
                        }
                    }

                    if (exportStarts)
                    {
                        now += rng.NextInt(1000, 15 * 60_000);
                        PruneModel(model, now);
                        var result = (ExportResult)rng.NextInt(0, 3);
                        var fin = g.OnExportFinished(pid, result, now);
                        if (result == ExportResult.Succeeded)
                        {
                            if (fundedByCredit)
                            {
                                funded[pid]++;
                                fundedSuccesses++;
                                Assert.Equal(ExportFinishKind.CreditConsumed, fin.Kind);
                            }
                            model.Remove(pid);
                        }
                        else if (fundedByCredit)
                        {
                            if (window == 0)
                            {
                                Assert.Equal(ExportFinishKind.CreditBurned, fin.Kind);
                                model.Remove(pid);
                            }
                            else
                            {
                                Assert.Equal(ExportFinishKind.RetryCreditGranted, fin.Kind);
                                Assert.Equal(now + window, fin.RetryExpiresAtMs);
                                model[pid] = now + window;
                            }
                        }
                        else
                        {
                            Assert.NotEqual(ExportFinishKind.RetryCreditGranted, fin.Kind);
                        }
                    }

                    // One view = at most one successful ad-funded export, per project (RW5), never transferred.
                    foreach (var pr in projects) Assert.True(funded[pr] <= rewards[pr]);
                    PruneModel(model, now);
                    foreach (var pr in projects)
                    {
                        bool has = g.TryGetCredit(pr, now, out _);
                        Assert.Equal(model.ContainsKey(pr), has);
                    }

                    if (Chance(ref rng, 5)) g = Restart(g);
                }
            }
            Assert.True(fundedSuccesses > 50);
        }

        static void PruneModel(Dictionary<string, long?> model, long now)
        {
            var expired = new List<string>();
            foreach (var kv in model)
            {
                if (kv.Value.HasValue && now > kv.Value.Value) expired.Add(kv.Key);
            }
            foreach (var k in expired) model.Remove(k);
        }

        [Fact]
        public void SameSeedGivesSameSerializedState()
        {
            Assert.Equal(RunScript(42), RunScript(42));
            Assert.NotEqual(RunScript(42), RunScript(43));
        }

        static string RunScript(ulong seed)
        {
            var rng = Rng.FromSeed(seed, 1);
            var p = new InterstitialPolicy(AdPolicyConfig.CreateDefault());
            long now = T0;
            for (int i = 0; i < 200; i++)
            {
                now += rng.NextInt(0, 60_000);
                p.OnGameCompleted(GameRunSummary.FinishedRegular(rng.NextInt(0, 1000)));
                if (p.OnNaturalBreak(Break((NaturalBreakKind)rng.NextInt(0, 5), now, Chance(ref rng, 70))).IsShow) p.OnInterstitialShown(now);
            }
            return p.SerializeState();
        }
    }
}
