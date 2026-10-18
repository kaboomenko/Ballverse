namespace Ballverse.Core.Projects
{
    /// <summary>Which file a project was loaded from (spec §8 ST6: backup copy <c>project.json.bak</c>).</summary>
    public enum LoadSource
    {
        None = 0,
        /// <summary><c>project.json</c>.</summary>
        Main = 1,
        /// <summary><c>project.json.bak</c>, because the main file was missing or unusable.</summary>
        Backup = 2,
    }
}
