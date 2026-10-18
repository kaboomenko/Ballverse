using System.Text;
using Ballverse.Core;
using NUnit.Framework;

namespace Ballverse.Tests
{
    /// <summary>
    /// Ballverse.Core compiles and runs inside Unity with the same results as in `dotnet test`
    /// (reference vectors are the ones core-tests use), and Newtonsoft.Json from the Unity package is wired up.
    /// </summary>
    public sealed class CoreSmokeTests
    {
        [Test]
        public void Pcg32MatchesReferenceVectors()
        {
            var rng = new Rng(42UL, 54UL);
            uint[] expected = { 0xa15c02b7, 0x7b47f409, 0xba1d3330, 0x83d2f293, 0xbfa4784b, 0xcbed606e };
            foreach (uint e in expected) Assert.AreEqual(e, rng.NextUInt());
        }

        [Test]
        public void XxHash64MatchesReferenceVectors()
        {
            Assert.AreEqual(0xef46db3751d8e999UL, XxHash64.Hash(Encoding.ASCII.GetBytes(""), 0UL));
            Assert.AreEqual(0x313a82ac407f7178UL, XxHash64.Hash(Encoding.ASCII.GetBytes("Ballverse"), 0UL));
            Assert.AreEqual(0x2b55c01267545641UL, XxHash64.Hash(Encoding.ASCII.GetBytes("Ballverse"), 12345UL));
        }

        [Test]
        public void ModeCatalogHasRingEscape()
        {
            Assert.IsTrue(ModeCatalog.TryGetById("ring_escape", out ModeInfo info));
            Assert.AreEqual(ModeCode.M01, info.Code);
            Assert.AreSame(info, ModeCatalog.Get(ModeCode.M01));
        }

        public sealed class JsonProbe
        {
            public string DisplayName { get; set; }
            public ModeCode Mode { get; set; }
            public double Speed { get; set; }
        }

        [Test]
        public void CoreJsonRoundTripsWithCamelCaseAndStringEnums()
        {
            var probe = new JsonProbe { DisplayName = "Ring", Mode = ModeCode.M01, Speed = 1.5 };
            string json = CoreJson.Serialize(probe, indented: false);
            Assert.AreEqual("{\"displayName\":\"Ring\",\"mode\":\"M01\",\"speed\":1.5}", json);

            JsonProbe back = CoreJson.Deserialize<JsonProbe>(json);
            Assert.AreEqual("Ring", back.DisplayName);
            Assert.AreEqual(ModeCode.M01, back.Mode);
            Assert.AreEqual(1.5, back.Speed);
        }
    }
}
