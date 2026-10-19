using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.UI.Localization;

namespace Ballverse.UI.Tests
{
    sealed class DictTables : Localizer.ITableSource
    {
        readonly Dictionary<string, string> _json;

        public DictTables(Dictionary<string, string> json)
        {
            _json = json;
        }

        public string LoadTableJson(string language)
        {
            return _json.TryGetValue(language, out var s) ? s : null;
        }
    }

    /// <summary>Reads the shipped tables from Resources (paths relative to this project).</summary>
    sealed class FileTables : Localizer.ITableSource
    {
        public string LoadTableJson(string language)
        {
            string path = Path.Combine(Paths.UiRoot, "Resources", "BallverseUI", "Localization", language + ".json");
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
    }

    static class Paths
    {
        /// <summary>client/Assets/Ballverse/UI, found by walking up from the test binary.</summary>
        public static string UiRoot
        {
            get
            {
                var dir = new DirectoryInfo(AppContext.BaseDirectory);
                while (dir != null)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "Ballverse.UI.asmdef"))) return dir.FullName;
                    dir = dir.Parent;
                }
                throw new InvalidOperationException("Ballverse.UI.asmdef not found above " + AppContext.BaseDirectory);
            }
        }
    }
}
