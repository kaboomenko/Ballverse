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
    /// <see cref="PreviewAudioOutput"/>, which is also the master clock while it plays. Edits are applied with
    /// <see cref="SetProject"/> (≤ 1 frame: geometry is recomposed, the video is not re-decoded). When frames are missed the internal
    /// resolution drops, never the frame rate (<see cref="PreviewQualityGovernor"/>). Main thread; driven by
    /// <see cref="StudioRunner.Tick"/>; dispose to release the decoder, the clip and the render textures.
    /// </summary>
    public sealed class StudioPreviewPlayer : IDisposable
    {
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
        Task<IVideoReader> _readerOpening;
        CancellationTokenSource _readerCts;
        CancellationTokenSource _mixCts;
        string _videoKey;
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
            _frames = new StudioFrameRenderer(services.Compositor, services.SimRenderer, StudioRenderQuality.Preview, 2048);
            _frames.OpenClip(clip);
            _audio = PreviewAudioOutput.Create(services.Runner.transform);
            _exportFps = OutputFps.IsValid(exportFps) ? exportFps : OutputFps.Fps60;
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

        /// <summary>True while the offline mix is being (re)built.</summary>
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
            if (_audio.HasBuffer && !AudioPending) _audio.Play(_clock.PositionUs);
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
            _frames.ResetHistory();
            _dirty = true;
        }

        void OnTick(float dt)
        {
            if (_disposed) return;
            long dtUs = (long)(Math.Max(0f, dt) * 1e6f);
            bool wasPlaying = _clock.IsPlaying;
            long? audible = wasPlaying && _audio.HasBuffer && !AudioPending ? _audio.AudibleUs : (long?)null;
            _clock.Advance(dtUs, audible);
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

        void EnsureVideoReader()
        {
            UserVideoSettings uv = _doc.UserVideo;
            string path = uv != null && _projectDir != null ? Path.Combine(_projectDir, uv.File.Replace('/', Path.DirectorySeparatorChar)) : null;
            // Only what changes the decoded size reopens the decoder (layout edits such as a ratio drag do not).
            string key = path == null ? null : path + "|" + uv.Rotation + "|" + uv.Fill + "|" + uv.Zoom.ToString(System.Globalization.CultureInfo.InvariantCulture);
            if (key == _videoKey) return;
            _videoKey = key;
            CloseReader();
            _video.Invalidate();
            _deliveredRequestUs = long.MinValue;
            _videoSize = default(IntVec2);
            if (path == null || !File.Exists(path)) return;
            _readerCts = new CancellationTokenSource();
            _readerOpening = OpenReaderAsync(path, uv, _readerCts.Token);
        }

        async Task<IVideoReader> OpenReaderAsync(string path, UserVideoSettings uv, CancellationToken ct)
        {
            MediaInfo info = await _services.Media.ProbeAsync(path, ct);
            // Decode box: what a full 1080×1920 frame would need, halved (a phone preview area is ≤ ~540 px wide), so slot
            // and canvas changes never reopen the decoder.
            IntVec2 box = VideoDecodeSizing.Compute(info.DisplayWidth, info.DisplayHeight, 1080, 1920, ProjectComposition.BuildVideoContent(uv));
            box = new IntVec2(Math.Max(VideoDecodeSizing.MinSide, (box.X / 2) & ~1), Math.Max(VideoDecodeSizing.MinSide, (box.Y / 2) & ~1));
            return await _services.Media.OpenVideoReaderAsync(path, new VideoReaderOptions(box.X, box.Y), ct);
        }

        void PollReader()
        {
            Task<IVideoReader> opening = _readerOpening;
            if (opening == null || !opening.IsCompleted) return;
            _readerOpening = null;
            if (opening.Status == TaskStatus.RanToCompletion)
            {
                _reader = opening.Result;
                _videoSize = new IntVec2(_reader.Width, _reader.Height);
                VideoError = null;
                RebuildPlans();
            }
            else if (opening.IsFaulted)
            {
                VideoError = opening.Exception?.GetBaseException();
                Debug.LogWarning("[Studio] Preview video cannot be opened: " + VideoError?.Message);
            }
        }

        void CloseReader()
        {
            _readerCts?.Cancel();
            _readerCts?.Dispose();
            _readerCts = null;
            Task<IVideoReader> opening = _readerOpening;
            _readerOpening = null;
            if (opening != null) opening.ContinueWith(t => { if (t.Status == TaskStatus.RanToCompletion) t.Result.Dispose(); }, TaskScheduler.FromCurrentSynchronizationContext());
            _reader?.Dispose();
            _reader = null;
        }

        void RemixIfNeeded()
        {
            string key = AudioKey(_doc, _timeline);
            if (key == _audioKey) return;
            _audioKey = key;
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
                if (_clock.IsPlaying) _audio.Play(_clock.PositionUs);
            }
            catch (OperationCanceledException)
            {
                // superseded by a newer edit
            }
            catch (Exception e)
            {
                if (ct.IsCancellationRequested) return;
                AudioError = e;
                _audio.SetMix(Array.Empty<float>());
                Debug.LogWarning("[Studio] Preview audio failed: " + e.Message);
            }
            finally
            {
                if (!ct.IsCancellationRequested) AudioPending = false;
            }
        }

        /// <summary>Everything the mix depends on: timeline length and mapping, audio block, clip sounds, user video trim.</summary>
        static string AudioKey(ProjectDocument doc, StudioTimeline t)
        {
            var c = doc.Clip ?? new ClipSettings();
            var o = doc.Output ?? new OutputSettings(); // export settings and history do not change the sound
            return CoreJson.Serialize(doc.Audio, false) + "|" + c.SoundPack + "|" + c.Melody + "|" + c.TrimInTick + "|" + c.TrimOutTick + "|" + c.Speed
                + "|" + CoreJson.Serialize(c.FinaleSlowMo, false) + "|" + c.AfterEnd + "|" + CoreJson.Serialize(doc.UserVideo, false)
                + "|" + o.DurationMode + "|" + o.ManualDurationUs + "|" + CoreJson.Serialize(o.Cover, false) + "|" + t.Fps + "|" + t.FrameCount;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _services.Runner.Tick -= OnTick;
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
