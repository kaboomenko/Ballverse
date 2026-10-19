using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ballverse.Core;
using Ballverse.Core.Ads;
using Ballverse.Core.Modes;
using Ballverse.Core.Monetization;
using Ballverse.Core.Projects;
using Ballverse.Core.Studio;
using Ballverse.Core.Timeline;
using Ballverse.Media;
using Ballverse.Services;
using Ballverse.Studio;
using Ballverse.UI.Localization;
using Ballverse.UI.ViewModels;
using UnityEngine;

namespace Ballverse.App
{
    /// <summary>
    /// Export flow (spec §3.8, §3.10, §5.3.1): settings sheet with Core estimates and the free-space check, the rewarded gate of
    /// the services (no ad with NO_EXPORT_ADS, a retry credit or an earned credit; Р14: when no ad can be shown the export
    /// starts anyway), progress with the encoded frame, done screen with save / share and the watermark upsell, failure with
    /// 720p/30 fallback, and the "Продолжить экспорт" offer after the system unloaded the app (no second ad, RW4). The studio's
    /// preview is released while exporting. The watermark row follows Core <see cref="WatermarkRowPolicy"/> and the drawn
    /// watermark Core <see cref="WatermarkPolicy"/>. Main thread.
    /// </summary>
    public sealed class ExportViewModel : ViewModelBase, IExportViewModel
    {
        static readonly int[] s_fps = { OutputFps.Fps30, OutputFps.Fps60 };

        readonly AppContext _ctx;
        readonly StudioViewModel _studio;
        ExportStage _stage;
        ExportResolution _resolution = ExportResolution.P1080;
        int _fps = 60;
        long _estimatedBytes;
        long _estimatedMs;
        bool _enoughSpace = true;
        float _progress;
        long _remainingMs;
        bool _throttled;
        StudioExportResult _result;
        Texture _resultPreview;
        bool _savedToGallery;
        bool _showUpsell;
        LocalizedText _failure;
        bool _canFallback;
        CancellationTokenSource _cts;
        string _projectId;
        string _gateProjectId;
        bool _gateApproved;
        ExportResumeState _resumable;
        float _interrupted;

        public ExportViewModel(AppContext ctx, StudioViewModel studio)
            : base(ctx.Notices.Report)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
            _studio = studio ?? throw new ArgumentNullException(nameof(studio));
            _ctx.Studio.ExportProgress += OnProgress;
            _ctx.Services.Purchases.EntitlementsChanged += _ => Notify();
            _ctx.Services.Purchases.ProductsChanged += Notify;
        }

        /// <summary>The gate the studio's export pipeline calls (see <see cref="RewardedGateBridge"/>).</summary>
        public IExportGate Gate { get; internal set; }

        /// <summary>An export finished successfully (quest step, projects refresh).</summary>
        public event Action<StudioExportResult> Exported;

        /// <summary>The user asked for the watermark offer (the UI opens the shop sheet).</summary>
        public event Action WatermarkOfferRequested;

        public ExportStage Stage => _stage;
        public ExportResolution Resolution => _resolution;
        public int Fps => _fps;
        public IReadOnlyList<int> FpsOptions => s_fps;
        public bool HasNoWatermark => _ctx.Services.Entitlements.Contains(Entitlement.NoWatermark);

        public bool WatermarkEnabled
        {
            get
            {
                ProjectDocument doc = _studio.Doc;
                bool toggle = doc != null ? doc.Output.Watermark : _ctx.Settings.Data.KeepWatermark;
                return WatermarkPolicy.Resolve(toggle, HasNoWatermark, _ctx.Remote.WatermarkEnabled);
            }
        }

        /// <summary>Core row presentation (Hidden when the kill switch is off, Locked without NO_WATERMARK, Toggle otherwise).</summary>
        public WatermarkRow WatermarkRow => WatermarkRowPolicy.GetRow(_ctx.Services.Entitlements, _ctx.Remote.WatermarkEnabled);

        public long EstimatedBytes => _estimatedBytes;
        public long EstimatedMs => _estimatedMs;
        public bool EnoughSpace => _enoughSpace;

