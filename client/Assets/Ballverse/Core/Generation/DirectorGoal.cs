namespace Ballverse.Core.Generation
{
    /// <summary>Extra director goal on top of generation (spec §2.5).</summary>
    public enum DirectorGoal
    {
        None = 0,
        /// <summary>"Win in the last second": favour candidates with high finishTension (release 1: M01; roadmap: M03, M06).</summary>
        LastSecond = 1,
        /// <summary>"Comeback": favour candidates whose winner was last in the first half (race and battle).</summary>
        Comeback = 2,
    }
}
