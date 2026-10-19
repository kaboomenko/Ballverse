using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using Ballverse.UI.Localization;

namespace Ballverse.App
{
    /// <summary>
    /// Localization keys of the toasts the composition root raises. Keys marked "app.*" are not in the UI string tables yet:
    /// <see cref="AppText"/> supplies their EN / RU texts until the UI track adds them (see the App README); the others exist
    /// in <c>UI/Resources/BallverseUI/Localization</c>.
    /// </summary>
    public static class AppStrings
    {
        // Existing UI keys.
        public const string ClipSaved = "sim.clip_saved";
        public const string ExportFailed = "export.failed.body";
        public const string ExportLowSpace = "export.low_space";
        public const string ExportSaved = "export.done.saved";
        public const string PurchaseSuccess = "shop.purchase_success";
        public const string PurchasePending = "shop.pending";
        public const string RestoreDone = "shop.restore_done";
        public const string StoreUnavailable = "shop.unavailable";
        public const string CacheCleared = "storage.cache_cleared";
        public const string PresetError = "presets.error.generic";
        public const string PlatformHint = "export.done.platform_hint";

        // Keys the UI tables still need (English stand-ins in the App README).
        public const string GenerateWeak = "app.generate.weak";
        public const string GenerateFailed = "app.generate.failed";
        public const string GenerateNone = "app.generate.none";
        public const string GenerateNearestWinner = "app.generate.nearest_winner";
        public const string GenerateGoalMissed = "app.generate.goal_missed";
        /// <summary>The director goal «Победа в последнюю секунду» as a choice of the 🎲 mini sheet (spec §2.5).</summary>
        public const string GenerateStyleLastSecond = "app.generate.style.last_second";
        public const string WinnerNotFoundTitle = "app.generate.winner_not_found.title";
        public const string WinnerNotFoundBody = "app.generate.winner_not_found.body";
        public const string SearchLonger = "app.generate.search_longer";
        public const string TakeNearest = "app.generate.take_nearest";
        public const string HintRingEscapeDuration = "app.hint.m01_duration";
        public const string HintWiderGaps = "app.hint.m01_wider_gaps";
        public const string HintFewerRings = "app.hint.m01_fewer_rings";
        public const string SimStartFailed = "app.sim.start_failed";
        public const string ModeUnavailable = "app.sim.mode_unavailable";
        public const string StudioUnavailable = "app.studio.unavailable";
        public const string RunTooShort = "app.sim.run_too_short";
        public const string RecordingFailed = "app.sim.recording_failed";
        public const string OpenProjectFailed = "app.studio.open_failed";
        public const string ImportFailed = "app.studio.import_failed";
        public const string ImportUnsupported = "app.studio.import_unsupported";
        public const string ImportNoSpace = "app.studio.import_no_space";
        public const string ImportLargeFile = "app.studio.import_large";
        public const string ImportHdr = "app.studio.import_hdr";
        public const string ImportTrimmed = "app.studio.import_trimmed";
        public const string PreviewFailed = "app.studio.preview_failed";
        public const string SaveFailed = "app.studio.save_failed";
        public const string GallerySaveFailed = "app.export.gallery_failed";
        public const string GalleryPermission = "app.export.gallery_permission";
        public const string ShareFailed = "app.export.share_failed";
        public const string CaptionCopied = "app.export.caption_copied";
        public const string ResumeFailed = "app.export.resume_failed";
        public const string PurchaseFailed = "app.shop.purchase_failed";
        public const string RestoreFailed = "app.shop.restore_failed";
        public const string PrivacyFailed = "app.settings.privacy_failed";
        public const string ProjectActionFailed = "app.projects.action_failed";
        public const string ResultStatProgress = "app.result.stat.progress";
        public const string ResultStatScore = "app.result.stat.score";
        public const string GenericError = "app.error.generic";
    }

    /// <summary>
    /// One place for user-visible notices and non-fatal error reports: <see cref="Toast"/> shows a toast through the UI shell
    /// at once (main thread only — worker threads post through <see cref="AppWorkQueue"/> first), <see cref="Report"/> logs
    /// an exception to the crash reporter / log with a context and optionally shows a localized toast (spec §7.12: native and
    /// SDK failures never crash the app). Keys go through <see cref="Text"/> (EN / RU texts of keys the UI tables lack).
    /// </summary>
    public sealed class AppNotices
    {
        readonly Action<string, Exception> _report;
        Action<LocalizedText> _show;

        public AppNotices(Action<string, Exception> report)
        {
            _report = report ?? ((c, e) => { });
        }

        /// <summary>Raised for every toast (tests read it; the UI module forwards it to the shell).</summary>
        public event Action<LocalizedText> Shown;

        public void AttachUi(Action<LocalizedText> show) => _show = show;

        /// <summary>Texts of keys the UI tables do not have yet (set by the composition); null = keys only.</summary>
        public AppText Text { get; set; }

        /// <summary>The localized text of <paramref name="key"/> (see <see cref="AppText"/>).</summary>
        public LocalizedText TextOf(string key, params object[] args)
        {
            return Text != null ? Text.Of(key, args) : LocalizedText.FromKey(key, args);
        }

        public void Toast(string key, params object[] args)
        {
            Toast(TextOf(key, args));
        }

        public void Toast(LocalizedText text)
        {
            Shown?.Invoke(text);
            try
            {
                _show?.Invoke(text);
            }
            catch (Exception e)
            {
                _report("notices.toast", e);
            }
        }

        public void Report(string context, Exception exception)
        {
            _report(context, exception);
        }

        public void Report(string context, Exception exception, string toastKey)
        {
            _report(context, exception);
            if (toastKey != null) Toast(toastKey);
        }
    }

    /// <summary>
    /// Work posted from the App's worker threads (generation, file tasks) and run on the main thread at the next app tick.
    /// Lock-free queue; actions that throw are reported through the App's error sink and do not stop the others. (Services
    /// keep their own <c>Ballverse.Services.MainThreadQueue</c> for SDK callbacks, drained by the services runner; this one is
    /// drained by <see cref="AppComposition.Tick"/> first thing each frame.)
    /// </summary>
    public sealed class AppWorkQueue
    {
        readonly ConcurrentQueue<Action> _queue = new ConcurrentQueue<Action>();
        readonly Action<string, Exception> _report;

        public AppWorkQueue(Action<string, Exception> report)
        {
            _report = report ?? ((c, e) => { });
        }

        public int Pending => _queue.Count;

        public void Post(Action action)
        {
            if (action != null) _queue.Enqueue(action);
        }

        public void Drain()
        {
            int n = _queue.Count;
            while (n-- > 0 && _queue.TryDequeue(out Action a))
            {
                try
                {
                    a();
                }
                catch (Exception e)
                {
                    _report("main_thread_queue", e);
                }
            }
        }
    }

    /// <summary><see cref="IAppClock"/> on a monotonic stopwatch.</summary>
    public sealed class StopwatchClock : IAppClock
    {
        readonly Stopwatch _watch = Stopwatch.StartNew();

        public double NowSeconds => _watch.Elapsed.TotalSeconds;
    }
}