        public string RemoveExportAdsPrice
        {
            get
            {
                IPurchaseService p = _ctx.Services.Purchases;
                if (p.State != PurchaseServiceState.Ready || !p.Config.IsSellable(ProductIds.RemoveExportAds)) return null;
                return p.TryGetProduct(ProductIds.RemoveExportAds, out StoreProduct product) && product.AvailableToPurchase ? product.LocalizedPrice : null;
            }
        }

        public float Progress => _progress;
        public long RemainingMs => _remainingMs;
        public Texture ProgressPreview => _ctx.Studio.ExportPreviewTexture;
        public bool Throttled => _throttled;
        public Texture ResultPreview => _resultPreview;
        public bool SavedToGallery => _savedToGallery;
        public bool ShowWatermarkUpsell => _showUpsell;
        public float InterruptedProgress => _interrupted;
        public LocalizedText FailureMessage => _failure;
        public bool CanFallback => _canFallback;

        /// <summary>The last finished export (Done stage), or null.</summary>
        public StudioExportResult LastResult => _result;

        // ------------------------------------------------------------------ settings

        public void Begin()
        {
            if (_stage == ExportStage.Progress || _stage == ExportStage.External || _stage == ExportStage.WaitingForAd) return;
            ProjectDocument doc = _studio.Doc;
            if (doc == null)
            {
                _ctx.Notices.Toast(AppStrings.OpenProjectFailed);
                return;
            }
            _projectId = doc.Id;
            ExportSettings defaults = _ctx.Studio.ResolveExportSettings(doc, new ExportRequest { Bitrates = _ctx.Remote.Bitrates });
            _resolution = defaults.Resolution;
            _fps = defaults.Fps;
            Estimate();
            SetStage(ExportStage.Settings);
        }

        public void SetResolution(ExportResolution resolution)
        {
            _resolution = resolution;
            Estimate();
            Notify();
        }

        public void SetFps(int fps)
        {
            if (!OutputFps.IsValid(fps)) return;
            _fps = fps;
            Estimate();
            Notify();
        }

        public void SetWatermarkEnabled(bool enabled)
        {
            if (!HasNoWatermark) return;
            _studio.Edit(d => d.Output.Watermark = enabled, null);
            _ctx.Settings.Update(d => d.KeepWatermark = enabled);
            _studio.UpdateOverlays();
            Notify();
        }

        public void OpenWatermarkOffer()
        {
            WatermarkOfferRequested?.Invoke();
        }

        void Estimate()
        {
            ProjectDocument doc = _studio.Doc;
            if (doc == null) return;
            try
            {
                ExportSettings settings = _ctx.Studio.ResolveExportSettings(doc, Request());
                StudioTimeline timeline = ProjectComposition.BuildTimeline(doc, settings.Fps, AvailableTicks(doc));
                var plan = new ExportPlan(timeline, settings);
                _estimatedBytes = plan.EstimateFileSizeBytes();
                ModeCode mode = ModeCatalog.TryGetById(doc.Clip.ModeId, out ModeInfo info) ? info.Code : ModeCode.M01;
                _estimatedMs = plan.EstimateExportTimeMs(_ctx.Tier, mode);
                long free = FreeSpace();
                _enoughSpace = ExportPolicy.Preflight(plan, free).Ok;
            }
            catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
            {
                _ctx.Notices.Report("export.estimate", e);
                _enoughSpace = true;
            }
        }

        int AvailableTicks(ProjectDocument doc)
        {
            StudioTimelineData tl = _studio.Timeline;
            int ticks = StudioViewModel.UsToTicks(tl.Sim.SourceUs);
            return ticks > 0 ? ticks : Math.Max(1, doc.Clip.TrimOutTick);
        }

        long FreeSpace()
        {
            try
            {
                return _ctx.Media.Signals?.FreeSpaceBytes ?? -1;
            }
            catch (Exception e) when (e is MediaException || e is InvalidOperationException)
            {
                _ctx.Notices.Report("export.free_space", e);
                return -1;
            }
        }

