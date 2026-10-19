using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ballverse.Core;
using Ballverse.Core.Layout;
using Ballverse.Core.Projects;
using Ballverse.Core.Studio;
using Ballverse.Core.Timeline;
using Ballverse.Media;
using UnityEngine;

namespace Ballverse.Studio
{
    /// <summary>
    /// The studio preview (spec §3.7): plays a project in real time on <see cref="Target"/> (a 9:16 render texture for the UI) with
    /// the same frame renderer and compositor as the export. The simulation comes from the clip's render stream (random access,
    /// scrubbing within one decoded 1-second block), the user video from a realtime decoder (frame by pts; late frames are dropped:
    /// the last decoded frame stays until the requested one arrives), the sound from the offline mix played through
    /// <see cref="PreviewAudioOutput"/>, which is also the master clock while it runs (Core <see cref="PreviewAudioSync"/>: the
    /// picture is held during the DSP latency after a start and runs on the frame clock when the audio thread stops). Edits are
    /// applied with <see cref="SetProject"/> (≤ 1 frame: geometry is recomposed, the video is not re-decoded; audio edits remix
    /// after <see cref="RemixDebounceUs"/> of quiet, one mix at a time). When frames are missed the internal
    /// resolution drops, never the frame rate (<see cref="PreviewQualityGovernor"/>). Main thread; driven by
    /// <see cref="StudioRunner.Tick"/>; dispose to release the decoder, the clip and the render textures.
    /// </summary>
    public sealed class StudioPreviewPlayer : IDisposable
    {
        /// <summary>An audio edit remixes after this long without further audio edits (a volume drag remixes once), µs.</summary>
        public const long RemixDebounceUs = 300_000;

        readonly StudioServices _services;
        readonly StudioFrameRenderer _frames;
        readonly VideoTextureUploader _video = new VideoTextureUploader();
        readonly PreviewAudioOutput _audio;
        readonly string _projectDir;
        ClipSource _clip;
        ProjectDocument _doc;
        StudioTimeline _timeline;
        StudioFramePlans _plans;
        PlaybackClock _clock;
        PreviewQualityGovernor _governor;
        IVideoReader _reader;
        IntVec2 _readerBox;
        Task<OpenedReader> _readerOpening;
        IntVec2 _openingBox;
        IntVec2 _displaySize;
        CancellationTokenSource _readerCts;
        CancellationTokenSource _mixCts;
        PreviewAudioSync _sync;
        long _nowUs;
        long _remixDueUs = -1;
        string _videoPath;
        string _audioKey;
        IntVec2 _videoSize;
        IntVec2 _baseCanvas = new IntVec2(540, 960);
        IntVec2 _canvas;
        RenderTexture _target;
        int _exportFps = OutputFps.Fps60;
        bool _dirty = true;
        long _lastDrawnUs = -1;
        long _lastVideoPts = long.MinValue;
        long _deliveredRequestUs = long.MinValue;
        bool _disposed;
        bool _showWatermark = true;
        IReadOnlyList<FloatRect> _masks;
        IReadOnlyList<FloatRect> _textBounds = Array.Empty<FloatRect>();

        internal StudioPreviewPlayer(StudioServices services, ClipSource clip, string projectDir, ProjectDocument doc, int exportFps, DeviceTier tier)
        {
            _services = services;
            _clip = clip;
            _projectDir = projectDir;
            _frames = new StudioFrameRenderer(services.Compositor, services.CreateSimRenderer(), StudioRenderQuality.Preview, 2048, ownsSimRenderer: true);
            _frames.OpenClip(clip);
            _audio = PreviewAudioOutput.Create(services.Runner.transform);
            _exportFps = OutputFps.IsValid(exportFps) ? exportFps : OutputFps.Fps60;
            RebuildSync();
            _audio.ConfigurationChanged += OnAudioConfigurationChanged;
            _governor = new PreviewQualityGovernor(PreviewPolicy.TargetFps(_exportFps), PreviewPolicy.ShouldReduceInternalResolution(tier, ExportResolution.P1080, _exportFps));
            SetProject(doc);
            services.Runner.Tick += OnTick;
        }

        /// <summary>The composed preview frame (9:16). Recreated when the size changes: listen to <see cref="TargetChanged"/>.</summary>
        public RenderTexture Target => _target;

