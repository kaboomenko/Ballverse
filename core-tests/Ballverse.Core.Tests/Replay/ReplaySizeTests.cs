using System.IO;
using Ballverse.Core;
using Ballverse.Core.Replay;
using Xunit;

namespace Ballverse.Core.Tests.Replay
{
    public class ReplaySizeTests
    {
        /// <summary>Spec §3.1: a replay without render stream weighs ≤ 200 KB per minute.</summary>
        [Fact]
        public void OneMinuteReplayFitsTheBudget()
        {
            var rng = Rng.FromSeed(60, 0);
            var ms = new MemoryStream();
            string paramsJson = "{\"ballCount\":300,\"speed\":1.0,\"theme\":\"neon\",\"soundPack\":\"Melody\",\"melody\":\"ode_to_joy\"}";
            var w = new ReplayWriter(ms, new ReplayHeader("ball_battle", 1, "1.0.0 (100)", PhysicsBackend.Box2DV3, rng.NextULong(), paramsJson));
            const uint duration = 60 * SimConstants.TickRate;
            uint tick = 0;
            for (int i = 0; i < 40 * 60; i++)
            {
                tick = (uint)(i * SimConstants.TickRate / 40);
                w.AddEvent(tick, (ReplayEventType)rng.NextInt(0, 8), rng.NextInt(0, 300),
                    ReplayQuantization.QuantizeImpulse(rng.NextFloat(0f, 50f)), rng.NextInt(-1000, 1000));
            }
            for (uint t = 0; t <= duration; t += SimConstants.HashIntervalTicks) w.AddHash(t, rng.NextULong());
            for (int i = 0; i < 20; i++)
            {
                w.AddInput((uint)(i * 180), 1, new int[]
                {
                    ReplayQuantization.QuantizeCoordinate(rng.NextFloat(-4.5f, 4.5f)),
                    ReplayQuantization.QuantizeCoordinate(rng.NextFloat(-8f, 8f)),
                    ReplayQuantization.QuantizeAngle(rng.NextFloat(0f, 6.28f)),
                    ReplayQuantization.QuantizeUnit(rng.NextFloat()),
                });
            }
            w.AddParamChange(600, "speed", "2.0");
            w.SetResult(new ReplayResult("red", 0, new[] { 120, 80, 40, 10 }, duration));
            w.Finish(duration);

            Assert.True(ms.Length <= 200 * 1024, "1-minute replay is " + ms.Length + " bytes");
            Assert.True(ms.Length < 40 * 1024, "expected compact encoding, got " + ms.Length + " bytes");
            Assert.Equal(2400, ReplayReader.Read(ms.ToArray()).Events.Count);
        }
    }
}
