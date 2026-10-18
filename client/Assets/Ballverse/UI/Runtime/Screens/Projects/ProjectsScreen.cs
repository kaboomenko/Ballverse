using System.Collections.Generic;
using Ballverse.UI.Components;
using Ballverse.UI.Format;
using Ballverse.UI.Localization;
using Ballverse.UI.Navigation;
using Ballverse.UI.ViewModels;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ballverse.UI.Screens
{
    /// <summary>
    /// Tab "Проекты" (spec §3.9, §4.3): "Проекты" / "Клипы" tabs, grid of cards with cover, duration and date; long press
    /// opens the context menu (open, rename, duplicate, export again, delete); empty state with "Сделать первый ролик".
    /// </summary>
    public sealed class ProjectsScreen : ScreenController
    {
        IProjectsViewModel _vm;
        VisualElement _grid;
        VisualElement _empty;
        SegmentedControl _tabs;
        ProjectsTab _tab = ProjectsTab.Projects;
        VisualElement _emptyIcon;
        Tween _emptyPulse;

        public override ScreenId Id => ScreenId.Projects;
        protected override string Layout => "Projects";

        protected override void OnCreate()
        {
            _vm = Ctx.ViewModels.Projects;
            _grid = Q<VisualElement>("grid");
            _empty = Q<VisualElement>("empty");
            _emptyIcon = Q<VisualElement>("empty-icon");
            Q<ScrollView>("scroll").verticalScrollerVisibility = ScrollerVisibility.Hidden;
            _tabs = new SegmentedControl();
            _tabs.AddSegment(Ctx.L("projects.tab.projects"));
            _tabs.AddSegment(Ctx.L("projects.tab.clips"));
            _tabs.SetSelectedWithoutNotify(0);
            _tabs.SelectionChanged += i =>
            {
                _tab = i == 0 ? ProjectsTab.Projects : ProjectsTab.Clips;
                Rebuild();
            };
            Q<VisualElement>("tabs-host").Add(_tabs);
            Tap.On(Root, "settings", () => Ctx.Navigator.Push(ScreenId.Settings));
            Tap.On(Root, "empty-action", () => Ctx.Navigator.SelectTab(TabId.Modes));
        }

        protected override void OnShow()
        {
            _vm.Changed += Rebuild;
            Rebuild();
        }

        protected override void OnHide()
        {
            _vm.Changed -= Rebuild;
            _emptyPulse?.Cancel();
        }

        protected override void OnLanguageChanged()
        {
            _tabs.SetLabel(0, Ctx.L("projects.tab.projects"));
            _tabs.SetLabel(1, Ctx.L("projects.tab.clips"));
            Rebuild();
        }

        void Rebuild()
        {
            var items = _tab == ProjectsTab.Projects ? _vm.Projects : _vm.Clips;
            _grid.Clear();
            foreach (var item in items) _grid.Add(BuildCard(item));
            bool empty = items.Count == 0;
            _empty.style.display = empty ? DisplayStyle.Flex : DisplayStyle.None;
            Ctx.Binder.SetKey(Q<Label>("empty-title"), _tab == ProjectsTab.Projects ? "projects.empty.title" : "projects.empty_clips.title");
            Ctx.Binder.SetKey(Q<Label>("empty-body"), _tab == ProjectsTab.Projects ? "projects.empty.body" : "projects.empty_clips.body");
            if (empty) Pulse();
        }

        void Pulse()
        {
            // Gentle breathing animation of the empty-state icon (spec §4.3 "анимация"); disabled under reduced motion.
            _emptyPulse?.Cancel();
            if (Tween.ReduceMotion || !IsVisible) return;
            _emptyPulse = Tween.Run(_emptyIcon, 1.6f, Ease.Linear, k =>
            {
                float s = 1f + 0.06f * Mathf.Sin(k * Mathf.PI * 2f);
                _emptyIcon.style.scale = new Scale(new Vector3(s, s, 1f));
            }, () =>
            {
                if (IsVisible && _empty.resolvedStyle.display == DisplayStyle.Flex) Pulse();
            });
        }

        VisualElement BuildCard(ProjectCardData item)
        {
            var card = new VisualElement();
            card.AddToClassList("bv-project-card");
            var cover = new VisualElement();
            cover.AddToClassList("bv-project-card__cover");
            TextureSlot.Set(cover, item.Cover);
            var duration = UiContext.TextLabel(DisplayFormat.DurationMs(item.DurationMs), "bv-project-card__duration");
            cover.Add(duration);
            card.Add(cover);
            card.Add(UiContext.TextLabel(item.Title, "bv-project-card__title"));
            var meta = new VisualElement();
            meta.AddToClassList("bv-project-card__meta");
            if (!string.IsNullOrEmpty(item.ModeId)) meta.Add(Ctx.KeyLabel("mode." + item.ModeId + ".name", "bv-project-card__mode"));
            meta.Add(UiContext.TextLabel(DisplayFormat.ShortDate(item.ModifiedLocal, Ctx.Localizer), "bv-project-card__date"));
            card.Add(meta);
            var tab = _tab;
            string id = item.Id;
            card.AddManipulator(new TapOrLongPress(() => Open(tab, id), () => ShowMenu(tab, item)));
            return card;
        }

        void Open(ProjectsTab tab, string id)
        {
            Navigate(_vm.Open(tab, id));
        }

        void Navigate(OpenTarget target)
        {
            if (target == OpenTarget.Studio) Ctx.Navigator.Push(ScreenId.Studio);
            else if (target == OpenTarget.Simulation) Ctx.Navigator.Push(ScreenId.Simulation);
        }

        void ShowMenu(ProjectsTab tab, ProjectCardData item)
        {
            var sheet = new BottomSheet(Ctx, new SheetOptions { Detents = SheetDetents.FitContent, Glass = false });
            sheet.SetTitle(item.Title);
            var form = new FormBuilder(Ctx, sheet.Content);
            string id = item.Id;
            form.NavRow(IconKind.Play, "projects.menu.open", null, () =>
            {
                sheet.Close();
                Open(tab, id);
            }, false, false);
            form.NavRow(IconKind.Edit, "projects.menu.rename", null, () =>
            {
                sheet.Close();
                Rename(tab, item);
            }, false, false);
            form.NavRow(IconKind.Copy, "projects.menu.duplicate", null, () =>
            {
                sheet.Close();
                _vm.Duplicate(tab, id);
            }, false, false);
            if (tab == ProjectsTab.Projects)
            {
                form.NavRow(IconKind.Share, "projects.menu.export_again", null, () =>
                {
                    sheet.Close();
                    var target = _vm.ExportAgain(id);
                    Navigate(target);
                    if (target == OpenTarget.Studio) Ctx.ViewModels.Export.Begin();
                }, false, false);
            }
            var delete = form.NavRow(IconKind.Trash, "projects.menu.delete", null, () =>
            {
                sheet.Close();
                Ctx.Dialogs.Confirm("projects.delete_title", "projects.delete_body", "common.delete", true, () => _vm.Delete(tab, id));
            }, false, false);
            delete.AddToClassList("bv-row--destructive");
            sheet.Show();
        }

        void Rename(ProjectsTab tab, ProjectCardData item)
        {
            var spec = new DialogSpec
            {
                Title = LocalizedText.FromKey("projects.rename_title"),
                TextFieldValue = item.Title,
                TextFieldMaxLength = _vm.MaxTitleLength,
            };
            string id = item.Id;
            spec.Buttons.Add(new DialogButton(LocalizedText.FromKey("common.save"), DialogButtonStyle.Primary, text =>
            {
                if (!_vm.Rename(tab, id, text)) Ctx.Toasts.Show(Ctx.L("projects.rename_invalid"));
            }));
            spec.Buttons.Add(DialogButton.Key("common.cancel", DialogButtonStyle.Plain, null));
            Ctx.Dialogs.Show(spec);
        }
    }
}
