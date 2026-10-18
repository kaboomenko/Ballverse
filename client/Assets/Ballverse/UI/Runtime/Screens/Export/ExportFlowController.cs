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
        BottomSheet _settingsSheet;
        bool _closingSheet;
        DialogHandle _gate;
        bool _gateWaiting;
        DialogHandle _failure;

        public ExportFlowController(UiContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _vm = ctx.ViewModels.Export;
            if (_vm != null) _vm.Changed += OnChanged;
        }

        void OnChanged()
        {
            var stage = _vm.Stage;
            bool stageChanged = stage != _shown;
            _shown = stage;

            if (stage != ExportStage.Settings) CloseSettings();
            if (stage != ExportStage.Gate && stage != ExportStage.WaitingForAd) CloseGate();
            if (stage != ExportStage.Failed) CloseFailure();

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
                case ExportStage.Idle:
                    if (_ctx.Navigator.Current == ScreenId.ExportProgress || _ctx.Navigator.Current == ScreenId.ExportDone)
                        _ctx.Navigator.Pop();
                    break;
            }
        }

        void ShowSettings()
        {
            if (_settingsSheet == null || !_settingsSheet.IsOpen)
            {
                _settingsSheet = new BottomSheet(_ctx, new SheetOptions
                {
                    TitleKey = "export.title",
                    Detents = SheetDetents.FitContent,
                    ScrimOpacity = 0.35f,
                    Closed = () =>
                    {
                        _settingsSheet = null;
                        if (!_closingSheet && _vm.Stage == ExportStage.Settings) _vm.Close();
                    },
                });
                BuildSettings(_settingsSheet.Content);
                _settingsSheet.Show();
            }
            else
            {
                BuildSettings(_settingsSheet.Content);
            }
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
                DisplayFormat.Remaining(_vm.EstimatedMs, _ctx.Localizer)));
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
            if (_settingsSheet == null) return;
            _closingSheet = true;
            _settingsSheet.Close();
            _closingSheet = false;
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
