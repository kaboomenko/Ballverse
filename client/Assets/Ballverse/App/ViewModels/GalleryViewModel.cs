using System;
using System.Collections.Generic;
using Ballverse.Core.Modes;
using Ballverse.Core.Params;
using Ballverse.Core.Params.Presets;
using Ballverse.UI.ViewModels;
using UnityEngine;

namespace Ballverse.App
{
    /// <summary>
    /// Tab «Режимы» (spec §4.3, release 1: every mode free, no locks, no prices): one card per registered, released mode that
    /// Remote Config has not switched off, in <c>gallery_order</c>. New modes appear as soon as they are registered in
    /// <see cref="ModeRegistry"/> (Core <c>ReleaseModes</c>). Previews loop while the gallery is visible.
    /// </summary>
    public sealed class GalleryViewModel : ViewModelBase, IGalleryViewModel
    {
        readonly AppContext _ctx;
        readonly SimulationViewModel _sim;
        readonly List<ModeCardData> _cards = new List<ModeCardData>();
        bool _previewsActive;

        public GalleryViewModel(AppContext ctx, SimulationViewModel sim)
            : base(ctx.Notices.Report)
        {
            _ctx = ctx;
            _sim = sim;
            ctx.Remote.Changed += Rebuild;
            if (ctx.Previews != null) ctx.Previews.Changed += Notify;
            ctx.TierChanged += _ => RefreshPreviews();
            Rebuild();
        }

        public IReadOnlyList<ModeCardData> Modes => _cards;

        public Texture GetPreview(string modeId) => _ctx.Previews?.Get(modeId);

        public void SetPreviewsActive(bool active)
        {
            _previewsActive = active;
            if (_ctx.Previews != null) _ctx.Previews.Active = active;
        }

        public void OpenMode(string modeId)
        {
            SetPreviewsActive(false);
            _sim.Open(modeId);
        }

        /// <summary>Advances the previews (main thread, every frame).</summary>
        public void Tick(float deltaSeconds)
        {
            if (!_previewsActive || _ctx.Previews == null || _ctx.AppPaused) return;
            try
            {
                _ctx.Previews.Update(deltaSeconds, Math.Max(1, _ctx.FrameRate.TargetFps), _ctx.Clock.NowSeconds);
            }
            catch (Exception e) when (e is InvalidOperationException || e is ArgumentException)
            {
                _ctx.Notices.Report("gallery.previews", e);
                _previewsActive = false;
            }
        }

        void Rebuild()
        {
            _cards.Clear();
            foreach (IModeDefinition m in _ctx.Remote.GalleryModes())
            {
                _cards.Add(ModeCardData.ForMode(m.Info.Id, m.Info.Interactive, m.Info.HasWinner, false));
            }
            RefreshPreviews();
            Notify();
        }

        void RefreshPreviews()
        {
            if (_ctx.Previews == null) return;
            var modes = new List<KeyValuePair<IModeDefinition, ParamSet>>();
            foreach (IModeDefinition m in _ctx.Remote.GalleryModes())
            {
                try
                {
                    Preset first = m.BuiltInPresets.Count > 0 ? m.BuiltInPresets[0] : null;
                    ParamSet values = first != null ? ModeParams.ApplyPreset(m, first, _ctx.Tier).Values : ModeParams.Defaults(m, _ctx.Tier);
                    modes.Add(new KeyValuePair<IModeDefinition, ParamSet>(m, values));
                }
                catch (Exception e) when (e is ArgumentException || e is InvalidOperationException)
                {
                    _ctx.Notices.Report("gallery.preset." + m.Info.Id, e);
                }
            }
            _ctx.Previews.SetModes(modes, _ctx.Tier);
        }
    }
}
