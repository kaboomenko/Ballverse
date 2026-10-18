using UnityEngine;
using UnityEngine.UIElements;

namespace Ballverse.UI.Glass
{
    /// <summary>
    /// Shows a window of a full-panel texture as an element's background: the texture is sized to the panel and shifted
    /// by the element's panel position, so every glass surface samples the same shared backdrop at its own place.
    /// Uses the background-size / background-position style API of UI Toolkit (Unity 2022.2+, stable in Unity 6).
    /// </summary>
    internal static class GlassBackgroundStyle
    {
        public static void Apply(VisualElement element, Texture texture, Vector2 panelSize, Rect worldBound)
        {
            var style = element.style;
            if (texture is RenderTexture rt) style.backgroundImage = new StyleBackground(Background.FromRenderTexture(rt));
            else style.backgroundImage = new StyleBackground(texture as Texture2D);
            style.backgroundSize = new StyleBackgroundSize(new BackgroundSize(new Length(panelSize.x), new Length(panelSize.y)));
            style.backgroundRepeat = new StyleBackgroundRepeat(new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat));
            style.backgroundPositionX = new StyleBackgroundPosition(new BackgroundPosition(BackgroundPositionKeyword.Left, new Length(-worldBound.x)));
            style.backgroundPositionY = new StyleBackgroundPosition(new BackgroundPosition(BackgroundPositionKeyword.Top, new Length(-worldBound.y)));
        }

        /// <summary>Only the offset changed (element moved): cheaper than <see cref="Apply"/>.</summary>
        public static void Move(VisualElement element, Rect worldBound)
        {
            var style = element.style;
            style.backgroundPositionX = new StyleBackgroundPosition(new BackgroundPosition(BackgroundPositionKeyword.Left, new Length(-worldBound.x)));
            style.backgroundPositionY = new StyleBackgroundPosition(new BackgroundPosition(BackgroundPositionKeyword.Top, new Length(-worldBound.y)));
        }

        /// <summary>Removes the inline background so USS (frosted / solid surfaces) applies again.</summary>
        public static void Clear(VisualElement element)
        {
            var style = element.style;
            style.backgroundImage = StyleKeyword.Null;
            style.backgroundSize = StyleKeyword.Null;
            style.backgroundRepeat = StyleKeyword.Null;
            style.backgroundPositionX = StyleKeyword.Null;
            style.backgroundPositionY = StyleKeyword.Null;
        }
    }
}
