using System;
using Ballverse.UI.Components;
using Ballverse.UI.Localization;
using Ballverse.UI.Navigation;
using Ballverse.UI.ViewModels;

namespace Ballverse.UI.Screens
{
    /// <summary>
    /// Shows the one-time "first video" celebration (spec §4.3, release 1) when the quest is completed: a short card
    /// that suggests the other release-1 mode and sells nothing. It waits until no other dialog is open and the export
    /// progress screen is gone.
    /// </summary>
    public sealed class QuestCelebrationController : IDisposable
    {
        readonly UiContext _ctx;
        readonly IQuestCelebrationViewModel _vm;
        readonly Action<string> _openMode;
        DialogHandle _dialog;
        bool _pending;

        public QuestCelebrationController(UiContext ctx, Action<string> openMode)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _openMode = openMode;
            _vm = ctx.ViewModels.QuestCelebration;
            if (_vm == null) return;
            _vm.Changed += OnChanged;
            OnChanged();
        }

        void OnChanged()
        {
            _pending = _vm.Pending != null;
        }

        /// <summary>Per frame: presents the celebration as soon as nothing else is in the way.</summary>
        public void Tick()
        {
            if (!_pending || _vm == null || (_dialog != null && _dialog.IsOpen)) return;
            if (_ctx.Dialogs.AnyOpen || _ctx.Navigator.Current == ScreenId.ExportProgress) return;
            var data = _vm.Pending;
            _pending = false;
            if (data == null) return;
            Show(data);
        }

        void Show(QuestCelebrationData data)
        {
            string modeName = _ctx.L(data.SuggestedModeNameKey);
            var spec = new DialogSpec
            {
                Title = LocalizedText.FromKey("quest.done.title"),
                Body = LocalizedText.FromKey("quest.done.body", modeName),
                Icon = IconKind.Sparkle,
                Cancelled = () => _vm.MarkShown(),
            };
            // "Позже" is a full-size secondary button, the same size as the main one (spec §4.3).
            spec.Buttons.Add(new DialogButton(LocalizedText.FromKey("quest.done.try", modeName), DialogButtonStyle.Primary, _ =>
            {
                _vm.MarkShown();
                _openMode?.Invoke(data.SuggestedModeId);
            }));
            spec.Buttons.Add(DialogButton.Key("quest.done.later", DialogButtonStyle.Secondary, () => _vm.MarkShown()));
            _dialog = _ctx.Dialogs.Show(spec);
        }

        public void Dispose()
        {
            if (_vm != null) _vm.Changed -= OnChanged;
            _dialog?.Close();
        }
    }
}
