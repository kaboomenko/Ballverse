namespace Ballverse.Media
{
    public enum GallerySaveStatus
    {
        Saved = 0,
        /// <summary>The user refused the (add-only / legacy storage) permission. Offer "Share" instead.</summary>
        PermissionDenied = 1,
        Failed = 2,
    }

    /// <summary>Result of <see cref="IMediaBridge.SaveToGalleryAsync"/>.</summary>
    public sealed class GallerySaveResult
    {
        public GallerySaveStatus Status { get; }
        /// <summary>Android content URI / file path, iOS PHAsset local identifier (may be empty with add-only access).</summary>
        public string Location { get; }
        /// <summary>
        /// Whether the item was placed into the named album. Android: always when saved (folder Movies/&lt;album&gt;).
        /// iOS (release 1): always false — the app uses add-only Photos access (spec §3.8, NSPhotoLibraryAddUsageDescription),
        /// which cannot create or look up albums, so the video goes to the library (Recents). An iOS "Ballverse" album needs
        /// full library access and its own permission prompt; that is an open owner decision.
        /// </summary>
        public bool AddedToAlbum { get; }
        public string ErrorMessage { get; }

        public GallerySaveResult(GallerySaveStatus status, string location, bool addedToAlbum, string errorMessage)
        {
            Status = status;
            Location = location ?? string.Empty;
            AddedToAlbum = addedToAlbum;
            ErrorMessage = errorMessage ?? string.Empty;
        }

        public bool IsSaved => Status == GallerySaveStatus.Saved;
    }
}
