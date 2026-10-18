using System;
using UnityEngine.UIElements;

namespace Ballverse.UI.Navigation
{
    /// <summary>
    /// Base of all screen controllers. A screen = UXML layout (Resources/BallverseUI/Screens/{Layout}.uxml) + USS
    /// classes + this controller, which binds a view model. Life cycle: Create (once) → Show / Hide (each time it
    /// becomes top / leaves the top) → Destroy (removed from the stack; tab roots are kept).
    /// </summary>
    public abstract class ScreenController
    {
        bool _visible;
        string _renderedLanguage;

        public abstract ScreenId Id { get; }
        /// <summary>Layout name under Resources/BallverseUI/Screens.</summary>
        protected abstract string Layout { get; }
        protected UiContext Ctx { get; private set; }
        public VisualElement Root { get; private set; }
        public bool IsVisible => _visible;

        internal void Create(UiContext ctx)
        {
            Ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            Root = ctx.InstantiateLayout(Layout);
            Root.AddToClassList("bv-screen");
            Root.AddToClassList("bv-screen--" + Id.ToString().ToLowerInvariant());
            Root.pickingMode = PickingMode.Position;
            ctx.Prepare(Root);
            OnCreate();
        }

        internal void Show()
        {
            if (_visible) return;
            _visible = true;
            Ctx.Localizer.LanguageChanged += HandleLanguageChanged;
            OnShow();
            // A cached screen that was hidden during a language switch re-renders its code-formatted texts now.
            if (_renderedLanguage != null && _renderedLanguage != Ctx.Localizer.Language) HandleLanguageChanged();
            _renderedLanguage = Ctx.Localizer.Language;
        }

        internal void Hide()
        {
            if (!_visible) return;
            _visible = false;
            Ctx.Localizer.LanguageChanged -= HandleLanguageChanged;
            OnHide();
        }

        internal void Destroy()
        {
            Hide();
            OnDestroy();
            Root?.RemoveFromHierarchy();
        }

        void HandleLanguageChanged()
        {
            _renderedLanguage = Ctx.Localizer.Language;
            Ctx.Binder.Refresh(Root);
            OnLanguageChanged();
        }

        protected virtual void OnCreate() { }
        /// <summary>Bind view-model events and refresh.</summary>
        protected virtual void OnShow() { }
        /// <summary>Unbind view-model events.</summary>
        protected virtual void OnHide() { }
        protected virtual void OnDestroy() { }
        /// <summary>Re-render code-formatted texts (keys from UXML are refreshed automatically).</summary>
        protected virtual void OnLanguageChanged() { }

        /// <summary>System back on this screen; return true when handled here (e.g. confirm leaving a game).</summary>
        public virtual bool HandleBack()
        {
            return false;
        }

        /// <summary>Per-frame tick while visible.</summary>
        public virtual void Tick(float deltaTime) { }

        protected T Q<T>(string name) where T : VisualElement
        {
            return UiAssets.Q<T>(Root, name);
        }
    }
}
