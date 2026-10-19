namespace Ballverse.UI.Navigation
{
    /// <summary>
    /// Holds the current instance of something that closes asynchronously (a bottom sheet animates out before its
    /// Closed callback runs). A late callback of a replaced instance must not clear its successor:
    /// <see cref="Release"/> clears only when the instance is still the current one. Pure.
    /// </summary>
    public sealed class CurrentRef<T> where T : class
    {
        public T Value { get; private set; }

        public bool HasValue => Value != null;

        /// <summary>Makes <paramref name="value"/> the current instance (replacing any previous one).</summary>
        public void Set(T value)
        {
            Value = value;
        }

        /// <summary>
        /// Clears the reference if <paramref name="value"/> is still the current instance. Returns false (and changes
        /// nothing) for a stale instance that was already replaced.
        /// </summary>
        public bool Release(T value)
        {
            if (value == null || !ReferenceEquals(Value, value)) return false;
            Value = null;
            return true;
        }

        public bool Is(T value)
        {
            return value != null && ReferenceEquals(Value, value);
        }
    }
}
