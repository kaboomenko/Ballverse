using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Ads;
using Ballverse.Core.Modes;
using Ballverse.Core.Params;
using Ballverse.Render;
using UnityEngine;

namespace Ballverse.Sim
{
    /// <summary>
    /// Looping previews of the gallery cards (spec §4.3 "зацикленное видео-превью 3–4 с"): instead of pre-rendered clips, each
    /// mode runs its first built-in preset headless at low resolution through the same renderer as the live screen and loops
    /// every <see cref="LoopSeconds"/> seconds (or at its finale). Only while the gallery is visible; previews are presented
    /// round-robin (one per display frame) to bound the cost; on tier C a single still frame is drawn. Main thread.
    /// </summary>
    public sealed class GalleryPreviewRenderer : IDisposable
    {
        public const float LoopSeconds = 4f;
        public const int Width = 180;
        public const int Height = 320;

        sealed class Entry
        {
            public string ModeId;
            public IModeDefinition Mode;
            public ParamSet Values;
            public ulong Seed;
            public LiveSimController Controller;
            public LiveSimView View;
            public bool Drawn;
        }

        readonly List<Entry> _entries = new List<Entry>();
        readonly RenderResources _resources;
        readonly Action<string, Exception> _onError;
        int _next;
        bool _disposed;

        public GalleryPreviewRenderer(RenderResources resources, Action<string, Exception> onError)
        {
            _resources = resources;
            _onError = onError;
        }

        /// <summary>Previews keep running (tiers A/B); false draws one still frame per mode (tier C).</summary>
        public bool Animated { get; set; } = true;

        public bool Active { get; set; }

        /// <summary>A preview texture was created or replaced.</summary>
        public event Action Changed;

        /// <summary>Sets the previewed modes (the gallery's cards). Existing previews of the same mode are kept.</summary>
        public void SetModes(IReadOnlyList<KeyValuePair<IModeDefinition, ParamSet>> modes)
        {
            var keep = new List<Entry>();
            foreach (KeyValuePair<IModeDefinition, ParamSet> m in modes)
            {
                Entry existing = _entries.Find(e => e.ModeId == m.Key.Info.Id);
                if (existing != null)
                {
                    keep.Add(existing);
                    continue;
                }
                var entry = new Entry
                {
                    ModeId = m.Key.Info.Id,
                    Mode = m.Key,
                    Values = m.Value,
                    Seed = m.Value.TryGet(CommonParams.Seed, out ParamValue s) ? s.AsSeed : 0UL,
                    Controller = new LiveSimController(new LiveSimOptions { FrameHistory = 90, MaxStepsPerDisplayFrame = 4 }),
                };
                entry.View = new LiveSimView(_resources);
                entry.View.SetTier(DeviceTier.C);
                entry.View.Presenter.DynamicQuality = false;
                entry.View.TargetChanged += _ => Changed?.Invoke();
                entry.View.Bind(entry.Controller);
                keep.Add(entry);
            }
            foreach (Entry old in _entries)
            {
                if (!keep.Contains(old)) DisposeEntry(old);
            }
            _entries.Clear();
            _entries.AddRange(keep);
            _next = 0;
        }

        public Texture Get(string modeId)
        {
            foreach (Entry e in _entries)
            {
                if (e.ModeId == modeId) return e.View.Target;
            }
            return null;
        }

        /// <summary>Steps the previews and draws one of them. Call once per display frame.</summary>
        public void Update(float deltaSeconds, int displayFrameRate, double nowSeconds)
        {
            if (_disposed || !Active || _entries.Count == 0) return;
            // At most one loop restart per display frame: a restart rebuilds the session (~100 KB of managed allocation), so
            // the cards restart on successive frames instead of all in the same one (spec §8.2 P3 frame pacing).
            bool restarted = false;
            for (int i = 0; i < _entries.Count; i++)
            {
                Entry e = _entries[i];
                try
                {
                    if (!e.Controller.HasRun || e.Controller.IsCompleted
                        || (Animated && e.Controller.Tick >= LoopSeconds * SimConstants.TickRate))
                    {
                        if (e.Controller.HasRun && !Animated && e.Drawn) continue;
                        if (restarted)
                        {
                            if (Animated && e.Controller.HasRun && !e.Controller.IsCompleted) e.Controller.AdvanceDisplayFrame(displayFrameRate, nowSeconds);
                            continue;
                        }
                        restarted = true;
                        e.Controller.End(GameRunEnd.Abandoned, false);
                        e.Controller.Begin(e.Mode, e.Values, e.Seed, null);
                    }
                    if (Animated) e.Controller.AdvanceDisplayFrame(displayFrameRate, nowSeconds);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
                {
                    _onError?.Invoke("gallery.preview." + e.ModeId, ex);
                    DisposeEntry(e);
                    _entries.RemoveAt(i);
                    i--;
                }
            }
            if (_entries.Count == 0) return;
            _next %= _entries.Count;
            Entry draw = _entries[_next];
            _next = (_next + 1) % _entries.Count;
            if (!Animated && draw.Drawn) return;
            if (draw.View.Present(Width, Height, deltaSeconds)) draw.Drawn = true;
        }

        /// <summary>Frees the preview textures (low memory); they are rebuilt when the gallery shows again.</summary>
        public void ReleaseTextures()
        {
            foreach (Entry e in _entries) DisposeEntry(e);
            var modes = new List<KeyValuePair<IModeDefinition, ParamSet>>();
            foreach (Entry e in _entries) modes.Add(new KeyValuePair<IModeDefinition, ParamSet>(e.Mode, e.Values));
            _entries.Clear();
            SetModes(modes);
            Changed?.Invoke();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (Entry e in _entries) DisposeEntry(e);
            _entries.Clear();
        }

        static void DisposeEntry(Entry e)
        {
            e.Controller.Dispose();
            e.View.Dispose();
        }
    }
}
