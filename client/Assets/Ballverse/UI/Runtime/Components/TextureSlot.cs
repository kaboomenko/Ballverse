using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ballverse.UI.Components
{
    /// <summary>
    /// Shows a texture (render target, preview video, thumbnail) as an element's background, updating the style only
    /// when the texture object changes. Elements get <c>bv-slot--empty</c> while there is no texture.
    /// </summary>
    public static class TextureSlot
    {
        sealed class Box
        {
            public Texture Texture;
        }

        static readonly ConditionalWeakTable<VisualElement, Box> s_current = new ConditionalWeakTable<VisualElement, Box>();

        public static void Set(VisualElement element, Texture texture)
        {
            if (element == null) return;
            var box = s_current.GetValue(element, _ => new Box());
            if (box.Texture == texture && (texture != null || element.ClassListContains("bv-slot--empty"))) return;
            box.Texture = texture;
            element.EnableInClassList("bv-slot--empty", texture == null);
            if (texture == null)
            {
                element.style.backgroundImage = StyleKeyword.Null;
                return;
            }
            element.style.backgroundImage = ToBackground(texture);
        }

        public static StyleBackground ToBackground(Texture texture)
        {
            if (texture is RenderTexture rt) return new StyleBackground(Background.FromRenderTexture(rt));
            if (texture is Texture2D t2) return new StyleBackground(t2);
            return new StyleBackground(StyleKeyword.Null);
        }
    }
}
