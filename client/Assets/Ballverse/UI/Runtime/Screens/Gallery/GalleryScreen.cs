using System.Collections.Generic;
using Ballverse.UI.Components;
using Ballverse.UI.Navigation;
using Ballverse.UI.ViewModels;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ballverse.UI.Screens
{
    /// <summary>
    /// Tab "Режимы" (spec §4.3, release 1): one card per free mode with a looping preview slot, name, description and
    /// badges. No locks or prices in release 1 (all three modes are free).
    /// </summary>
    public sealed class GalleryScreen : ScreenController
    {
        readonly Dictionary<string, VisualElement> _previews = new Dictionary<string, VisualElement>();
        VisualElement _cards;
        IGalleryViewModel _vm;

        public override ScreenId Id => ScreenId.Gallery;
        protected override string Layout => "Gallery";

        protected override void OnCreate()
        {
            _vm = Ctx.ViewModels.Gallery;
            _cards = Q<VisualElement>("cards");
            Q<ScrollView>("scroll").verticalScrollerVisibility = ScrollerVisibility.Hidden;
            Tap.On(Root, "settings", () => Ctx.Navigator.Push(ScreenId.Settings));
        }

        protected override void OnShow()
        {
            _vm.Changed += Rebuild;
            _vm.SetPreviewsActive(true);
            Rebuild();
        }

        protected override void OnHide()
        {
            _vm.Changed -= Rebuild;
            _vm.SetPreviewsActive(false);
        }

        public override void Tick(float deltaTime)
        {
            // Preview players may create their target textures after the first frame.
            foreach (var kv in _previews) SetPreview(kv.Value, _vm.GetPreview(kv.Key));
        }

        void Rebuild()
        {
            _cards.Clear();
            _previews.Clear();
            foreach (var mode in _vm.Modes) _cards.Add(BuildCard(mode));
        }

        VisualElement BuildCard(ModeCardData mode)
        {
            var card = new VisualElement();
            card.AddToClassList("bv-mode-card");
            var preview = new VisualElement();
            preview.AddToClassList("bv-mode-card__preview");
            preview.Add(Icons.Create(IconKind.Simulation, "bv-mode-card__placeholder"));
            card.Add(preview);
            _previews[mode.ModeId] = preview;
            SetPreview(preview, _vm.GetPreview(mode.ModeId));

            var badges = new VisualElement();
            badges.AddToClassList("bv-mode-card__badges");
            if (mode.IsNew) badges.Add(Badge("badge.new", IconKind.Sparkle, "bv-badge--accent"));
            if (mode.Interactive) badges.Add(Badge("badge.interactive", IconKind.Hand, null));
            if (mode.HasWinner) badges.Add(Badge("badge.winner", IconKind.Trophy, null));
            card.Add(badges);

            var caption = new VisualElement();
            caption.AddToClassList("bv-mode-card__caption");
            caption.Add(Ctx.KeyLabel(mode.NameKey, "bv-mode-card__name"));
            caption.Add(Ctx.KeyLabel(mode.DescriptionKey, "bv-mode-card__description"));
            var play = new VisualElement();
            play.AddToClassList("bv-mode-card__play");
            play.Add(Icons.Create(IconKind.Play, "bv-mode-card__play-icon"));
            caption.Add(play);
            card.Add(caption);

            string id = mode.ModeId;
            Tap.On(card, () =>
            {
                _vm.OpenMode(id);
                Ctx.Navigator.Push(ScreenId.Simulation);
            });
            return card;
        }

        VisualElement Badge(string key, IconKind icon, string extraClass)
        {
            var b = new VisualElement();
            b.AddToClassList("bv-badge");
            if (extraClass != null) b.AddToClassList(extraClass);
            b.Add(Icons.Create(icon, "bv-badge__icon"));
            b.Add(Ctx.KeyLabel(key, "bv-badge__label"));
            return b;
        }

        static void SetPreview(VisualElement preview, Texture texture)
        {
            TextureSlot.Set(preview, texture);
        }
    }
}
