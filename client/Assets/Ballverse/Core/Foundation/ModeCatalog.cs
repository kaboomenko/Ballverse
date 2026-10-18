using System;
using System.Collections.Generic;

namespace Ballverse.Core
{
    /// <summary>Release catalog codes M01–M15 (spec §2.8). Numeric values are stable and may be persisted.</summary>
    public enum ModeCode
    {
        M01 = 1, M02 = 2, M03 = 3, M04 = 4, M05 = 5,
        M06 = 6, M07 = 7, M08 = 8, M09 = 9, M10 = 10,
        M11 = 11, M12 = 12, M13 = 13, M14 = 14, M15 = 15,
    }

    public sealed class ModeInfo
    {
        public ModeCode Code { get; }
        /// <summary>Stable string id used in files and analytics, e.g. "ring_escape".</summary>
        public string Id { get; }
        public string NameEn { get; }
        public string NameRu { get; }
        /// <summary>Free in the default catalog (owner decision Р17). Actual availability comes from config + entitlements.</summary>
        public bool FreeByDefault { get; }
        public bool Interactive { get; }
        /// <summary>Supports a winner and therefore the director's forced winner (spec §2.5).</summary>
        public bool HasWinner { get; }

        public ModeInfo(ModeCode code, string id, string nameEn, string nameRu, bool freeByDefault, bool interactive, bool hasWinner)
        {
            Code = code;
            Id = id;
            NameEn = nameEn;
            NameRu = nameRu;
            FreeByDefault = freeByDefault;
            Interactive = interactive;
            HasWinner = hasWinner;
        }
    }

    public static class ModeCatalog
    {
        static readonly ModeInfo[] s_all =
        {
            new ModeInfo(ModeCode.M01, "ring_escape",    "Ring Escape",       "Побег из колец",      true,  false, false),
            new ModeInfo(ModeCode.M02, "grow_bounce",    "Grow Bounce",       "Растущий шар",        true,  false, false),
            new ModeInfo(ModeCode.M03, "marble_race",    "Marble Race",       "Гонка шариков",       true,  false, true),
            new ModeInfo(ModeCode.M04, "peg_drop",       "Peg Drop",          "Пег-дроп",            true,  true,  false),
            new ModeInfo(ModeCode.M05, "spiral_drop",    "Spiral Drop",       "Спираль",             true,  true,  false),
            new ModeInfo(ModeCode.M06, "ball_battle",    "Ball Battle",       "Битва шаров",         false, false, true),
            new ModeInfo(ModeCode.M07, "last_standing",  "Last One Standing", "Последний выживший",  false, false, true),
            new ModeInfo(ModeCode.M08, "color_war",      "Color War",         "Война цветов",        false, false, true),
            new ModeInfo(ModeCode.M09, "multiply",       "Multiply Gates",    "Умножение",           false, false, true),
            new ModeInfo(ModeCode.M10, "maze_escape",    "Maze Escape",       "Лабиринт",            false, true,  true),
            new ModeInfo(ModeCode.M11, "smash",          "Smash",             "Разрушение",          false, true,  false),
            new ModeInfo(ModeCode.M12, "chain_reaction", "Chain Reaction",    "Цепная реакция",      false, true,  false),
            new ModeInfo(ModeCode.M13, "pendulum_wave",  "Pendulum Wave",     "Волна маятников",     false, false, false),
            new ModeInfo(ModeCode.M14, "orbit",          "Orbits",            "Орбиты",              false, false, true),
            new ModeInfo(ModeCode.M15, "liquid_sand",    "Liquid & Sand",     "Жидкость и песок",    false, false, false),
        };

        static readonly Dictionary<string, ModeInfo> s_byId = BuildIndex();

        public static IReadOnlyList<ModeInfo> All => s_all;

        public static ModeInfo Get(ModeCode code)
        {
            int i = (int)code - 1;
            if (i < 0 || i >= s_all.Length) throw new ArgumentOutOfRangeException(nameof(code));
            return s_all[i];
        }

        public static bool TryGetById(string id, out ModeInfo info)
        {
            if (id == null)
            {
                info = null;
                return false;
            }
            return s_byId.TryGetValue(id, out info);
        }

        public static bool TryParseCode(string code, out ModeCode result)
        {
            result = default;
            if (code == null || code.Length != 3 || code[0] != 'M') return false;
            if (!int.TryParse(code.Substring(1), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out int n)) return false;
            if (n < 1 || n > s_all.Length) return false;
            result = (ModeCode)n;
            return true;
        }

        static Dictionary<string, ModeInfo> BuildIndex()
        {
            var d = new Dictionary<string, ModeInfo>(StringComparer.Ordinal);
            foreach (var m in s_all) d.Add(m.Id, m);
            return d;
        }
    }
}
