using System;
#if UNITY_ANDROID && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Ballverse.Studio
{
    /// <summary>
    /// Best-effort fsync of a folder after a rename, so the rename itself survives power loss (IFileStore.Replace contract,
    /// spec §7.4). Android: <c>open(dir, O_RDONLY)</c> + <c>fsync</c> + <c>close</c> from libc (resolved at run time, so a
    /// missing symbol only disables the sync). iOS: not done — APFS commits renames atomically through its metadata journal,
    /// and declaring libc's variadic <c>open</c> for IL2CPP's static "__Internal" linking would clash with the system headers.
    /// Editor and desktop players: not done. Never throws.
    /// </summary>
    public static class DirectorySync
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        static bool s_disabled;

        /// <summary>True when the platform implementation is compiled in and has not failed.</summary>
        public static bool IsAvailable => !s_disabled;

        public static void TrySync(string directory)
        {
            if (s_disabled || string.IsNullOrEmpty(directory)) return;
            try
            {
                int fd = open(directory, 0 /* O_RDONLY */);
                if (fd < 0) return;
                try
                {
                    fsync(fd);
                }
                finally
                {
                    close(fd);
                }
            }
            catch (Exception e) when (e is DllNotFoundException || e is EntryPointNotFoundException || e is MarshalDirectiveException)
            {
                s_disabled = true;
                UnityEngine.Debug.LogWarning("[Studio] Folder fsync unavailable: " + e.GetType().Name);
            }
        }

        [DllImport("libc", SetLastError = true)]
        static extern int open([MarshalAs(UnmanagedType.LPStr)] string path, int flags);

        [DllImport("libc", SetLastError = true)]
        static extern int fsync(int fd);

        [DllImport("libc", SetLastError = true)]
        static extern int close(int fd);
#else
        /// <summary>True when the platform implementation is compiled in and has not failed.</summary>
        public static bool IsAvailable => false;

        /// <summary>No-op on this platform (see the class remarks).</summary>
        public static void TrySync(string directory)
        {
            GC.KeepAlive(directory);
        }
#endif
    }
}
