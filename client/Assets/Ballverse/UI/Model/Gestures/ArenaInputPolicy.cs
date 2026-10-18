namespace Ballverse.UI.Gestures
{
    /// <summary>
    /// Which touches on the full-screen arena reach an interactive game (spec §2.6, §4.3). Positions are panel units
    /// (≈ pt) relative to the screen's top-left corner. Pure.
    /// </summary>
    public static class ArenaInputPolicy
    {
        /// <summary>Spec §2.6: "Мёртвая зона у краёв экрана — 24 pt" (system gestures of iOS / Android).</summary>
        public const float EdgeDeadZone = 24f;

        /// <param name="topStripBottom">Bottom edge of the top bar strip (touches above it are chrome, not game).</param>
        /// <param name="bottomStripTop">Top edge of the bottom bar strip (touches below it are chrome, not game).</param>
        /// <returns>
        /// False for a touch within <see cref="EdgeDeadZone"/> of a screen edge, or in a bar strip: there a tap only brings
        /// the hidden bars back (spec §4.3 "по тапу вне игровой зоны") and must not also act in the game.
        /// </returns>
        public static bool Accepts(float x, float y, float screenWidth, float screenHeight, float topStripBottom, float bottomStripTop)
        {
            if (!(screenWidth > 0f) || !(screenHeight > 0f)) return false;
            if (x < EdgeDeadZone || y < EdgeDeadZone || x > screenWidth - EdgeDeadZone || y > screenHeight - EdgeDeadZone) return false;
            if (y <= topStripBottom || y >= bottomStripTop) return false;
            return true;
        }
    }
}
