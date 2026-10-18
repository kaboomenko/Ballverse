using System;
using Ballverse.Core;
using Ballverse.UI.Components;
using Ballverse.UI.Format;
using Ballverse.UI.Localization;
using Ballverse.UI.Navigation;
using Ballverse.UI.ViewModels;
using UnityEngine.UIElements;

namespace Ballverse.UI.Screens
{
    /// <summary>
    /// Drives the export UI from <see cref="IExportViewModel.Stage"/> (spec §3.8, §5.3.1): settings sheet → rewarded
    /// gate dialog (solid surface; "watch an ad" is the main button, "export without ads forever {price}" second,
    /// cancel) → progress screen → done screen, and the failure dialog. The view model owns the flow; this class only
    /// presents it.
    /// </summary>
    public sealed class ExportFlowController : IDisposable
    {
        readonly UiContext _ctx;
        readonly IExportViewModel _vm;
        ExportStage _shown = ExportStage.Idle;
        /// <summary>The settings sheet; a replaced sheet's late Closed callback cannot clear or close its successor.</summary>
        readonly CurrentRef<BottomSheet> _settingsSheet = new CurrentRef<BottomSheet>();
        DialogHandle _gate;
        bool _gateWaiting;
        DialogHandle _failure;
        DialogHandle _resume;

        public ExportFlowController(UiContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _vm = ctx.ViewModels.Export;
            if (_vm == null) return;
            _vm.Changed += OnChanged;
            // The resume offer is a launch-time state: present it if the view model starts there.
            if (_vm.Stage != ExportStage.Idle) OnChanged();
        }

        void OnChanged()
        {
            var stage = _vm.Stage;
            bool stageChanged = stage != _shown;
            _shown = stage;

            if (stage != ExportStage.Settings) CloseSettings();
            if (stage != ExportStage.Gate && stage != ExportStage.WaitingForAd) CloseGate();
            if (stage != ExportStage.Failed) CloseFailure();
            if (stage != ExportStage.ResumeOffer) CloseResume();

            switch (stage)
            {
                case ExportStage.Settings:
                    ShowSettings();
                    break;
                case ExportStage.Gate:
                case ExportStage.WaitingForAd:
                    ShowGate(stage == ExportStage.WaitingForAd);
                    break;
                case ExportStage.Progress:
                    if (_ctx.Navigator.Current != ScreenId.ExportProgress) _ctx.Navigator.Push(ScreenId.ExportProgress);
                    break;
                case ExportStage.Done:
                    if (_ctx.Navigator.Current == ScreenId.ExportProgress) _ctx.Navigator.Replace(ScreenId.ExportDone);
                    else if (_ctx.Navigator.Current != ScreenId.ExportDone) _ctx.Navigator.Push(ScreenId.ExportDone);
                    break;
                case ExportStage.Failed:
                    if (_ctx.Navigator.Current == ScreenId.ExportProgress) _ctx.Navigator.Pop();
                    if (stageChanged || _failure == null) ShowFailure();
                    break;
                case ExportStage.ResumeOffer:
                    if (_resume == null || !_resume.IsOpen) ShowResume();
                    break;
                case ExportStage.Idle:
                    if (_ctx.Navigator.Current == ScreenId.ExportProgress || _ctx.Navigator.Current == ScreenId.ExportDone)
                        _ctx.Navigator.Pop();
                    break;
            }
        }

        void ShowSettings()
        {
            var current = _settingsSheet.Value;
            if (current != null && current.IsActive)
            {
                BuildSettings(current.Content);
                return;
            }
            _settingsSheet.Release(current);
            BottomSheet sheet = null;
            sheet = new BottomSheet(_ctx, new SheetOptions
            {
                TitleKey = "export.title",
                Detents = SheetDetents.FitContent,
                ScrimOpacity = 0.35f,
                Closed = () =>
                {
                    // Only a sheet the user dismissed (swipe, scrim, back) is still current here; one closed by a
                    // stage change was released before its close animation.
                    if (_settingsSheet.Release(sheet) && _vm.Stage == ExportStage.Settings) _vm.Close();
                },
            });
            _settingsSheet.Set(sheet);
            BuildSettings(sheet.Content);
            sheet.Show();
        }

