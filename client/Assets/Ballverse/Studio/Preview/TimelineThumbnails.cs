using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ballverse.Core.Projects;
using Ballverse.Core.Studio;
using Ballverse.Media;
using Unity.Collections;
using UnityEngine;

namespace Ballverse.Studio
{
    /// <summary>One timeline thumbnail: where it was taken and its texture (null until it is ready).</summary>
    public sealed class TimelineThumbnail
    {
        internal TimelineThumbnail(long sourceUs, int tick)
        {
            SourceUs = sourceUs;
            Tick = tick;
        }

        /// <summary>Video thumbnails: source time, µs. Simulation thumbnails: the tick's time, µs.</summary>
        public long SourceUs { get; }
        /// <summary>Simulation thumbnails: render-stream tick; −1 for video thumbnails.</summary>
        public int Tick { get; }
        /// <summary>Upright texture for the UI (<c>Background.FromTexture2D</c> / <c>FromRenderTexture</c>), or null while pending.</summary>
        public Texture Texture { get; internal set; }
        public bool IsReady => Texture != null;
    }

    /// <summary>
    /// Frame thumbnails of the timeline tracks (spec §3.4: "превью кадров-миниатюр" under the trim handles of track S, "миниатюры
    /// извлекаются в фоне" for track V), produced in the background over Unity frames: the simulation through this object's own
    /// <see cref="ISimFrameRenderer"/> (Preview quality, one thumbnail per frame, ~96 px wide), the user video through its own
    /// small realtime reader (frame by pts, non-blocking, rows flipped upright on upload). Thumbnails cover the full clip and the
    /// full source video, so trimming never re-renders them; a new video file restarts the V track. Main thread; driven by
    /// <see cref="StudioRunner.Tick"/>; dispose to release the textures, the reader and the renderer. Memory: 2 × 16 thumbnails of
    /// 96 × 170 RGBA ≈ 2 MB.
    /// </summary>
    public sealed class TimelineThumbnails : IDisposable
    {
        readonly StudioServices _services;
        readonly ClipSource _clip;
        readonly ISimFrameRenderer _sim;
        readonly string _projectDir;
        readonly int _width;
        readonly List<TimelineThumbnail> _simThumbs = new List<TimelineThumbnail>();
        readonly List<TimelineThumbnail> _videoThumbs = new List<TimelineThumbnail>();
        int _nextSim;
        int _nextVideo;
        int _discontinuity;
        IVideoReader _reader;
        Task<IVideoReader> _opening;
        CancellationTokenSource _videoCts;
        string _videoFile;
        bool _disposed;

        internal TimelineThumbnails(StudioServices services, ClipSource clip, string projectDir, ProjectDocument doc, int count, int width)
        {
            _services = services;
            _clip = clip;
            _projectDir = projectDir;
            _width = Math.Max(16, width & ~1);
            _sim = services.CreateSimRenderer();
            _sim.BeginClip(clip.Info);
            int[] ticks = ThumbnailPlan.Ticks(clip.TickCount, Math.Max(0, count));
            foreach (int t in ticks) _simThumbs.Add(new TimelineThumbnail(t * 1_000_000L / Ballverse.Core.SimConstants.TickRate, t));
            VideoCount = Math.Max(0, count);
            SetProject(doc);
            services.Runner.Tick += OnTick;
        }

        /// <summary>Thumbnails of track S (full clip), in time order.</summary>
        public IReadOnlyList<TimelineThumbnail> Simulation => _simThumbs;

        /// <summary>Thumbnails of track V (full source video), in time order; empty without a user video.</summary>
        public IReadOnlyList<TimelineThumbnail> Video => _videoThumbs;

        public int VideoCount { get; }

        /// <summary>Raised on the main thread when a thumbnail became ready (the UI redraws the track).</summary>
        public event Action Changed;

        /// <summary>Applies an edited project: a different user video file restarts the V thumbnails; trims do not.</summary>
        public void SetProject(ProjectDocument doc)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            string file = doc.UserVideo?.File;
            if (file == _videoFile) return;
            _videoFile = file;
            ResetVideo();
            if (file == null || _projectDir == null || VideoCount == 0) return;
            string path = Path.Combine(_projectDir, file.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) return;
            _videoCts = new CancellationTokenSource();
            _opening = OpenAsync(path, _videoCts.Token);
        }

