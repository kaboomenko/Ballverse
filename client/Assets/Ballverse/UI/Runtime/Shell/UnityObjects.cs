using UnityEngine;

namespace Ballverse.UI
{
    /// <summary>
    /// Destroys Unity objects correctly in both play mode and edit mode (EditMode tests, editor tooling):
    /// Object.Destroy is not allowed outside play mode.
    /// </summary>
    internal static class UnityObjects
    {
        public static void Destroy(Object o)
        {
            if (o == null) return;
            if (Application.isPlaying) Object.Destroy(o);
            else Object.DestroyImmediate(o);
        }
    }
}
