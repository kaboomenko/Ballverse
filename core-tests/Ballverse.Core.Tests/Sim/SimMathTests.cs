using System;
using System.IO;
using System.Text.RegularExpressions;
using Ballverse.Core.Sim;
using Xunit;

namespace Ballverse.Core.Tests.Sim
{
    /// <summary>
    /// Own trigonometry (spec §6.5: "без System.Math.Sin/Cos в логике — собственные SimMath"): accuracy against double
    /// precision references, exact special values, and a static check that the solver sources call no libm trigonometry.
    /// </summary>
    public class SimMathTests
    {
        const double Tolerance = 5e-7;

        [Fact]
        public void SinCosMatchDoublePrecisionOverSolverRange()
        {
            double worst = 0;
            for (int i = -200000; i <= 200000; i++)
            {
                float x = i * (8f * SimMath.Pi / 200000f); // ±8π, ~1.3e-4 rad steps
                SimMath.SinCos(x, out float s, out float c);
                worst = Math.Max(worst, Math.Abs(s - Math.Sin(x)));
                worst = Math.Max(worst, Math.Abs(c - Math.Cos(x)));
                Assert.Equal(s, SimMath.Sin(x));
                Assert.Equal(c, SimMath.Cos(x));
            }
            Assert.True(worst <= Tolerance, "worst sin/cos error " + worst);
        }

        [Fact]
        public void SinCosSpecialValues()
        {
            Assert.Equal(0f, SimMath.Sin(0f));
            Assert.Equal(1f, SimMath.Cos(0f));
            Assert.True(float.IsNaN(SimMath.Sin(float.NaN)));
            Assert.True(float.IsNaN(SimMath.Cos(float.PositiveInfinity)));
            // Large arguments are wrapped first and stay finite and on the unit circle.
            SimMath.SinCos(123456.7f, out float s, out float c);
            Assert.InRange(s * s + c * c, 1f - 1e-5f, 1f + 1e-5f);
            Vec2 v = Vec2.FromAngle(SimMath.HalfPi);
            Assert.InRange(v.X, -1e-6f, 1e-6f);
            Assert.InRange(v.Y, 1f - 1e-6f, 1f);
        }

        [Fact]
        public void Atan2MatchesDoublePrecisionInAllQuadrants()
        {
            double worst = 0;
            for (int i = 0; i < 20000; i++)
            {
                double a = -Math.PI + 2.0 * Math.PI * i / 20000.0;
                foreach (float len in new[] { 1e-3f, 0.37f, 1f, 250f })
                {
                    float x = (float)(Math.Cos(a) * len), y = (float)(Math.Sin(a) * len);
                    double expected = Math.Atan2(y, x);
                    double err = Math.Abs(SimMath.Atan2(y, x) - expected);
                    // ±π are the same direction.
                    err = Math.Min(err, Math.Abs(err - 2.0 * Math.PI));
                    worst = Math.Max(worst, err);
                }
            }
            Assert.True(worst <= Tolerance, "worst atan2 error " + worst);
            Assert.Equal(0f, SimMath.Atan2(0f, 0f));
            Assert.Equal(SimMath.HalfPi, SimMath.Atan2(2f, 0f));
            Assert.Equal(-SimMath.HalfPi, SimMath.Atan2(-2f, 0f));
            Assert.Equal(SimMath.Pi, SimMath.Atan2(0f, -1f));
            Assert.True(float.IsNaN(SimMath.Atan2(float.NaN, 1f)));
            Assert.InRange(SimMath.Atan(float.PositiveInfinity), SimMath.HalfPi - 1e-6f, SimMath.HalfPi + 1e-6f);
        }

        [Fact]
        public void AsinMatchesDoublePrecision()
        {
            double worst = 0;
            for (int i = -100000; i <= 100000; i++)
            {
                float x = i / 100000f;
                worst = Math.Max(worst, Math.Abs(SimMath.Asin(x) - Math.Asin(x)));
            }
            Assert.True(worst <= Tolerance, "worst asin error " + worst);
            Assert.Equal(0f, SimMath.Asin(0f));
            Assert.True(float.IsNaN(SimMath.Asin(1.0001f)));
        }

        /// <summary>
        /// Static check (the banned-API analyzer list lives outside this module): no Sim source may call platform
        /// trigonometry — MathF/Math Sin, Cos, Tan, Asin, Acos, Atan, Atan2, Sinh, Cosh, Tanh, Exp, Log or Pow — because IL2CPP
        /// maps them to the OS libm. Sqrt, Abs, Floor and Round are IEEE-exact and allowed.
        /// </summary>
        [Fact]
        public void SimSourcesCallNoPlatformTrigonometry()
        {
            string dir = FindSimSourceDirectory();
            var banned = new Regex(@"\bMathF?\.(Sin|Cos|Tan|Asin|Acos|Atan2?|Sinh|Cosh|Tanh|Exp|Log|Log10|Pow|SinCos)\s*\(");
            int files = 0;
            foreach (string path in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                files++;
                string[] lines = File.ReadAllLines(path);
                for (int i = 0; i < lines.Length; i++)
                {
                    string code = lines[i];
                    int comment = code.IndexOf("//", StringComparison.Ordinal);
                    if (comment >= 0) code = code.Substring(0, comment);
                    Assert.False(banned.IsMatch(code), Path.GetFileName(path) + ":" + (i + 1) + " calls platform math: " + lines[i].Trim());
                }
            }
            Assert.True(files >= 10, "found only " + files + " Sim sources in " + dir);
        }

        static string FindSimSourceDirectory()
        {
            string d = AppContext.BaseDirectory;
            while (d != null)
            {
                string candidate = Path.Combine(d, "client", "Assets", "Ballverse", "Core", "Sim");
                if (Directory.Exists(candidate)) return candidate;
                d = Path.GetDirectoryName(d.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }
            throw new DirectoryNotFoundException("client/Assets/Ballverse/Core/Sim not found above " + AppContext.BaseDirectory);
        }
    }
}
