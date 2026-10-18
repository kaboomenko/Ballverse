namespace Ballverse.UI.Navigation
{
    /// <summary>Full-screen destinations of the release-1 screen map (spec §4.2).</summary>
    public enum ScreenId
    {
        /// <summary>Tab "Режимы": the mode gallery.</summary>
        Gallery = 0,
        /// <summary>Tab "Проекты": projects and clips.</summary>
        Projects = 1,
        /// <summary>Tab "Магазин".</summary>
        Shop = 2,
        Simulation = 3,
        Studio = 4,
        /// <summary>App settings (gear).</summary>
        Settings = 5,
        /// <summary>Settings → Storage (spec §3.9).</summary>
        Storage = 6,
        /// <summary>Settings → About and third-party licenses.</summary>
        About = 7,
        /// <summary>Own explanation screen shown before the system ATT dialog (spec §5.6).</summary>
        TrackingPrePrompt = 8,
        /// <summary>Full-screen glass export progress (spec §3.8 step 3).</summary>
        ExportProgress = 9,
        /// <summary>Export finished: preview, save, share (spec §3.8 step 4).</summary>
        ExportDone = 10,
    }

    /// <summary>Tab bar entries (spec §4.2).</summary>
    public enum TabId
    {
        Modes = 0,
        Projects = 1,
        Shop = 2,
    }

    public static class ScreenIds
    {
        public static ScreenId RootOf(TabId tab)
        {
            switch (tab)
            {
                case TabId.Projects: return ScreenId.Projects;
                case TabId.Shop: return ScreenId.Shop;
                default: return ScreenId.Gallery;
            }
        }

        /// <summary>Tab roots show the tab bar.</summary>
        public static bool IsTabRoot(ScreenId id)
        {
            return id == ScreenId.Gallery || id == ScreenId.Projects || id == ScreenId.Shop;
        }

        public static bool TryGetTab(ScreenId id, out TabId tab)
        {
            switch (id)
            {
                case ScreenId.Gallery: tab = TabId.Modes; return true;
                case ScreenId.Projects: tab = TabId.Projects; return true;
                case ScreenId.Shop: tab = TabId.Shop; return true;
                default: tab = TabId.Modes; return false;
            }
        }

        /// <summary>
        /// Screens the user may not leave with the system back gesture while they are on top (export in progress:
        /// cancel goes through its own button and confirmation).
        /// </summary>
        public static bool BlocksSystemBack(ScreenId id)
        {
            return id == ScreenId.ExportProgress;
        }
    }
}