        /// <summary>Raised when <see cref="Target"/> was recreated (UI: <c>Background.FromRenderTexture</c>).</summary>
        public event Action<RenderTexture> TargetChanged;

        /// <summary>Raised after each drawn frame with the output time, µs.</summary>
        public event Action<long> FrameDrawn;

        public ProjectDocument Project => _doc;
        public StudioTimeline Timeline => _timeline;
        public long DurationUs => _clock.DurationUs;
        public long PositionUs => _clock.PositionUs;
        public bool IsPlaying => _clock.IsPlaying;
        public bool Loop { get => _clock.Loop; set => _clock.Loop = value; }

        /// <summary>Spec §3.7 "Полноэкранный предпросмотр" (the UI shows <see cref="Target"/> full screen; the canvas follows <see cref="SetDisplaySize"/>).</summary>
        public bool FullScreen { get; set; }

        /// <summary>Spec §3.3 "Зоны интерфейса": tint the platform UI masks (never in the export).</summary>
        public bool ShowPlatformMasks
        {
            get => _showMasks;
            set
            {
                _showMasks = value;
                _dirty = true;
            }
        }
        bool _showMasks;

        /// <summary>What drives the clock now (audio, held picture, or the frame clock when the audio output does not run).</summary>
        public PreviewClockSource ClockSource => _sync.Source;

        /// <summary>True while the offline mix is being (re)built or waits for the edit debounce.</summary>
        public bool AudioPending { get; private set; }

        /// <summary>Last audio mix error (the preview then plays silent).</summary>
        public Exception AudioError { get; private set; }

        /// <summary>Last user-video decoder error (the slot shows a placeholder).</summary>
        public Exception VideoError { get; private set; }

        /// <summary>HUD values of the shown frame.</summary>
        public Ballverse.Core.RenderStream.RenderFrame CurrentFrame => _frames.LastFrame;

        /// <summary>Internal resolution scale (1 = full).</summary>
        public float QualityScale => _governor.Scale;

        /// <summary>
        /// Watermark and masks of the preview: the App passes <see cref="WatermarkPolicy.Resolve"/> for the current entitlement and the
        /// platform masks from Remote Config (null = built-in defaults).
        /// </summary>
        public void SetOverlays(bool showWatermark, IReadOnlyList<FloatRect> platformMasks)
        {
            _showWatermark = showWatermark;
            _masks = platformMasks;
            RebuildPlans();
        }

        /// <summary>Pixel size of the preview area; the canvas is the largest 9:16 inside it, at the current quality scale.</summary>
        public void SetDisplaySize(int width, int height)
        {
            if (width < OutputCanvas.MinSide || height < OutputCanvas.MinSide) return;
            IntVec2 fit = OutputCanvas.FitPreview(width, height);
            // Never draw more pixels than the export frame.
            if (fit.Y > 1920) fit = new IntVec2(1080, 1920);
            if (fit.X == _baseCanvas.X && fit.Y == _baseCanvas.Y) return;
            _baseCanvas = fit;
            RebuildPlans();
        }

        /// <summary>Export fps the preview plays at (spec §3.7: preview fps = export fps).</summary>
        public void SetExportFps(int fps)
        {
            if (!OutputFps.IsValid(fps) || fps == _exportFps) return;
            _exportFps = fps;
            RebuildSync();
            SetProject(_doc);
        }

        /// <summary>Applies an edited project (from <see cref="ProjectEditSession.Document"/>).</summary>
        public void SetProject(ProjectDocument doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            _doc = doc;
            _timeline = ProjectComposition.BuildTimeline(doc, _exportFps, Math.Max(1, _clip.TickCount));
            if (_clock == null)
            {
                _clock = new PlaybackClock(_timeline.DurationUs, _exportFps);
            }
            else
            {
                _clock.SetDuration(_timeline.DurationUs, _exportFps);
            }
            CompositeStyle style = CompositeStyle.FromProject(doc, _services.ThemeBackground, _services.WatermarkTexture);
            _frames.SetProject(doc, style);
            _textBounds = _services.Compositor.Text.MeasureBounds(doc.Texts);
            EnsureVideoReader();
            RebuildPlans();
            RemixIfNeeded();
            _dirty = true;
        }

