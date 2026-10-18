namespace Ballverse.Core.Projects
{
    /// <summary>Bytes used by one project folder, by <see cref="StorageCategory"/> (spec §3.9 "Хранилище").</summary>
    public sealed class ProjectStorageUsage
    {
        readonly long[] _bytes;

        /// <param name="bytesByCategory">Bytes indexed by <see cref="StorageCategory"/>; copied.</param>
        public ProjectStorageUsage(long[] bytesByCategory)
        {
            _bytes = new long[(int)StorageCategory.Other + 1];
            if (bytesByCategory != null)
            {
                for (int i = 0; i < _bytes.Length && i < bytesByCategory.Length; i++) _bytes[i] = bytesByCategory[i];
            }
        }

        /// <summary>Bytes of one category.</summary>
        public long this[StorageCategory category] => _bytes[(int)category];

        /// <summary>Sum over all categories.</summary>
        public long TotalBytes
        {
            get
            {
                long sum = 0;
                for (int i = 0; i < _bytes.Length; i++) sum += _bytes[i];
                return sum;
            }
        }
    }
}
