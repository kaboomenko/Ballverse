using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ballverse.Core.Integration;
using Ballverse.Core.Projects;
using Ballverse.Core.Studio;
using Ballverse.Core.Timeline;
using Ballverse.Media;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ballverse.Studio
{
    /// <summary>
    /// GPU side of the export frame loop (spec §7.5 steps a–e, release 1 RGBA readback): renders frame i with
    /// <see cref="StudioFrameRenderer"/> at the export resolution with the Export quality profile, converts it for the encoder
    /// (vertical flip + sRGB encode into an RGBA8 UNorm target) and reads it back with
    /// <see cref="AsyncGPUReadback.RequestIntoNativeArray{T}(ref NativeArray{T}, Texture, int, TextureFormat, Action{AsyncGPUReadbackRequest})"/>
    /// into one of 3 persistent buffers (≈ 8.3 MB each at 1080p) — at most 3 frames in flight. Devices without async readback use
    /// a synchronous <see cref="Texture2D.ReadPixels(Rect, int, int, bool)"/> (slower, same bytes). The user video is decoded
    /// frame-exactly by pts (non-blocking: a frame whose video frame is not decoded yet is retried next Unity frame).
    /// Main thread.
    /// </summary>
    public sealed class GpuExportFrameRenderer : IExportFrameRenderer
    {
        enum SlotState
        {
            Free,
            Pending,
            Draining,
        }

        sealed class Slot
        {
            public RenderTexture Target;
            public NativeArray<byte> Buffer;
            public AsyncGPUReadbackRequest Request;
            public bool Async;
            public SlotState State;
            public int FrameIndex;
        }

        readonly StudioServices _services;
        readonly string _clipDir;
        readonly string _projectDir;
        readonly Slot[] _slots = new Slot[ExportFrameFlow.DefaultMaxInFlight];
        readonly Queue<Slot> _pending = new Queue<Slot>();
        readonly VideoTextureUploader _video = new VideoTextureUploader();
        ClipSource _clip;
        StudioFrameRenderer _frames;
        RenderTexture _canvas;
        Texture2D _cpuReadback;
        IVideoReader _reader;
        ProjectDocument _doc;
        bool _useAsync;
        bool _disposed;

        public GpuExportFrameRenderer(StudioServices services, string clipDir, string projectDir)
        {
            _services = services ?? throw new ArgumentNullException(nameof(services));
            _clipDir = clipDir ?? throw new ArgumentNullException(nameof(clipDir));
            _projectDir = projectDir;
        }

        public int MaxInFlight => _slots.Length;

        public int InFlight => _pending.Count;

        /// <summary>The last composed export frame (progress sheet preview, spec §3.8 "превью текущего кадра").</summary>
        public RenderTexture PreviewTexture => _canvas;

        public async Task PrepareAsync(ExportJob job, CancellationToken ct)
        {
            if (job == null) throw new ArgumentNullException(nameof(job));
            ExportResumeState state = job.State;
            _doc = state.Project;
            try
            {
                _clip = ClipSource.Open(_clipDir);
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException)
            {
                throw new ExportFailureException(ExportErrorCode.ClipDataMissing, "render stream unavailable: " + e.Message, e);
            }
            if (ExportJob.EffectiveStreamTicks(_clip.TickCount) != state.StreamTicks)
                throw new ExportFailureException(ExportErrorCode.ResumeInvalid, "render stream changed since the export started");
            _frames = new StudioFrameRenderer(_services.Compositor, _services.CreateSimRenderer(), StudioRenderQuality.Export, SimSlotSizing.DefaultMaxSide, ownsSimRenderer: true,
                maxSimZoomDensity: SimSlotSizing.ExportMaxZoomDensity);
            _frames.OpenClip(_clip);
            _frames.SetProject(_doc, CompositeStyle.FromProject(_doc, _services.ThemeBackground, _services.WatermarkTexture));
            int w = job.Settings.Width, h = job.Settings.Height;
            _canvas = StudioCompositor.CreateCanvas(w, h, "StudioExportCanvas");
            _useAsync = SystemInfo.supportsAsyncGPUReadback;
            for (int i = 0; i < _slots.Length; i++)
            {
                _slots[i] = new Slot
                {
                    Target = StudioCompositor.CreateReadbackTarget(w, h, "StudioExportReadback" + i),
                    Buffer = new NativeArray<byte>(w * h * 4, Allocator.Persistent, NativeArrayOptions.UninitializedMemory),
                    State = SlotState.Free,
                };
            }
            if (!_useAsync) _cpuReadback = new Texture2D(w, h, TextureFormat.RGBA32, false, true) { hideFlags = HideFlags.HideAndDontSave };

            if (_doc.UserVideo != null && _projectDir != null)
            {
                string path = Path.Combine(_projectDir, _doc.UserVideo.File.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path)) throw new ExportFailureException(ExportErrorCode.UserVideoFailed, "user video missing");
                int boxW = state.VideoBoxWidth > 0 ? state.VideoBoxWidth : w;
                int boxH = state.VideoBoxHeight > 0 ? state.VideoBoxHeight : h;
                try
                {
                    _reader = await _services.Media.OpenVideoReaderAsync(path, new VideoReaderOptions(boxW, boxH), ct);
                }
                catch (MediaException e)
                {
                    throw new ExportFailureException(ExportErrorCode.UserVideoFailed, e.Message, e);
                }
                if (state.VideoWidth > 0 && (_reader.Width != state.VideoWidth || _reader.Height != state.VideoHeight))
                    throw new ExportFailureException(ExportErrorCode.ResumeInvalid, "decoded video size " + _reader.Width + "x" + _reader.Height
                        + " differs from the planned " + state.VideoWidth + "x" + state.VideoHeight);
            }
        }

        public bool TryRender(in ExportFrame frame)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(GpuExportFrameRenderer));
            ReclaimDrained();
            Slot slot = FreeSlot();
            if (slot == null) return false;

            SlotPicture video = SlotPicture.Empty;
            if (frame.DrawsVideoSlot)
            {
                if (frame.Video.Kind == VideoSampleKind.Black)
                {
                    video = SlotPicture.Black;
                }
                else if (frame.NeedsVideoFrame && _reader != null)
                {
                    try
                    {
                        if (!_reader.TryReadFrameAt(frame.Video.SourcePtsUs, out VideoFrame vf)) return false;
                        using (vf) _video.Upload(vf);
                    }
                    catch (MediaException e)
                    {
                        throw new ExportFailureException(ExportErrorCode.UserVideoFailed, e.Message, e);
                    }
                    if (frame.Plan.HasVideoMapping)
                    {
                        FitBackground bg = _doc.UserVideo?.FitBackground;
                        video = new SlotPicture
                        {
                            Kind = SlotPictureKind.Picture,
                            Texture = _video.Texture,
                            BottomUp = false,
                            Mapping = frame.Plan.VideoMapping,
                            Content = ProjectComposition.BuildVideoContent(_doc.UserVideo),
                            Background = bg?.Kind ?? FitBackgroundKind.Blur,
                            BackgroundColor = StudioColor.Parse(bg?.Color, StudioColor.Black),
                        };
                    }
                }
            }

            _frames.Render(_canvas, frame.Plan, frame.Sim, frame.PtsUs, video, 0, null);
            _services.Compositor.ConvertForReadback(_canvas, slot.Target);
            slot.FrameIndex = frame.Index;
            if (_useAsync)
            {
                slot.Request = AsyncGPUReadback.RequestIntoNativeArray(ref slot.Buffer, slot.Target, 0, TextureFormat.RGBA32);
                slot.Async = true;
            }
            else
            {
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = slot.Target;
                _cpuReadback.ReadPixels(new Rect(0, 0, slot.Target.width, slot.Target.height), 0, 0, false);
                RenderTexture.active = previous;
                NativeArray<byte> raw = _cpuReadback.GetRawTextureData<byte>();
                NativeArray<byte>.Copy(raw, slot.Buffer, slot.Buffer.Length);
                slot.Async = false;
            }
            slot.State = SlotState.Pending;
            _pending.Enqueue(slot);
            return true;
        }

        public void Prefetch(in ExportFrame next)
        {
            if (_reader != null && next.NeedsVideoFrame)
            {
                try
                {
                    _reader.Prefetch(next.Video.SourcePtsUs);
                }
                catch (MediaException e)
                {
                    throw new ExportFailureException(ExportErrorCode.UserVideoFailed, e.Message, e);
                }
            }
        }

        public bool TryPeekCompleted(out NativeArray<byte> rgba, out int frameIndex)
        {
            rgba = default(NativeArray<byte>);
            frameIndex = -1;
            if (_pending.Count == 0) return false;
            Slot slot = _pending.Peek();
            if (slot.Async)
            {
                if (!slot.Request.done) return false;
                if (slot.Request.hasError) throw new ExportFailureException(ExportErrorCode.Timeout, "GPU readback failed for frame " + slot.FrameIndex);
            }
            rgba = slot.Buffer;
            frameIndex = slot.FrameIndex;
            return true;
        }

        public void ReleaseCompleted()
        {
            if (_pending.Count == 0) throw new InvalidOperationException("nothing to release");
            _pending.Dequeue().State = SlotState.Free;
        }

        public void DiscardInFlight()
        {
            while (_pending.Count > 0)
            {
                Slot s = _pending.Dequeue();
                // A readback still writing into the buffer keeps the slot until it is done.
                s.State = s.Async && !s.Request.done ? SlotState.Draining : SlotState.Free;
            }
            _video.Invalidate();
            _frames?.ResetHistory();
        }

        void ReclaimDrained()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                Slot s = _slots[i];
                if (s != null && s.State == SlotState.Draining && s.Request.done) s.State = SlotState.Free;
            }
        }

        Slot FreeSlot()
        {
            for (int i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] != null && _slots[i].State == SlotState.Free) return _slots[i];
            }
            return null;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            for (int i = 0; i < _slots.Length; i++)
            {
                Slot s = _slots[i];
                if (s == null) continue;
                // Never free a buffer the GPU is still writing.
                if (s.Async && s.State != SlotState.Free && !s.Request.done) s.Request.WaitForCompletion();
                if (s.Buffer.IsCreated) s.Buffer.Dispose();
                StudioUnity.Release(s.Target);
                _slots[i] = null;
            }
            _pending.Clear();
            _reader?.Dispose();
            _reader = null;
            _video.Dispose();
            _frames?.Dispose();
            _frames = null;
            _clip?.Dispose();
            _clip = null;
            StudioUnity.Release(_canvas);
            _canvas = null;
            StudioUnity.Destroy(_cpuReadback);
            _cpuReadback = null;
        }
    }
}