        public void Play()
        {
            _clock.Play();
            StartAudioAtClock();
            _dirty = true;
        }

        public void Pause()
        {
            _clock.Pause();
            _audio.Pause();
        }

        /// <summary>Scrub / jump to an output time (spec §3.7: frame within ≤ 100 ms).</summary>
        public void Seek(long outputUs)
        {
            _clock.Seek(outputUs);
            _audio.Seek(_clock.PositionUs);
            if (_clock.IsPlaying && AudioAvailable) _sync.Start(_clock.PositionUs, _audio.CursorUs);
            _frames.ResetHistory();
            _dirty = true;
        }

        bool AudioAvailable => _audio.HasBuffer && !AudioPending;

        /// <summary>Starts the mix at the clock position (play, a new mix while playing, audio resync) and holds the picture for the latency.</summary>
        void StartAudioAtClock()
        {
            if (_clock == null || _sync == null || !_clock.IsPlaying || !AudioAvailable) return;
            _audio.Play(_clock.PositionUs);
            _sync.Start(_clock.PositionUs, _audio.CursorUs);
        }

        void RebuildSync()
        {
            _sync = PreviewAudioSync.ForOutput(_exportFps, _audio.BlockUs, _audio.LatencyUs);
            StartAudioAtClock();
        }

        void OnAudioConfigurationChanged()
        {
            if (!_disposed && _clock != null) RebuildSync();
        }

        void OnTick(float dt)
        {
            if (_disposed) return;
            long dtUs = (long)(Math.Max(0f, dt) * 1e6f);
            _nowUs += dtUs;
            if (_remixDueUs >= 0 && _nowUs >= _remixDueUs)
            {
                _remixDueUs = -1;
                StartRemix();
            }
            bool wasPlaying = _clock.IsPlaying;
            long audioEndUs = _audio.Cursor.FrameCount * 1000000L / Ballverse.Core.Audio.AudioConstants.SampleRate;
            _sync.Advance(_clock, dtUs, AudioAvailable, _audio.CursorUs, _audio.LatencyUs, audioEndUs);
            // The audio output runs again, or the clock wrapped (Loop): continue the mix at the picture.
            if (_sync.NeedsAudioResync) StartAudioAtClock();
            if (wasPlaying && !_clock.IsPlaying) _audio.Pause();
            if (wasPlaying && _governor.OnFrame(dtUs)) RebuildPlans();
            PollReader();
            Draw();
        }

        void Draw()
        {
            if (_plans == null || _target == null) return;
            long t = _clock.PositionUs;
            int frameIndex = _timeline.FrameAtTimeUs(t);
            long frameUs = _timeline.FrameTimeUs(frameIndex);
            SimSample sim = _timeline.SampleSim(frameIndex);
            VideoSample vs = _timeline.SampleVideo(frameIndex);
            FramePlan plan = _plans.For(vs);
            SlotPicture video = VideoPicture(plan, vs, frameIndex);
            bool videoChanged = _video.PtsUs != _lastVideoPts;
            if (!_dirty && frameUs == _lastDrawnUs && !videoChanged) return;
            IReadOnlyList<IntRect> masks = _showMasks ? _plans.Request.Masks.Rects : null;
            _frames.Render(_target, plan, sim, frameUs, video, _clock.Discontinuity, masks);
            _lastDrawnUs = frameUs;
            _lastVideoPts = _video.PtsUs;
            _dirty = false;
            FrameDrawn?.Invoke(frameUs);
        }

