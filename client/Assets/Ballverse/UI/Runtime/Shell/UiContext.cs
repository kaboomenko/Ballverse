using Ballverse.UI.Components;
using Ballverse.UI.Glass;
using Ballverse.UI.Localization;
using Ballverse.UI.Navigation;
using Ballverse.UI.Theming;
using Ballverse.UI.ViewModels;
using UnityEngine.UIElements;

namespace Ballverse.UI
{
    /// <summary>Services shared by screens and components of one shell instance.</summary>
    public sealed class UiContext
    {
        public Localizer Localizer { get; }
        public LocalizedTreeBinder Binder { get; }
        public IBallverseViewModels ViewModels { get; }
        public VisualElement AppRoot { get; }
        public UiNavigator Navigator { get; internal set; }
        public SheetHost Sheets { get; internal set; }
        public DialogHost Dialogs { get; internal set; }
        public ToastHost Toasts { get; internal set; }
        public GlassSurfaces Glass { get; internal set; }
        public SafeAreaController SafeArea { get; internal set; }
        public ThemeController Theme { get; internal set; }

        internal UiContext(Localizer localizer, IBallverseViewModels viewModels, VisualElement appRoot)
        {
            Localizer = localizer;
            Binder = new LocalizedTreeBinder(localizer);
            ViewModels = viewModels;
            AppRoot = appRoot;
        }

        public string L(string key)
        {
            return Localizer.Get(key);
        }

        public string L(LocalizedText text)
        {
            return Localizer.Resolve(text);
        }

        public string Format(string key, params object[] args)
        {
            return Localizer.Format(key, args);
        }

        /// <summary>
        /// Prepares a freshly instantiated subtree: localizes <c>#key</c> texts, wires icons, registers glass
        /// surfaces and safe-area spacers.
        /// </summary>
        public void Prepare(VisualElement root)
        {
            Binder.Bind(root);
            Icons.AttachAll(root);
            Glass?.Scan(root);
            SafeArea?.Apply(root);
        }

        /// <summary>Label with a localization key (kept across language switches).</summary>
        public Label KeyLabel(string key, string className = null)
        {
            var label = new Label();
            if (className != null) label.AddToClassList(className);
            Binder.SetKey(label, key);
            return label;
        }

        /// <summary>Label with text computed by code (user content or formatted values).</summary>
        public static Label TextLabel(string text, string className = null)
        {
            var label = new Label(text ?? "");
            if (className != null) label.AddToClassList(className);
            return label;
        }
    }
}
