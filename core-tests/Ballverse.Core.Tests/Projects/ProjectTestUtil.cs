using System;
using System.IO;
using System.Linq;
using Ballverse.Core.Projects;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Ballverse.Core.Tests.Projects
{
    static class ProjectTestUtil
    {
        public const string Now = "2026-10-18T12:00:00Z";
        public const string Later = "2026-10-18T12:10:00Z";

        public static string ReadData(string relative)
        {
            return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "Projects", relative));
        }

        public static ProjectDocument Golden()
        {
            return ProjectJson.Deserialize(ReadData("project_v1.json"));
        }

        public static ProjectDocument Doc(string id = "p1", string name = "Project")
        {
            var d = ProjectJson.Deserialize(ReadData("project_v1.json"));
            d.Id = id;
            d.Name = name;
            return d;
        }

        /// <summary>Semantic JSON equality: object key order ignored, all numbers compared as doubles, null and string kept distinct.</summary>
        public static bool JsonEquivalent(JToken a, JToken b, out string where, string path = "$")
        {
            where = null;
            if (IsNumber(a) && IsNumber(b))
            {
                if (a.Value<double>() == b.Value<double>()) return true;
                where = path + ": " + a + " != " + b;
                return false;
            }
            if (a.Type != b.Type)
            {
                where = path + ": type " + a.Type + " != " + b.Type;
                return false;
            }
            switch (a)
            {
                case JObject oa:
                    var ob = (JObject)b;
                    var ka = oa.Properties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
                    var kb = ob.Properties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
                    if (!ka.SequenceEqual(kb))
                    {
                        where = path + ": keys [" + string.Join(",", ka) + "] != [" + string.Join(",", kb) + "]";
                        return false;
                    }
                    foreach (var k in ka)
                    {
                        if (!JsonEquivalent(oa[k], ob[k], out where, path + "." + k)) return false;
                    }
                    return true;
                case JArray aa:
                    var ab = (JArray)b;
                    if (aa.Count != ab.Count)
                    {
                        where = path + ": length " + aa.Count + " != " + ab.Count;
                        return false;
                    }
                    for (int i = 0; i < aa.Count; i++)
                    {
                        if (!JsonEquivalent(aa[i], ab[i], out where, path + "[" + i + "]")) return false;
                    }
                    return true;
                default:
                    if (JToken.DeepEquals(a, b)) return true;
                    where = path + ": " + a + " != " + b;
                    return false;
            }
        }

        public static void AssertJsonEquivalent(JToken expected, JToken actual)
        {
            Assert.True(JsonEquivalent(expected, actual, out var where), where);
        }

        static bool IsNumber(JToken t)
        {
            return t.Type == JTokenType.Integer || t.Type == JTokenType.Float;
        }
    }
}