        SlotPicture VideoPicture(FramePlan plan, VideoSample vs, int frameIndex)
        {
            if (!StudioFramePlans.DrawsVideoSlot(plan, vs)) return SlotPicture.Empty;
            if (vs.Kind == VideoSampleKind.Black) return SlotPicture.Black;
            if (vs.Kind == VideoSampleKind.NoVideo) return SlotPicture.Empty;
            if (_reader != null && _reader.IsOpen)
            {
                try
                {
                    // The frame for this time is already on the texture: do not ask the decoder again (paused preview).
                    bool shown = _video.HasFrame && _deliveredRequestUs == vs.SourcePtsUs;
                    if (!shown && _reader.TryReadFrameAt(vs.SourcePtsUs, out VideoFrame frame))
                    {
                        using (frame)
                        {
                            _video.Upload(frame);
                            _deliveredRequestUs = frame.RequestedPtsUs;
                        }
                    }
                    if (_clock.IsPlaying && vs.Kind == VideoSampleKind.Frame && frameIndex + 1 < _timeline.FrameCount)
                    {
                        VideoSample next = _timeline.SampleVideo(frameIndex + 1);
                        if (next.Kind == VideoSampleKind.Frame) _reader.Prefetch(next.SourcePtsUs);
                    }
                }
                catch (MediaException e)
                {
                    VideoError = e;
                    CloseReader();
                }
            }
            if (!_video.HasFrame || !plan.HasVideoMapping) return SlotPicture.Empty;
            FitBackground bg = _doc.UserVideo?.FitBackground;
            return new SlotPicture
            {
                Kind = SlotPictureKind.Picture,
                Texture = _video.Texture,
                BottomUp = false,
                Mapping = plan.VideoMapping,
                Content = ProjectComposition.BuildVideoContent(_doc.UserVideo),
                Background = bg?.Kind ?? FitBackgroundKind.Blur,
                BackgroundColor = StudioColor.Parse(bg?.Color, StudioColor.Black),
            };
        }

        void RebuildPlans()
        {
            if (_doc == null) return;
            IntVec2 canvas = _governor.ScaleCanvas(_baseCanvas);
            if (_target == null || canvas.X != _canvas.X || canvas.Y != _canvas.Y)
            {
                _canvas = canvas;
                RenderTexture old = _target;
                _target = StudioCompositor.CreateCanvas(canvas.X, canvas.Y, "StudioPreview");
                TargetChanged?.Invoke(_target);
                StudioUnity.Release(old);
            }
            FrameRequest request = ProjectComposition.BuildFrameRequest(_doc, _canvas.X, _canvas.Y, _videoSize, _showWatermark, _masks, _textBounds,
                StudioCompositor.LogoAspect(_frames.Style));
            _plans = StudioFramePlans.Compose(request);
            _dirty = true;
        }

        /// <summary>A reader with the box it was opened for and the source's display size.</summary>
        sealed class OpenedReader
        {
            public IVideoReader Reader;
            public IntVec2 Box;
            public IntVec2 DisplaySize;
        }

        /// <summary>
        /// Keeps a realtime reader for the user video. Only a new file reopens it from scratch; a zoom that needs more decoded pixels
        /// than the open reader gives (<see cref="VideoDecodeSizing.PreviewBox"/>, zoom rounded up to a power of two) opens a larger
        /// one in the background while the current one keeps drawing, and swaps on completion — the slot never goes blank, and
        /// pinching back out never reopens.
        /// </summary>
        void EnsureVideoReader()
        {
            UserVideoSettings uv = _doc.UserVideo;
            string path = uv != null && _projectDir != null ? Path.Combine(_projectDir, uv.File.Replace('/', Path.DirectorySeparatorChar)) : null;
            if (path != _videoPath)
            {
                _videoPath = path;
                CloseReader();
                _video.Invalidate();
                _deliveredRequestUs = long.MinValue;
                _videoSize = default(IntVec2);
                _readerBox = default(IntVec2);
                _displaySize = default(IntVec2);
                if (path == null || !File.Exists(path)) return;
                StartOpening(path, ProjectComposition.BuildVideoContent(uv));
                return;
            }
            if (path == null || _displaySize.X <= 0) return; // first open still probing
            IntVec2 needed = VideoDecodeSizing.PreviewBox(_displaySize.X, _displaySize.Y, ProjectComposition.BuildVideoContent(uv));
            IntVec2 have = _readerOpening != null ? _openingBox : _readerBox;
            if (VideoDecodeSizing.Covers(have, needed)) return;
            CancelOpening();
            StartOpening(path, ProjectComposition.BuildVideoContent(uv));
        }

        void StartOpening(string path, SlotContent content)
        {
            _readerCts = new CancellationTokenSource();
            _openingBox = _displaySize.X > 0 ? VideoDecodeSizing.PreviewBox(_displaySize.X, _displaySize.Y, content) : new IntVec2(int.MaxValue, int.MaxValue);
            _readerOpening = OpenReaderAsync(path, content, _displaySize, _readerCts.Token);
        }