        ExportRequest Request()
        {
            ProjectDocument doc = _studio.Doc;
            bool toggle = doc != null ? doc.Output.Watermark : _ctx.Settings.Data.KeepWatermark;
            return new ExportRequest
            {
                Resolution = _resolution,
                Fps = _fps,
                ShowWatermark = WatermarkPolicy.Resolve(toggle, HasNoWatermark, _ctx.Remote.WatermarkEnabled),
                PlatformMasks = _ctx.Remote.MaskUnion,
                Bitrates = _ctx.Remote.Bitrates,
                SaveToGallery = _ctx.Settings.Data.AutoSaveToGallery,
            };
        }

        // ------------------------------------------------------------------ gate

        /// <summary>«Экспорт» in the settings sheet: the rewarded gate decides, then the export runs.</summary>
        public void Confirm()
        {
            if (_stage != ExportStage.Settings || _projectId == null) return;
            if (!_enoughSpace)
            {
                _ctx.Notices.Toast(AppStrings.ExportLowSpace);
                return;
            }
            ExportGateDecision decision;
            try
            {
                decision = _ctx.Services.RewardedExport.RequestExport(_projectId);
            }
            catch (Exception e)
            {
                // Р14: a broken gate never blocks the export.
                _ctx.Notices.Report("export.gate", e);
                StartExport(true);
                return;
            }
            if (!decision.IsShowOffer)
            {
                StartExport(true);
                return;
            }
            if (_ctx.Services.Ads.State == AdsServiceState.Unavailable)
            {
                // No ads in this build / region: run the gate's "ad unavailable" path right away (Р14), no dialog.
                WatchAdAndExport();
                return;
            }
            SetStage(ExportStage.Gate);
        }

        public void WatchAdAndExport()
        {
            if (_projectId == null || _stage == ExportStage.WaitingForAd || _stage == ExportStage.External) return;
            SetStage(ExportStage.WaitingForAd);
            string projectId = _projectId;
            try
            {
                _ctx.Services.RewardedExport.WatchAd(projectId, step => OnGateStep(projectId, step));
                if (_stage == ExportStage.WaitingForAd && !_ctx.Services.RewardedExport.IsWaitingForAd && _ctx.Services.RewardedExport.IsBusy)
                    SetStage(ExportStage.External);
            }
            catch (Exception e)
            {
                _ctx.Notices.Report("export.watch_ad", e);
                StartExport(true);
            }
        }

        void OnGateStep(string projectId, RewardedGateStep step)
        {
            if (projectId != _projectId) return;
            switch (step.Kind)
            {
                case RewardedGateStepKind.StartExport:
                    StartExport(true);
                    break;
                case RewardedGateStepKind.ShowOfferAgain:
                    SetStage(ExportStage.Gate);
                    break;
                case RewardedGateStepKind.StartPurchase:
                    BuyRemoveExportAds();
                    break;
                default:
                    SetStage(ExportStage.Idle);
                    break;
            }
        }

        public void BuyRemoveExportAds()
        {
            if (_projectId == null) return;
            string projectId = _projectId;
            try
            {
                if (_stage == ExportStage.Gate) _ctx.Services.RewardedExport.Decline(projectId, true);
            }
            catch (Exception e)
            {
                _ctx.Notices.Report("export.decline", e);
            }
            SetStage(ExportStage.External);
            if (_ctx.Ads != null) _ctx.Ads.InPurchaseFlow = true;
            try
            {
                _ctx.Services.Purchases.Purchase(ProductIds.RemoveExportAds, "rewarded_gate", outcome =>
                {
                    if (_ctx.Ads != null) _ctx.Ads.InPurchaseFlow = false;
                    if (projectId != _projectId) return;
                    if (outcome.Kind == PurchaseResultKind.Purchased || outcome.Kind == PurchaseResultKind.AlreadyOwned)
                    {
                        _ctx.Notices.Toast(AppStrings.PurchaseSuccess);
                        SetStage(ExportStage.Settings);
                        Confirm();
                        return;
                    }
                    if (outcome.Kind == PurchaseResultKind.Pending) _ctx.Notices.Toast(AppStrings.PurchasePending);
                    else if (outcome.Kind == PurchaseResultKind.Failed || outcome.Kind == PurchaseResultKind.NotAvailable) _ctx.Notices.Toast(AppStrings.PurchaseFailed);
                    // Back to the offer: a fresh decision opens it again.
                    SetStage(ExportStage.Settings);
                    Confirm();
                });
            }
            catch (Exception e)
            {
                if (_ctx.Ads != null) _ctx.Ads.InPurchaseFlow = false;
                _ctx.Notices.Report("export.purchase", e, AppStrings.PurchaseFailed);
                SetStage(ExportStage.Gate);
            }
        }

