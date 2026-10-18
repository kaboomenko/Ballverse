using System;
using System.Collections.Generic;
using Ballverse.Core;
using Ballverse.Core.Params;
using Ballverse.Core.Params.Presets;
using Ballverse.UI.Layout;
using Ballverse.UI.Localization;
using UnityEngine;

namespace Ballverse.UI.ViewModels
{
    /// <summary>First-run hints shown one after another (spec §4.3 "Первый запуск").</summary>
    public enum HintStep
    {
        None = 0,
        /// <summary>"Нажми 🎲 — новая симуляция".</summary>
        Generate = 1,
        /// <summary>"Крути настройки здесь".</summary>
        Settings = 2,
        /// <summary>"Понравилось? Сделай видео".</summary>
        ToVideo = 3,
    }

    public enum ArenaPointerPhase
    {
        Down = 0,
        Move = 1,
        Up = 2,
        Cancel = 3,
    }

    /// <summary>Choice in the long-press generation mini sheet (style or duration).</summary>
    public sealed class GenerationChoice
    {
        public string Id { get; }
        public LocalizedText Label { get; }

        public GenerationChoice(string id, LocalizedText label)
        {
            Id = id;
            Label = label;
        }
    }

    /// <summary>One line of the result card.</summary>
    public sealed class ResultStat
    {
        public string LabelKey { get; }
        public LocalizedText Value { get; }

        public ResultStat(string labelKey, LocalizedText value)
        {
            LabelKey = labelKey;
            Value = value;
        }
    }

    /// <summary>Result card content (spec §4.3 "Экран результата").</summary>
    public sealed class SimResultData
    {
        /// <summary>e.g. key "result.winner" with the winner's name, or "result.finished".</summary>
        public LocalizedText Headline { get; }
        /// <summary>Winner colour dot, or null.</summary>
        public ColorRgba? WinnerColor { get; }
        public long DurationMs { get; }
        public IReadOnlyList<ResultStat> Stats { get; }

        public SimResultData(LocalizedText headline, ColorRgba? winnerColor, long durationMs, IReadOnlyList<ResultStat> stats)
        {
            Headline = headline;
            WinnerColor = winnerColor;
            DurationMs = durationMs;
            Stats = stats ?? Array.Empty<ResultStat>();
        }
    }

    /// <summary>One of the last 5 runs kept in the ring buffer (spec §3.1).</summary>
    public sealed class RunThumbData
    {
        public int Index { get; }
        public Texture Thumbnail { get; }
        public long DurationMs { get; }
        public bool IsCurrent { get; }

        public RunThumbData(int index, Texture thumbnail, long durationMs, bool isCurrent)
        {
            Index = index;
            Thumbnail = thumbnail;
            DurationMs = durationMs;
            IsCurrent = isCurrent;
        }
    }

    /// <summary>Optional, collapsible "first video" quest (spec §4.3).</summary>
    public sealed class FirstVideoQuestData
    {
        public bool RanSimulation { get; }
        public bool Generated { get; }
        public bool Exported { get; }
        public bool Collapsed { get; }

        public FirstVideoQuestData(bool ranSimulation, bool generated, bool exported, bool collapsed)
        {
            RanSimulation = ranSimulation;
            Generated = generated;
            Exported = exported;
            Collapsed = collapsed;
        }
    }

    /// <summary>Simulation screen (spec §4.3): full-screen render target, floating glass bars, result card.</summary>
    public interface ISimulationViewModel : IObservableViewModel
    {
        string ModeId { get; }
        string ModeNameKey { get; }
        /// <summary>Texture the simulation renders into (shown edge to edge with "cover" fit).</summary>
        Texture RenderTarget { get; }
        SimPhaseView Phase { get; }
        bool IsPlaying { get; }
        bool IsInteractive { get; }
        /// <summary>Playback speed multiplier shown on the ⚡ button.</summary>
        float Speed { get; }
        bool IsGenerating { get; }
        /// <summary>0–1 on tier C (spec §2.4 "анимация + прогресс"), negative when only the button animates.</summary>
        float GenerationProgress { get; }
        /// <summary>Non-null once the run finished (shows the result card).</summary>
        SimResultData Result { get; }
        IReadOnlyList<RunThumbData> PreviousRuns { get; }
        HintStep ActiveHint { get; }
        /// <summary>Null hides the quest card.</summary>
        FirstVideoQuestData Quest { get; }
        IReadOnlyList<GenerationChoice> GenerationStyles { get; }
        IReadOnlyList<GenerationChoice> GenerationDurations { get; }
        string SelectedStyleId { get; }
        string SelectedDurationId { get; }