        async Task<OpenedReader> OpenReaderAsync(string path, SlotContent content, IntVec2 knownDisplay, CancellationToken ct)
        {
            IntVec2 display = knownDisplay;
            if (display.X <= 0)
            {
                MediaInfo info = await _services.Media.ProbeAsync(path, ct);
                display = new IntVec2(info.DisplayWidth, info.DisplayHeight);
            }
            IntVec2 box = VideoDecodeSizing.PreviewBox(display.X, display.Y, content);
            IVideoReader reader = await _services.Media.OpenVideoReaderAsync(path, new VideoReaderOptions(box.X, box.Y), ct);
            return new OpenedReader { Reader = reader, Box = box, DisplaySize = display };
        }

        void PollReader()
        {
            Task<OpenedReader> opening = _readerOpening;
            if (opening == null || !opening.IsCompleted) return;
            _readerOpening = null;
            _readerCts?.Dispose();
            _readerCts = null;
            if (opening.Status == TaskStatus.RanToCompletion)
            {
                OpenedReader opened = opening.Result;
                _reader?.Dispose(); // the replaced reader drew until now; the texture keeps its last frame until the new one uploads
                _reader = opened.Reader;
                _readerBox = opened.Box;
                _displaySize = opened.DisplaySize;
                _deliveredRequestUs = long.MinValue;
                _videoSize = new IntVec2(_reader.Width, _reader.Height);
                VideoError = null;
                RebuildPlans();
                EnsureVideoReader(); // the zoom may have grown again while opening
            }
            else if (opening.IsFaulted)
            {
                VideoError = opening.Exception?.GetBaseException();
                Debug.LogWarning("[Studio] Preview video cannot be opened: " + VideoError?.Message);
            }
        }

        void CancelOpening()
        {
            _readerCts?.Cancel();
            _readerCts?.Dispose();
            _readerCts = null;
            Task<OpenedReader> opening = _readerOpening;
            _readerOpening = null;
            if (opening != null) opening.ContinueWith(t => { if (t.Status == TaskStatus.RanToCompletion) t.Result.Reader.Dispose(); }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void CloseReader()
        {
            CancelOpening();
            _reader?.Dispose();
            _reader = null;
        }

        void RemixIfNeeded()
        {
            string key = ProjectComposition.AudioMixKey(_doc, _timeline);
            if (key == _audioKey) return;
            bool first = _audioKey == null;
            _audioKey = key;
            AudioPending = true;
            _mixCts?.Cancel(); // the running mix stops within ~1 s (StudioMixBuilder checks between chunks)
            _mixCts?.Dispose();
            _mixCts = null;
            if (first) StartRemix();
            else _remixDueUs = _nowUs + RemixDebounceUs;
        }

        void StartRemix()
        {
            _mixCts?.Cancel();
            _mixCts?.Dispose();
            _mixCts = new CancellationTokenSource();
            _ = RemixAsync(_mixCts.Token);
        }

        async Task RemixAsync(CancellationToken ct)
        {
            AudioPending = true;
            try
            {
                StudioMixResult mix = await _services.AudioMixer.RenderAsync(_doc, _timeline, _clip.Directory, _projectDir, ct);
                if (ct.IsCancellationRequested || _disposed) return;
                _audio.SetMix(mix.Samples);
                AudioError = null;
                AudioPending = false;
                StartAudioAtClock();
            }
            catch (OperationCanceledException)
            {
                // superseded by a newer edit
            }
            catch (Exception e)
            {
                if (ct.IsCancellationRequested || _disposed) return;
                AudioError = e;
                _audio.SetMix(Array.Empty<float>());
                Debug.LogWarning("[Studio] Preview audio failed: " + e.Message);
            }
            finally
            {
                if (!ct.IsCancellationRequested && _remixDueUs < 0) AudioPending = false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _services.Runner.Tick -= OnTick;
            _audio.ConfigurationChanged -= OnAudioConfigurationChanged;
            _mixCts?.Cancel();
            _mixCts?.Dispose();
            CloseReader();
            _video.Dispose();
            _frames.Dispose();
            if (_audio != null) StudioUnity.Destroy(_audio.gameObject);
            StudioUnity.Release(_target);
            _target = null;
            _clip?.Dispose();
            _clip = null;
        }
    }
}
