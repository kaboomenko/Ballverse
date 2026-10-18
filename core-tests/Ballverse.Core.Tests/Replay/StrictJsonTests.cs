using System;
using System.IO;
using Ballverse.Core.Replay;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Ballverse.Core.Tests.Replay
{
    public class StrictJsonTests
    {
        [Theory]
        [InlineData("0")]
        [InlineData("-0")]
        [InlineData("1.5")]
        [InlineData("-2.5e-3")]
        [InlineData("1E+10")]
        [InlineData("true")]
        [InlineData("false")]
        [InlineData("null")]
        [InlineData("\"\"")]
        [InlineData("\"a\\\"b\\\\c\\/d\\b\\f\\n\\r\\t\\u00e9\\uD83C\\uDFB2\"")]
        [InlineData("\"π 🎲\"")]
        [InlineData("[]")]
        [InlineData("{}")]
        [InlineData("[1,[2,{\"a\":null}],\"x\"]")]
        [InlineData("{\"x\":0,\"y\":-9.81,\"list\":[true,false]}")]
        public void AcceptsStrictCompactJson(string json)
        {
            Assert.Null(StrictJson.Validate(json, true));
            Assert.Null(StrictJson.Validate(json, false));
            JToken.Parse(json);
        }

        [Theory]
        [InlineData("")]
        [InlineData("{a:1}")]
        [InlineData("'x'")]
        [InlineData("{'a':1}")]
        [InlineData("NaN")]
        [InlineData("Infinity")]
        [InlineData("-Infinity")]
        [InlineData("undefined")]
        [InlineData("[1,]")]
        [InlineData("{\"a\":1,}")]
        [InlineData("[,1]")]
        [InlineData("01")]
        [InlineData("1.")]
        [InlineData(".5")]
        [InlineData("1e")]
        [InlineData("+1")]
        [InlineData("0x10")]
        [InlineData("tru")]
        [InlineData("True")]
        [InlineData("nul")]
        [InlineData("1 2")]
        [InlineData("{} {}")]
        [InlineData("/*c*/1")]
        [InlineData("1//c")]
        [InlineData("\"abc")]
        [InlineData("\"a\\x\"")]
        [InlineData("\"\\u12G4\"")]
        [InlineData("\"\\u12\"")]
        [InlineData("\"tab\there\"")]
        [InlineData("[1")]
        [InlineData("{\"a\"}")]
        [InlineData("{\"a\":}")]
        [InlineData("{\"a\" 1}")]
        [InlineData("[1;2]")]
        public void RejectsNonStrictJson(string json)
        {
            Assert.NotNull(StrictJson.Validate(json, false));
            Assert.NotNull(StrictJson.Validate(json, true));
        }

        [Theory]
        [InlineData("[1, 2]")]
        [InlineData(" 1")]
        [InlineData("1 ")]
        [InlineData("{\"a\": 1}")]
        [InlineData("{\n\"a\":1\n}")]
        [InlineData("[\r\n1\t]")]
        public void CompactModeRejectsWhitespaceOutsideStrings(string json)
        {
            Assert.Null(StrictJson.Validate(json, false));
            Assert.NotNull(StrictJson.Validate(json, true));
        }

        [Fact]
        public void WhitespaceInsideStringsIsCompact()
        {
            Assert.Null(StrictJson.Validate("\"a b\"", true));
            Assert.Null(StrictJson.Validate("{\"k k\":\" \"}", true));
        }

        [Fact]
        public void NonJsonWhitespaceIsRejected()
        {
            Assert.NotNull(StrictJson.Validate("\u00A01", false));
            Assert.NotNull(StrictJson.Validate("1\f", false));
        }

        [Fact]
        public void NullIsRejected()
        {
            Assert.NotNull(StrictJson.Validate(null, false));
        }

        [Fact]
        public void NestingIsLimited()
        {
            string ok = new string('[', StrictJson.MaxDepth) + new string(']', StrictJson.MaxDepth);
            Assert.Null(StrictJson.Validate(ok, true));
            JToken.Parse(ok);
            string deep = new string('[', StrictJson.MaxDepth + 1) + new string(']', StrictJson.MaxDepth + 1);
            Assert.NotNull(StrictJson.Validate(deep, true));
            Assert.NotNull(StrictJson.Validate(new string('[', 200000), true));
            Assert.NotNull(StrictJson.Validate(new string('{', 1) + new string('[', 200000), true));
        }

        [Theory]
        [InlineData("{a:1}")]
        [InlineData("'x'")]
        [InlineData("NaN")]
        [InlineData("undefined")]
        [InlineData("[1,]")]
        [InlineData("Infinity")]
        [InlineData("[1, 2]")]
        public void WriterRejectsNonStandardParmValues(string value)
        {
            var w = new ReplayWriter(new MemoryStream(), ReplayTestUtil.Header());
            Assert.Throws<ArgumentException>(() => w.AddParamChange(0, "p", value));
        }

        [Theory]
        [InlineData("{a:1}")]
        [InlineData("{\"a\":NaN}")]
        [InlineData("[1,]")]
        [InlineData("")]
        public void WriterRejectsNonStandardParamsJson(string paramsJson)
        {
            var header = new ReplayHeader("m", 1, "", PhysicsBackend.None, 0, paramsJson);
            Assert.Throws<ArgumentException>(() => new ReplayWriter(new MemoryStream(), header));
        }

        [Fact]
        public void WriterAcceptsIndentedParamsJson()
        {
            var header = new ReplayHeader("m", 1, "", PhysicsBackend.None, 0, CoreJson.Serialize(new { a = 1, b = new[] { 2, 3 } }));
            Assert.Contains("\n", header.ParamsJson);
            var ms = new MemoryStream();
            new ReplayWriter(ms, header).Finish(0);
            Assert.Equal(header.ParamsJson, ReplayReader.Read(ms.ToArray()).Header.ParamsJson);
        }

        [Fact]
        public void CoreJsonCompactOutputIsAccepted()
        {
            var w = new ReplayWriter(new MemoryStream(), ReplayTestUtil.Header());
            w.AddParamValue(0, "nan", double.NaN);
            w.AddParamValue(1, "inf", double.PositiveInfinity);
            w.AddParamValue(2, "obj", new { x = 1.5, s = "a \"q\"" });
        }
    }
}