        public void CancelGate()
        {
            if (_projectId != null && (_stage == ExportStage.Gate || _stage == ExportStage.WaitingForAd))
            {
                try
                {
                    _ctx.Services.RewardedExport.Decline(_projectId, false);
                }
                catch (Exception e)
                {
                    _ctx.Notices.Report("export.decline", e);
                }
            }
            SetStage(ExportStage.Settings);
        }

        // ------------------------------------------------------------------ export run

        void StartExport(bool gatePassed)
        {
            if (_projectId == null) return;
            _gateProjectId = _projectId;
            _gateApproved = gatePassed;
            _progress = 0f;
            _remainingMs = 0;
            _throttled = false;
            _failure = default;
            _canFallback = false;
            _studio.SuspendPreview();
            if (_ctx.Ads != null) _ctx.Ads.Exporting = true;
            SetStage(ExportStage.Progress);
            _cts = new CancellationTokenSource();
            Run(_ctx.Studio.ExportAsync(_projectId, Request(), _cts.Token));
        }

        async void Run(Task<StudioExportResult> task)
        {
            StudioExportResult result;
            try
            {
                result = await task;
            }
            catch (OperationCanceledException)
            {
                result = StudioExportResult.Failure(ExportErrorCode.Cancelled, "cancelled", _projectId, null);
            }
            catch (Exception e)
            {
                _ctx.Notices.Report("export.run", e);
                result = StudioExportResult.Failure(ExportErrorCode.Unknown, e.Message, _projectId, null);
            }
            Finish(result);
        }

        void Finish(StudioExportResult result)
        {
            _cts?.Dispose();
            _cts = null;
            _gateApproved = false;
            _gateProjectId = null;
            _result = result;
            if (result.Succeeded)
            {
                _savedToGallery = result.Gallery == ExportGalleryStatus.Saved;
                if (result.Gallery == ExportGalleryStatus.PermissionDenied) _ctx.Notices.Toast(AppStrings.GalleryPermission);
                else if (result.Gallery == ExportGalleryStatus.Failed) _ctx.Notices.Toast(AppStrings.GallerySaveFailed);
                int count = _ctx.Settings.Data.ExportCount + 1;
                _ctx.Settings.Update(d =>
                {
                    d.ExportCount = count;
                    d.QuestExported = true;
                });
                // Spec §5.2: not in the first 2 exports, then at most every third, only while the watermark is drawn.
                _showUpsell = !HasNoWatermark && _ctx.Remote.WatermarkEnabled && count > 2 && count % 3 == 0;
                LoadResultPreview(result.ProjectId);
                _studio.ResumePreview();
                SetStage(ExportStage.Done);
                Exported?.Invoke(result);
                return;
            }
            if (_ctx.Ads != null) _ctx.Ads.Exporting = false;
            _studio.ResumePreview();
            switch (result.Error)
            {
                case ExportErrorCode.Cancelled:
                    SetStage(ExportStage.Idle);
                    return;
                case ExportErrorCode.GateDeclined:
                    SetStage(ExportStage.Settings);
                    return;
                case ExportErrorCode.InsufficientSpace:
                    _failure = _ctx.Notices.TextOf(AppStrings.ExportLowSpace);
                    break;
                default:
                    _failure = _ctx.Notices.TextOf(AppStrings.ExportFailed);
                    break;
            }
            _canFallback = result.SuggestLowerSettings;
            _ctx.Notices.Report("export.failed", new IOException("export " + result.Error + ": " + result.Message));
            SetStage(ExportStage.Failed);
        }

