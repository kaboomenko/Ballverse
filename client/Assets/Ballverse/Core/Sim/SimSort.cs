namespace Ballverse.Core.Sim
{
    /// <summary>
    /// Allocation-free stable merge sort for ulong keys (events, sensor pairs). Own implementation so the order and the
    /// absence of allocations do not depend on the runtime's Array.Sort (Mono / IL2CPP / CoreCLR).
    /// </summary>
    internal static class SimSort
    {
        const int InsertionThreshold = 24;

        /// <summary>Sorts keys[0..count) ascending. <paramref name="scratch"/> must hold at least <paramref name="count"/> items.</summary>
        public static void Sort(ulong[] keys, int count, ulong[] scratch)
        {
            if (count < 2) return;
            if (count <= InsertionThreshold)
            {
                InsertionSort(keys, 0, count);
                return;
            }
            // Bottom-up: sort runs with insertion sort, then merge doubling widths, ping-ponging between buffers.
            for (int start = 0; start < count; start += InsertionThreshold)
            {
                int end = start + InsertionThreshold;
                if (end > count) end = count;
                InsertionSort(keys, start, end);
            }
            ulong[] src = keys;
            ulong[] dst = scratch;
            for (int width = InsertionThreshold; width < count; width *= 2)
            {
                for (int lo = 0; lo < count; lo += 2 * width)
                {
                    int mid = lo + width;
                    if (mid > count) mid = count;
                    int hi = lo + 2 * width;
                    if (hi > count) hi = count;
                    Merge(src, dst, lo, mid, hi);
                }
                ulong[] t = src;
                src = dst;
                dst = t;
            }
            if (!ReferenceEquals(src, keys))
            {
                for (int i = 0; i < count; i++) keys[i] = src[i];
            }
        }

        static void Merge(ulong[] src, ulong[] dst, int lo, int mid, int hi)
        {
            int i = lo, j = mid, k = lo;
            while (i < mid && j < hi) dst[k++] = src[j] < src[i] ? src[j++] : src[i++];
            while (i < mid) dst[k++] = src[i++];
            while (j < hi) dst[k++] = src[j++];
        }

        static void InsertionSort(ulong[] a, int start, int end)
        {
            for (int i = start + 1; i < end; i++)
            {
                ulong v = a[i];
                int j = i - 1;
                while (j >= start && a[j] > v)
                {
                    a[j + 1] = a[j];
                    j--;
                }
                a[j + 1] = v;
            }
        }

        /// <summary>Insertion sort for short int lists (broadphase candidates).</summary>
        public static void InsertionSort(int[] a, int start, int end)
        {
            for (int i = start + 1; i < end; i++)
            {
                int v = a[i];
                int j = i - 1;
                while (j >= start && a[j] > v)
                {
                    a[j + 1] = a[j];
                    j--;
                }
                a[j + 1] = v;
            }
        }
    }
}
