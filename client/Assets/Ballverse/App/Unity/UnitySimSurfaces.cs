using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Modes;
using Ballverse.Core.Params;
using Ballverse.Render;
using Ballverse.Sim;
using Ballverse.UI.ViewModels;
using UnityEngine;
using QualityLevel = Ballverse.Core.RenderGeometry.QualityLevel;

namespace Ballverse.App
{
    /// <summary>
    /// <see cref="ISimSurface"/> over Ballverse.Sim's <see cref="LiveSimView"/>: the live run drawn edge to edge at screen
    /// resolution (longest side capped per tier to bound the HDR scene buffers, spec §7.11), starting profile from the tier, the
    /// user's quality choice forcing a level, and the renderer's dynamic fallback below 55 FPS (spec §7.7).
    /// </summary>
    public sealed class UnitySimSurface : ISimSurface
    {
        readonly LiveSimView _view;
        DeviceTier _tier = DeviceTier.B;

        public UnitySimSurface(LiveSimView view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _view.TargetChanged += _ => TargetChanged?.Invoke();
        }

        public Texture RenderTarget => _view.Target;

        public event Action TargetChanged;

        public void Bind(LiveSimController controller) => _view.Bind(controller);

        public void ApplyLook(ParamSet values) => _view.ApplyLook(values);

        public void SetTier(DeviceTier tier, QualityPreference preference)
        {
            _tier = tier;
            _view.SetTier(tier);
            if (preference == QualityPreference.High) _view.SetQuality(QualityLevel.A);
            else if (preference == QualityPreference.Economy) _view.SetQuality(QualityLevel.C);
        }

        public void SetReduceMotion(bool reduce) => _view.ReduceMotion = reduce;

        public void Present(float deltaSeconds)
        {
            int w = Screen.width;
            int h = Screen.height;
            if (w <= 0 || h <= 0) return;
            int cap = _tier == DeviceTier.A ? 1920 : _tier == DeviceTier.B ? 1600 : 1280;
            int longest = Mathf.Max(w, h);
            if (longest > cap)
            {
                float s = cap / (float)longest;
                w = Mathf.Max(16, Mathf.RoundToInt(w * s));
                h = Mathf.Max(16, Mathf.RoundToInt(h * s));
            }
            _view.Present(w, h, deltaSeconds);
        }

        public Texture CaptureThumbnail() => _view.CaptureThumbnail(96);

        public void ReleaseThumbnail(Texture texture) => LiveSimView.ReleaseThumbnail(texture);

        public void Dispose() => _view.Dispose();
    }

    /// <summary><see cref="IGalleryPreviews"/> over Ballverse.Sim's <see cref="GalleryPreviewRenderer"/> (still frames on tier C).</summary>
    public sealed class UnityGalleryPreviews : IGalleryPreviews
    {
        readonly GalleryPreviewRenderer _renderer;

        public UnityGalleryPreviews(GalleryPreviewRenderer renderer)
        {
            _renderer = renderer ?? throw new ArgumentNullException(nameof(renderer));
            _renderer.Changed += () => Changed?.Invoke();
        }

        public event Action Changed;

        public bool Active
        {
            get => _renderer.Active;
            set => _renderer.Active = value;
        }

        public void SetModes(IReadOnlyList<KeyValuePair<IModeDefinition, ParamSet>> modes, DeviceTier tier)
        {
            _renderer.Animated = tier != DeviceTier.C;
            _renderer.SetModes(modes);
        }

        public Texture Get(string modeId) => _renderer.Get(modeId);

        public void Update(float deltaSeconds, int displayFrameRate, double nowSeconds) => _renderer.Update(deltaSeconds, displayFrameRate, nowSeconds);

        public void ReleaseTextures() => _renderer.ReleaseTextures();

        public void Dispose() => _renderer.Dispose();
    }
}