        async Task<IVideoReader> OpenAsync(string path, CancellationToken ct)
        {
            MediaInfo info = await _services.Media.ProbeAsync(path, ct);
            int h = ThumbnailPlan.HeightFor(_width, info.DisplayWidth, info.DisplayHeight);
            IVideoReader reader = await _services.Media.OpenVideoReaderAsync(path, new VideoReaderOptions(_width, Math.Max(_width, h)), ct);
            if (_disposed || ct.IsCancellationRequested)
            {
                reader.Dispose();
                ct.ThrowIfCancellationRequested();
            }
            long duration = reader.DurationUs > 0 ? reader.DurationUs : info.DurationUs;
            foreach (long t in ThumbnailPlan.Times(duration, VideoCount)) _videoThumbs.Add(new TimelineThumbnail(t, -1));
            return reader;
        }

        void OnTick(float dt)
        {
            if (_disposed) return;
            bool changed = false;
            if (_nextSim < _simThumbs.Count)
            {
                RenderSim(_simThumbs[_nextSim++]);
                changed = true;
            }
            changed |= PollVideo();
            if (changed) Changed?.Invoke();
        }

        void RenderSim(TimelineThumbnail thumb)
        {
            int h = ThumbnailPlan.HeightFor(_width, SimSlotSizing.AspectWidth, SimSlotSizing.AspectHeight);
            RenderTexture rt = StudioCompositor.CreateCanvas(_width, h, "StudioThumbSim");
            try
            {
                _sim.SetFrameTime(new SimFrameTime(thumb.Tick, true, 0, ++_discontinuity, thumb.SourceUs));
                _sim.Render(_clip.ReadFrame(thumb.Tick), _clip.Shapes, _clip.Labels, rt, StudioRenderQuality.Preview);
                thumb.Texture = rt;
            }
            catch (Exception e) when (!(e is OutOfMemoryException))
            {
                StudioUnity.Release(rt);
                Debug.LogWarning("[Studio] Simulation thumbnail failed: " + e.Message);
            }
        }

        bool PollVideo()
        {
            if (_opening != null)
            {
                if (!_opening.IsCompleted) return false;
                Task<IVideoReader> done = _opening;
                _opening = null;
                if (done.Status != TaskStatus.RanToCompletion)
                {
                    if (done.IsFaulted) Debug.LogWarning("[Studio] Video thumbnails unavailable: " + done.Exception?.GetBaseException().Message);
                    return false;
                }
                _reader = done.Result;
                if (_videoThumbs.Count > 0) _reader.Prefetch(_videoThumbs[0].SourceUs);
            }
            if (_reader == null || _nextVideo >= _videoThumbs.Count) return false;
            TimelineThumbnail thumb = _videoThumbs[_nextVideo];
            try
            {
                if (!_reader.TryReadFrameAt(thumb.SourceUs, out VideoFrame frame)) return false;
                using (frame) thumb.Texture = UploadUpright(frame);
            }
            catch (MediaException e)
            {
                Debug.LogWarning("[Studio] Video thumbnail failed: " + e.Message);
                CloseReader();
                return false;
            }
            _nextVideo++;
            if (_nextVideo < _videoThumbs.Count) _reader.Prefetch(_videoThumbs[_nextVideo].SourceUs);
            else CloseReader(); // all done: free the decoder
            return true;
        }

        static Texture2D UploadUpright(VideoFrame frame)
        {
            var tex = new Texture2D(frame.Width, frame.Height, TextureFormat.RGBA32, false, false)
            {
                name = "StudioThumbVideo",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.HideAndDontSave,
            };
            // Frames are top-down; Unity textures start at the bottom row: copy the rows in reverse so the UI shows it upright.
            NativeArray<byte> dst = tex.GetRawTextureData<byte>();
            NativeArray<byte> src = frame.Data;
            int row = frame.RowBytes;
            for (int y = 0; y < frame.Height; y++) NativeArray<byte>.Copy(src, y * row, dst, (frame.Height - 1 - y) * row, row);
            tex.Apply(false, true);
            return tex;
        }

        void ResetVideo()
        {
            CloseReader();
            foreach (TimelineThumbnail t in _videoThumbs) StudioUnity.Destroy(t.Texture);
            _videoThumbs.Clear();
            _nextVideo = 0;
        }

        void CloseReader()
        {
            _videoCts?.Cancel();
            _videoCts?.Dispose();
            _videoCts = null;
            Task<IVideoReader> opening = _opening;
            _opening = null;
            if (opening != null) opening.ContinueWith(t => { if (t.Status == TaskStatus.RanToCompletion) t.Result.Dispose(); }, TaskScheduler.FromCurrentSynchronizationContext());
            _reader?.Dispose();
            _reader = null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _services.Runner.Tick -= OnTick;
            ResetVideo();
            foreach (TimelineThumbnail t in _simThumbs)
            {
                if (t.Texture is RenderTexture rt) StudioUnity.Release(rt);
                t.Texture = null;
            }
            _sim.EndClip();
            if (_sim is IDisposable d) d.Dispose();
            _clip.Dispose();
        }
    }
}