        void LoadResultPreview(string projectId)
        {
            ReleaseResultPreview();
            if (projectId == null) return;
            try
            {
                string cover = _ctx.Studio.RenderCover(projectId);
                if (cover != null) _resultPreview = _ctx.Textures.Load(cover);
            }
            catch (Exception e) when (e is IOException || e is InvalidOperationException || e is UnauthorizedAccessException)
            {
                _ctx.Notices.Report("export.cover", e);
            }
        }

        void ReleaseResultPreview()
        {
            if (_resultPreview == null) return;
            _ctx.Textures.Release(_resultPreview);
            _resultPreview = null;
        }

        void OnProgress(ExportProgressInfo info)
        {
            _progress = (float)Math.Max(0.0, Math.Min(1.0, info.Fraction));
            _remainingMs = info.RemainingMs ?? 0;
            _throttled = info.Throttled;
            if (_stage == ExportStage.Progress) Notify();
        }

        public void CancelExport()
        {
            _cts?.Cancel();
        }

        public void SaveToGallery()
        {
            if (_result == null || _result.OutputPath == null || _savedToGallery) return;
            SaveAsync(_result.OutputPath);
        }

        async void SaveAsync(string path)
        {
            try
            {
                GallerySaveResult r = await _ctx.Media.SaveToGalleryAsync(path, "Ballverse", CancellationToken.None);
                if (r.IsSaved)
                {
                    _savedToGallery = true;
                    _ctx.Notices.Toast(AppStrings.ExportSaved);
                }
                else if (r.Status == GallerySaveStatus.PermissionDenied) _ctx.Notices.Toast(AppStrings.GalleryPermission);
                else _ctx.Notices.Report("export.gallery", new IOException(r.ErrorMessage), AppStrings.GallerySaveFailed);
                Notify();
            }
            catch (Exception e) when (e is MediaException || e is IOException || e is InvalidOperationException)
            {
                _ctx.Notices.Report("export.gallery", e, AppStrings.GallerySaveFailed);
            }
        }

        public void Share(SharePlatform platform)
        {
            if (_result == null || _result.OutputPath == null) return;
            string caption = Caption(_studio.Doc?.Clip.ModeId ?? CaptionMode());
            try
            {
                if (platform != SharePlatform.System && _ctx.Settings.Data.CopyCaptionOnShare)
                {
                    _ctx.Platform.CopyToClipboard(caption);
                    _ctx.Notices.Toast(AppStrings.CaptionCopied);
                }
                _ctx.Media.Share(_result.OutputPath, caption);
            }
            catch (Exception e) when (e is MediaException || e is InvalidOperationException || e is IOException)
            {
                _ctx.Notices.Report("export.share", e, AppStrings.ShareFailed);
            }
        }

        string CaptionMode()
        {
            return _ctx.Settings.Data.LastModeId ?? "";
        }

        /// <summary>Caption with hashtags (spec §3.11): #ballverse #satisfying + the mode's tag.</summary>
        public static string Caption(string modeId)
        {
            string tag = string.IsNullOrEmpty(modeId) ? "" : " #" + modeId.Replace("_", "");
            return "#ballverse #satisfying" + tag;
        }

        public void RetryFailed(bool fallback720p30)
        {
            if (_projectId == null) return;
            if (fallback720p30)
            {
                _resolution = ExportResolution.P720;
                _fps = OutputFps.Fps30;
            }
            Estimate();
            SetStage(ExportStage.Settings);
            Confirm();
        }

        public void Close()
        {
            if (_stage == ExportStage.Progress) return;
            if (_ctx.Ads != null) _ctx.Ads.Exporting = false;
            ReleaseResultPreview();
            SetStage(ExportStage.Idle);
        }

        /// <summary>
        /// «Новое видео» on the export result. Not a natural break: IN3 forbids an interstitial right after an export, so a
        /// pending one waits for the next real break (result card, back, exit to the gallery).
        /// </summary>
        public void NewVideo()
        {
            Close();
        }

