using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Config;
using Ballverse.Core.Generation;
using Ballverse.Core.Integration;
using Ballverse.Core.Layout;
using Ballverse.Core.Modes;
using Ballverse.Core.Params;
using Ballverse.Core.Params.Presets;
using Ballverse.Core.Timeline;

namespace Ballverse.App
{
    /// <summary>
    /// The App's typed view of Remote Config (spec §9.3), rebuilt whenever the services deliver new values: gallery order and
    /// kill switch, generation budgets and spectacle weights, export bitrates and default fps, platform UI zones, the watermark
    /// kill switch and remote presets. Ad policies and the release catalog (free modes, disabled modes) are applied by the
    /// services themselves. Every adapter failure keeps the built-in value (reported).
    /// </summary>
    public sealed class RemoteSettings
    {
        readonly ModeRegistry _modes;
        readonly Action<string, Exception> _report;

        public RemoteSettings(ModeRegistry modes, RemoteConfigValues initial, Action<string, Exception> report)
        {
            _modes = modes ?? throw new ArgumentNullException(nameof(modes));
            _report = report ?? ((c, e) => { });
            Apply(initial ?? RemoteConfigValues.Defaults);
        }

        public RemoteConfigValues Values { get; private set; }
        public GenerationBudgetTable Budgets { get; private set; } = GenerationBudgetTable.Default;
        public SpectacleProfileTable Profiles { get; private set; }
        public ExportBitrateTable Bitrates { get; private set; }
        public PlatformUiMasks Masks { get; private set; } = PlatformUiMasks.Default;
        /// <summary>Union of every platform's UI zones, normalized (preview overlay, PiP magnets, export masks).</summary>
        public IReadOnlyList<FloatRect> MaskUnion { get; private set; } = Array.Empty<FloatRect>();
        public bool WatermarkEnabled { get; private set; } = true;
        public IReadOnlyList<Preset> RemotePresets { get; private set; } = Array.Empty<Preset>();

        /// <summary>Raised after <see cref="Apply"/> (main thread).</summary>
        public event Action Changed;

        public void Apply(RemoteConfigValues values)
        {
            if (values == null) return;
            Values = values;
            try
            {
                Budgets = RemoteConfigAdapters.ToGenerationBudgets(values);
            }
            catch (ArgumentException e)
            {
                _report("remote.budgets", e);
                Budgets = GenerationBudgetTable.Default;
            }
            try
            {
                AdapterResult<SpectacleProfileTable> profiles = RemoteConfigAdapters.ToSpectacleProfiles(values);
                Profiles = profiles.Value;
            }
            catch (ArgumentException e)
            {
                _report("remote.profiles", e);
                Profiles = null;
            }
            try
            {
                Bitrates = RemoteConfigAdapters.ToExportBitrates(values);
            }
            catch (ArgumentException e)
            {
                _report("remote.bitrates", e);
                Bitrates = null;
            }
            try
            {
                Masks = RemoteConfigAdapters.ToPlatformUiMasks(values);
                MaskUnion = Masks.Union();
            }
            catch (ArgumentException e)
            {
                _report("remote.masks", e);
                Masks = PlatformUiMasks.Default;
                MaskUnion = Masks.Union();
            }
            WatermarkEnabled = values.WatermarkEnabled;
            try
            {
                RemotePresetLoadResult presets = RemoteConfigAdapters.LoadRemotePresets(values, SchemaFor);
                RemotePresets = presets.Accepted;
            }
            catch (Exception e) when (e is ArgumentException || e is FormatException || e is PresetException)
            {
                _report("remote.presets", e);
                RemotePresets = Array.Empty<Preset>();
            }
            Changed?.Invoke();
        }

        /// <summary>Modes of the gallery: registered, released and not switched off, in <c>gallery_order</c> (then code order).</summary>
        public IReadOnlyList<IModeDefinition> GalleryModes()
        {
            IReadOnlyList<IModeDefinition> available = _modes.Available(Values.ModesDisabled);
            var ordered = new List<IModeDefinition>(available.Count);
            foreach (ModeCode code in Values.GalleryOrder)
            {
                foreach (IModeDefinition m in available)
                {
                    if (m.Info.Code == code && m.Info.InRelease && !ordered.Contains(m)) ordered.Add(m);
                }
            }
            foreach (IModeDefinition m in available)
            {
                if (m.Info.InRelease && !ordered.Contains(m)) ordered.Add(m);
            }
            return ordered;
        }

        public bool IsAvailable(string modeId, out IModeDefinition mode)
        {
            foreach (IModeDefinition m in GalleryModes())
            {
                if (m.Info.Id == modeId)
                {
                    mode = m;
                    return true;
                }
            }
            mode = null;
            return false;
        }

        /// <summary>Remote presets of one mode.</summary>
        public IReadOnlyList<Preset> RemotePresetsFor(string modeId)
        {
            var list = new List<Preset>();
            foreach (Preset p in RemotePresets)
            {
                if (p.ModeId == modeId) list.Add(p);
            }
            return list;
        }

        ParamSchema SchemaFor(string modeId)
        {
            return _modes.TryGet(modeId, out IModeDefinition m) ? m.Schema : null;
        }
    }
}
