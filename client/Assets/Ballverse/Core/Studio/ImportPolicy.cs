namespace Ballverse.Core.Studio
{
    /// <summary>Rules of the gallery import (spec §3.2). Pure.</summary>
    public static class ImportPolicy
    {
        /// <summary>
        /// Spec §3.2 "предупреждение, если файл занимает > 25% свободного места": compared with the free space the device had
        /// before the copy. When only the free space after the copy is known, the file's own size is added back.
        /// </summary>
        /// <param name="fileBytes">Size of the imported file.</param>
        /// <param name="freeBeforeBytes">Free bytes before the copy, or −1 when unknown.</param>
        /// <param name="freeAfterBytes">Free bytes after the copy, or −1 when unknown.</param>
        public static bool IsLargeFile(long fileBytes, long freeBeforeBytes, long freeAfterBytes)
        {
            if (fileBytes <= 0) return false;
            long free = freeBeforeBytes >= 0 ? freeBeforeBytes : freeAfterBytes >= 0 ? freeAfterBytes + fileBytes : -1;
            if (free <= 0) return false;
            return fileBytes > free / 4;
        }
    }
}