        public void GoToProjects()
        {
            Close();
        }

        // ------------------------------------------------------------------ resume (spec §3.8 RW4)

        /// <summary>At launch: offers to continue an export the system interrupted.</summary>
        public void CheckResumable()
        {
            IReadOnlyList<ExportResumeState> list;
            try
            {
                list = _ctx.Studio.ListResumable();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException)
            {
                _ctx.Notices.Report("export.list_resumable", e);
                return;
            }
            if (list == null || list.Count == 0 || _stage != ExportStage.Idle) return;
            _resumable = list[0];
            int done = 0;
            foreach (ExportSegmentRecord s in _resumable.Segments) done += s.FrameCount;
            _interrupted = _resumable.FrameCount > 0 ? Math.Min(1f, done / (float)_resumable.FrameCount) : 0f;
            _projectId = _resumable.ProjectId;
            SetStage(ExportStage.ResumeOffer);
        }

        public void ResumeInterrupted()
        {
            if (_resumable == null) return;
            string jobId = _resumable.JobId;
            _resumable = null;
            _progress = _interrupted;
            if (_ctx.Ads != null) _ctx.Ads.Exporting = true;
            SetStage(ExportStage.Progress);
            _cts = new CancellationTokenSource();
            Run(ResumeAsync(jobId, _cts.Token));
        }

        async Task<StudioExportResult> ResumeAsync(string jobId, CancellationToken ct)
        {
            try
            {
                return await _ctx.Studio.ResumeExportAsync(jobId, ct);
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is InvalidOperationException)
            {
                _ctx.Notices.Report("export.resume", e, AppStrings.ResumeFailed);
                return StudioExportResult.Failure(ExportErrorCode.ResumeInvalid, e.Message, _projectId, jobId);
            }
        }

        public void DiscardInterrupted()
        {
            if (_resumable != null)
            {
                try
                {
                    _ctx.Studio.DiscardResumable(_resumable.JobId);
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException)
                {
                    _ctx.Notices.Report("export.discard", e);
                }
            }
            _resumable = null;
            SetStage(ExportStage.Idle);
        }

        void SetStage(ExportStage stage)
        {
            _stage = stage;
            Notify();
        }

        // ------------------------------------------------------------------ gate seam for the pipeline

        /// <summary>
        /// The pipeline asks the gate once per new export. The decision was already taken before the export started
        /// (<see cref="Confirm"/>), so this approves exactly the export it started.
        /// </summary>
        internal bool IsApproved(string projectId) => _gateApproved && projectId == _gateProjectId;

        internal void ReportFinished(string projectId, StudioExportResult result)
        {
            ExportResult r = result.Succeeded ? ExportResult.Succeeded
                : result.Error == ExportErrorCode.Cancelled ? ExportResult.Cancelled : ExportResult.Failed;
            try
            {
                _ctx.Services.RewardedExport.OnExportFinished(projectId, r);
            }
            catch (Exception e)
            {
                _ctx.Notices.Report("export.gate_finished", e);
            }
        }
    }

    /// <summary>
    /// Studio <see cref="IExportGate"/> connected to the services' rewarded gate through <see cref="ExportViewModel"/>: the
    /// decision (ad / no ad / purchase) happens in the export sheet before the export starts, the pipeline's request approves
    /// the export that sheet started, and every finished run is reported for the retry credit (RW4) and credit use (RW5).
    /// </summary>
    public sealed class RewardedGateBridge : IExportGate
    {
        ExportViewModel _export;

        public void Attach(ExportViewModel export)
        {
            _export = export;
            if (export != null) export.Gate = this;
        }

        public Task<bool> RequestAsync(ExportGateRequest request, CancellationToken ct)
        {
            // Not attached (headless use): never block an export (Р14).
            bool ok = _export == null || _export.IsApproved(request.ProjectId);
            return Task.FromResult(ok);
        }

        public void OnExportFinished(string projectId, StudioExportResult result)
        {
            _export?.ReportFinished(projectId, result);
        }
    }
}
