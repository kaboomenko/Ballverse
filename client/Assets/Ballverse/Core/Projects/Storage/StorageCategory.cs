namespace Ballverse.Core.Projects
{
    /// <summary>What a file in a project folder is, for the storage screen (spec §3.9 "Хранилище", §7.4 layout).</summary>
    public enum StorageCategory
    {
        /// <summary>project.json, its backup and temporary files.</summary>
        ProjectFile = 0,
        /// <summary>clip.bvr: the replay.</summary>
        Replay = 1,
        /// <summary>clip.bvs: render stream, can be rebuilt.</summary>
        RenderCache = 2,
        /// <summary>The user video copy (media/, or userVideo.file).</summary>
        Media = 3,
        /// <summary>thumbs/: timeline thumbnails and the cover (cache).</summary>
        Thumbs = 4,
        /// <summary>Exported videos kept with the project (exports/, or exports[].file).</summary>
        Exports = 5,
        /// <summary>Anything else.</summary>
        Other = 6,
    }
}
