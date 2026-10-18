namespace Ballverse.Core.RenderGeometry
{
    /// <summary>
    /// Meaning of the palette indices a mode writes into <c>BodyState.Color</c> and <see cref="RenderEvent.ColorIndex"/>
    /// (spec §7.4 "цвета — индексы палитры"). The renderer resolves them against the active theme (spec §2.10) and the
    /// user's <c>ballColors</c> (spec §2.2), so changing the theme re-colours a recorded clip without re-simulating it.
    /// <list type="table">
    /// <item><term>0 … 223</term><description>Participant colours: entry i of the ball palette, cycling
    /// (i mod palette length). The ball palette is <c>ballColors</c> when set, otherwise the theme palette.</description></item>
    /// <item><term>224 … 239</term><description>Theme palette entries ignoring <c>ballColors</c> (i − 224, cycling): decor
    /// that should follow the theme even when the user picked ball colours (e.g. rainbow rings).</description></item>
    /// <item><term>240 … 247</term><description>Named theme roles, see the constants.</description></item>
    /// <item><term>248 … 255</term><description>Reserved; resolve to <see cref="Arena"/>.</description></item>
    /// </list>
    /// </summary>
    public static class PaletteSlots
    {
        /// <summary>First index of the theme-only decor range.</summary>
        public const int ThemeDecorFirst = 224;
        /// <summary>First named role index.</summary>
        public const int RolesFirst = 240;

        /// <summary>Walls, rings, platforms: the arena colour of the theme.</summary>
        public const byte Arena = 240;
        /// <summary>Secondary arena colour (alternate rings, inner column of the Helix tower).</summary>
        public const byte ArenaAlt = 241;
        /// <summary>Deadly / danger sectors (M05).</summary>
        public const byte Danger = 242;
        /// <summary>Accent: highlights, fireball, finish line.</summary>
        public const byte Accent = 243;
        /// <summary>Muted: passed / extinguished parts.</summary>
        public const byte Muted = 244;
        /// <summary>Text drawn in the arena.</summary>
        public const byte Text = 245;
        /// <summary>The background colour (to punch "holes").</summary>
        public const byte Background = 246;
        /// <summary>Gold: winner, records.</summary>
        public const byte Gold = 247;
    }
}
