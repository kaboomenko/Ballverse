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
        /// <summary>
        /// Free in the default catalog (owner decisions Р17, Р18). Always a subset of the released modes: in release 1 exactly
        /// {M01, M02, M05}. Actual availability comes from config + entitlements. A mode never stops being free by default in a
        /// later build (spec §5.1: the free set only grows).
        /// </summary>
        public bool FreeByDefault { get; }
        /// <summary>
        /// Part of this build's release (owner decision Р18): the mode's code ships and it may be offered anywhere in the app.
        /// Unreleased modes stay in the catalog (codes and ids are stable and reserved) but must not appear as available:
        /// configuration lists ignore them and the gallery never shows them. A later release ships a mode by setting this flag
        /// in <see cref="ModeCatalog"/>; Remote Config cannot enable a mode that is not in the build.
        /// </summary>
        public bool InRelease { get; }
        /// <summary>Has a player-controlled (interactive) variant whose input is recorded in the replay (spec §2.6).</summary>
        public bool Interactive { get; }
        /// <summary>
        /// Can run without a player, so the director can select the duration (spec §2.4): every non-interactive mode, and an
        /// interactive mode with an automatic control (M05 "Авто-ролик" and "Бот", spec §2.9). False for modes that are
        /// interactive only.
        /// </summary>
        public bool HasAutoControl { get; }
        /// <summary>
        /// Supports a winner (in some variant) and therefore the director's forced winner (spec §2.5). Release 1: M01
        /// "Соревнование" and M02 "Открытая грань"; whether the current parameters produce a winner is the mode's concern.
        /// </summary>
        public bool HasWinner { get; }

        public ModeInfo(ModeCode code, string id, string nameEn, string nameRu, bool freeByDefault, bool interactive, bool hasWinner)
            : this(code, id, nameEn, nameRu, freeByDefault, interactive, hasWinner, freeByDefault, !interactive)
        {
        }

        /// <exception cref="ArgumentException">A free-by-default mode is not released.</exception>
        public ModeInfo(ModeCode code, string id, string nameEn, string nameRu, bool freeByDefault, bool interactive, bool hasWinner,
            bool inRelease, bool hasAutoControl)
        {
            if (freeByDefault && !inRelease) throw new ArgumentException("a free-by-default mode must be released", nameof(freeByDefault));
            Code = code;
            Id = id;
            NameEn = nameEn;
            NameRu = nameRu;
            FreeByDefault = freeByDefault;
            Interactive = interactive;
            HasWinner = hasWinner;
            InRelease = inRelease;
            HasAutoControl = hasAutoControl || !interactive;
        }
    }

    public static class ModeCatalog
    {
        // Release 1 (owner decision Р18, 2026-10-18): M01 Ring Escape, M02 Geometry Bounce, M05 Helix Drop — all free.
        // M02 and M05 keep their codes; the retired ids "grow_bounce" and "spiral_drop" were never shipped and are not reused.
        // RU names follow the spec catalog (§2.8): "Геометрия отскоков", "Хеликс-дроп" (the RU name follows the EN one and
        // changes with it if the trademark check, question В22, fails).
        // Columns: code, id, EN, RU, freeByDefault, interactive, hasWinner, inRelease, hasAutoControl.
        static readonly ModeInfo[] s_all =
        {
            new ModeInfo(ModeCode.M01, "ring_escape",     "Ring Escape",       "Побег из колец",      true,  false, true,  true,  true),
            new ModeInfo(ModeCode.M02, "geometry_bounce", "Geometry Bounce",   "Геометрия отскоков",  true,  false, true,  true,  true),
            new ModeInfo(ModeCode.M03, "marble_race",     "Marble Race",       "Гонка шариков",       false, false, true,  false, true),
            new ModeInfo(ModeCode.M04, "peg_drop",        "Peg Drop",          "Пег-дроп",            false, true,  false, false, false),
            new ModeInfo(ModeCode.M05, "helix_drop",      "Helix Drop",        "Хеликс-дроп",         true,  true,  false, true,  true),
            new ModeInfo(ModeCode.M06, "ball_battle",     "Ball Battle",       "Битва шаров",         false, false, true,  false, true),
            new ModeInfo(ModeCode.M07, "last_standing",   "Last One Standing", "Последний выживший",  false, false, true,  false, true),
            new ModeInfo(ModeCode.M08, "color_war",       "Color War",         "Война цветов",        false, false, true,  false, true),
            new ModeInfo(ModeCode.M09, "multiply",        "Multiply Gates",    "Умножение",           false, false, true,  false, true),
            new ModeInfo(ModeCode.M10, "maze_escape",     "Maze Escape",       "Лабиринт",            false, true,  true,  false, false),
            new ModeInfo(ModeCode.M11, "smash",           "Smash",             "Разрушение",          false, true,  false, false, false),
            new ModeInfo(ModeCode.M12, "chain_reaction",  "Chain Reaction",    "Цепная реакция",      false, true,  false, false, false),
            new ModeInfo(ModeCode.M13, "pendulum_wave",   "Pendulum Wave",     "Волна маятников",     false, false, false, false, true),
            new ModeInfo(ModeCode.M14, "orbit",           "Orbits",            "Орбиты",              false, false, true,  false, true),
            new ModeInfo(ModeCode.M15, "liquid_sand",     "Liquid & Sand",     "Жидкость и песок",    false, false, false, false, true),
        };

        static readonly ModeInfo[] s_released = BuildReleased();

        static readonly Dictionary<string, ModeInfo> s_byId = BuildIndex();

        /// <summary>Every catalog mode M01–M15 in code order, released or not (codes and ids are reserved).</summary>
        public static IReadOnlyList<ModeInfo> All => s_all;

        /// <summary>The modes of this build's release in code order (release 1: M01, M02, M05). See <see cref="ModeInfo.InRelease"/>.</summary>
        public static IReadOnlyList<ModeInfo> Released => s_released;

        /// <summary>True when <paramref name="code"/> is a catalog mode that ships in this build (<see cref="ModeInfo.InRelease"/>).</summary>
        public static bool IsReleased(ModeCode code)
        {
            int i = (int)code - 1;
            return i >= 0 && i < s_all.Length && s_all[i].InRelease;
        }

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

        static ModeInfo[] BuildReleased()
        {
            var list = new List<ModeInfo>();
            foreach (var m in s_all)
            {
                if (m.InRelease) list.Add(m);
            }
            return list.ToArray();
        }

        static Dictionary<string, ModeInfo> BuildIndex()
        {
            var d = new Dictionary<string, ModeInfo>(StringComparer.Ordinal);
            foreach (var m in s_all) d.Add(m.Id, m);
            return d;
        }
    }
}
