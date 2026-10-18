namespace Ballverse.Core.Params
{
    /// <summary>Category of dynamic bodies in a mode's §2.9 body budget (see <see cref="BodyBudgets"/>).</summary>
    public enum BodyKind
    {
        /// <summary>Every dynamic body counted together (the spec gives one number covering balls and other bodies).</summary>
        Bodies = 0,
        /// <summary>Balls / fighters / pendulum bobs.</summary>
        Balls = 1,
        /// <summary>Moving obstacles (M03).</summary>
        Obstacles = 2,
        /// <summary>Visible platforms (M05).</summary>
        Platforms = 3,
        /// <summary>Shards and fragments (M05, M11).</summary>
        Shards = 4,
        /// <summary>Arrows and other projectiles (M06).</summary>
        Projectiles = 5,
        /// <summary>Arena tiles (M07).</summary>
        Tiles = 6,
    }
}
