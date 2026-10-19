using UnityEngine;

namespace Ballverse.Studio
{
    /// <summary>Small Unity helpers shared by the studio runtime.</summary>
    internal static class StudioUnity
    {
        /// <summary>Destroys an engine object in play mode and in edit mode (EditMode tests, editor tooling); null is ignored.</summary>
        public static void Destroy(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Object.Destroy(obj);
            else Object.DestroyImmediate(obj);
        }

        /// <summary>Releases and destroys a render texture; null is ignored.</summary>
        public static void Release(RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            Destroy(rt);
        }
    }
}