        IParamPanelViewModel Params { get; }
        IPresetsViewModel Presets { get; }

        /// <summary>One-off messages (e.g. "try a longer duration" after a weak generation); shown as toasts.</summary>
        event Action<LocalizedText> Notice;

        /// <summary>Leaving the screen (a natural pause for a pending interstitial, spec §5.3.2 IN2).</summary>
        void Back();
        void Retry();
        void TogglePlay();
        void Pause();
        /// <summary>Short press on 🎲: generate with the current director settings.</summary>
        void Generate();
        /// <summary>From the long-press mini sheet.</summary>
        void Generate(string styleId, string durationId);
        void CycleSpeed();
        /// <summary>"В видео": turn the current replay into a clip; the UI then opens the studio.</summary>
        void ToVideo();
        void SaveClip();
        void SelectPreviousRun(int index);
        void DismissHint();
        void SetQuestCollapsed(bool collapsed);
        /// <summary>
        /// Touch on the arena of an interactive mode; <paramref name="uv"/> is 0–1 over the render target, origin
        /// bottom-left. The UI already drops presses that start within 24 pt of a screen edge (spec §2.6 dead zone for
        /// system gestures) or in the bar strips (spec §4.3); moves and the release of an accepted press are always sent.
        /// </summary>
        void OnArenaPointer(ArenaPointerPhase phase, Vector2 uv, int pointerId);
        /// <summary>The screen became visible / hidden (pause rendering and cancel generation when hidden, spec §2.4).</summary>
        void SetScreenActive(bool active);
    }

    /// <summary>Settings sheet over the simulation (spec §2.2 panel rules, §4.3).</summary>
    public interface IParamPanelViewModel : IObservableViewModel
    {
        /// <summary>Merged schema of the current mode (common + mode layer).</summary>
        ParamSchema Schema { get; }
        /// <summary>Current applied values (sanitized, complete).</summary>
        ParamSet Values { get; }
        DeviceTier Tier { get; }
        /// <summary>Countdown / Running / Finale: structural edits need a restart.</summary>
        bool IsRunning { get; }
        /// <summary>Apply one sanitized value now: a live edit (replay input event) or a scene rebuild in Setup / Finished.</summary>
        void Apply(string id, ParamValue value);
        /// <summary>Restart the run with these values (structural edits confirmed by the user).</summary>
        void RestartWith(ParamSet values);
        /// <summary>"Сбросить к пресету".</summary>
        void ResetToPreset();
    }

    public sealed class PresetItemData
    {
        public string Id { get; }
        public LocalizedText Name { get; }
        public PresetSource Source { get; }
        public bool IsNew { get; }
        public Texture Thumbnail { get; }

        public PresetItemData(string id, LocalizedText name, PresetSource source, bool isNew, Texture thumbnail)
        {
            Id = id;
            Name = name;
            Source = source;
            IsNew = isNew;
            Thumbnail = thumbnail;
        }
    }

    /// <summary>Presets sheet (spec §2.3).</summary>
    public interface IPresetsViewModel : IObservableViewModel
    {
        IReadOnlyList<PresetItemData> Presets { get; }
        string SelectedId { get; }
        /// <summary>Name in the top bar ("Классика", or the user's name).</summary>
        LocalizedText CurrentName { get; }
        /// <summary>Values differ from the selected preset (shows "изменено").</summary>
        bool IsModified { get; }
        int UserPresetCount { get; }
        int MaxUserPresets { get; }
        void Apply(string id);
        UserPresetError SaveCurrentAs(string name);
        UserPresetError Rename(string id, string name);
        void Delete(string id);
    }
}