        void BuildSettings(VisualElement content)
        {
            content.Clear();
            var form = new FormBuilder(_ctx, content);
            var res = _vm.Resolution;
            form.Segmented("export.resolution", new[] { _ctx.L("export.res_720"), _ctx.L("export.res_1080") },
                res == ExportResolution.P720 ? 0 : 1,
                i => _vm.SetResolution(i == 0 ? ExportResolution.P720 : ExportResolution.P1080));
            var fpsOptions = _vm.FpsOptions;
            var fpsLabels = new string[fpsOptions.Count];
            int fpsIndex = 0;
            for (int i = 0; i < fpsLabels.Length; i++)
            {
                fpsLabels[i] = _ctx.Format("export.fps_value", fpsOptions[i]);
                if (fpsOptions[i] == _vm.Fps) fpsIndex = i;
            }
            form.Segmented("export.fps", fpsLabels, fpsIndex, i => _vm.SetFps(fpsOptions[i]));
            if (_vm.HasNoWatermark)
            {
                form.Toggle("export.watermark", _vm.WatermarkEnabled, v => _vm.SetWatermarkEnabled(v), "export.watermark_owner_note");
            }
            else
            {
                form.NavRow(IconKind.Sparkle, "export.watermark", _ctx.L("export.watermark_on"), () =>
                {
                    _vm.OpenWatermarkOffer();
                    ShopOfferSheet.Open(_ctx, "shop.watermark_offer.title");
                }, true);
            }
            form.Note(_ctx.Format("export.estimate", DisplayFormat.Bytes(_vm.EstimatedBytes, _ctx.Localizer),
                DisplayFormat.Duration(_vm.EstimatedMs, _ctx.Localizer)));
            if (!_vm.EnoughSpace)
            {
                form.NoteKey("export.low_space", "bv-note bv-note--warning");
                form.NavRow(IconKind.Storage, "settings.storage", null, () =>
                {
                    _vm.Close();
                    _ctx.Navigator.Push(ScreenId.Storage);
                });
            }
            var go = form.Button("export.go", IconKind.Share, () => _vm.Confirm(), "primary");
            go.SetEnabled(_vm.EnoughSpace);
        }

        void CloseSettings()
        {
            var sheet = _settingsSheet.Value;
            if (sheet == null) return;
            _settingsSheet.Release(sheet);
            sheet.Close();
        }

        void ShowGate(bool waiting)
        {
            if (_gate != null && _gate.IsOpen && _gateWaiting == waiting) return;
            CloseGate();
            _gateWaiting = waiting;
            var spec = new DialogSpec
            {
                Title = LocalizedText.FromKey("export.gate.title"),
                Body = LocalizedText.FromKey("export.gate.body"),
                Icon = IconKind.Film,
                Cancellable = true,
                Cancelled = () => _vm.CancelGate(),
            };
            if (waiting)
            {
                var row = new VisualElement();
                row.AddToClassList("bv-dialog__waiting");
                row.Add(new Spinner());
                row.Add(_ctx.KeyLabel("export.gate.loading", "bv-dialog__waiting-label"));
                spec.Extra = row;
            }
            spec.Buttons.Add(new DialogButton(LocalizedText.FromKey("export.gate.watch"), DialogButtonStyle.Primary,
                _ => _vm.WatchAdAndExport(), keepOpen: true));
            if (!string.IsNullOrEmpty(_vm.RemoveExportAdsPrice))
                spec.Buttons.Add(new DialogButton(LocalizedText.FromKey("export.gate.buy", _vm.RemoveExportAdsPrice),
                    DialogButtonStyle.Secondary, _ => _vm.BuyRemoveExportAds(), keepOpen: true));
            spec.Buttons.Add(new DialogButton(LocalizedText.FromKey("common.cancel"), DialogButtonStyle.Plain, _ => _vm.CancelGate()));
            _gate = _ctx.Dialogs.Show(spec);
            if (waiting) _gate.SetButtonsEnabled(false);
        }

        void CloseGate()
        {
            _gate?.Close();
            _gate = null;
        }

        void ShowFailure()
        {
            CloseFailure();
            var spec = new DialogSpec
            {
                Title = LocalizedText.FromKey("export.failed.title"),
                Body = _vm.FailureMessage.IsEmpty ? LocalizedText.FromKey("export.failed.body") : _vm.FailureMessage,
                Icon = IconKind.Info,
                Cancelled = () => _vm.Close(),
            };
            spec.Buttons.Add(DialogButton.Key("export.failed.retry", DialogButtonStyle.Primary, () => _vm.RetryFailed(false)));
            if (_vm.CanFallback)
                spec.Buttons.Add(DialogButton.Key("export.failed.fallback", DialogButtonStyle.Secondary, () => _vm.RetryFailed(true)));
            spec.Buttons.Add(DialogButton.Key("common.close", DialogButtonStyle.Plain, () => _vm.Close()));
            _failure = _ctx.Dialogs.Show(spec);
        }

        /// <summary>Spec §3.8 "Приложение выгружено системой": continue from the last finished segment, no new ad.</summary>
        void ShowResume()
        {
            CloseResume();
            int percent = (int)Math.Floor(Math.Max(0f, Math.Min(1f, _vm.InterruptedProgress)) * 100f);
            var spec = new DialogSpec
            {
                Title = LocalizedText.FromKey("export.resume.title"),
                Body = LocalizedText.FromKey("export.resume.body", percent),
                Icon = IconKind.Film,
                // An explicit choice: dismissing by back would silently lose the finished segments.
                Cancellable = false,
            };
            spec.Buttons.Add(DialogButton.Key("export.resume.continue", DialogButtonStyle.Primary, () => _vm.ResumeInterrupted()));
            spec.Buttons.Add(DialogButton.Key("export.resume.discard", DialogButtonStyle.Secondary, () => _vm.DiscardInterrupted()));
            _resume = _ctx.Dialogs.Show(spec);
        }

        void CloseResume()
        {
            _resume?.Close();
            _resume = null;
        }

        void CloseFailure()
        {
            _failure?.Close();
            _failure = null;
        }

        public void Dispose()
        {
            if (_vm != null) _vm.Changed -= OnChanged;
        }
    }
}
