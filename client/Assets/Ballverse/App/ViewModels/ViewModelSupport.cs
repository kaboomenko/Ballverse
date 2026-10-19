using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core.Integration;
using Ballverse.Core.Modes;
using Ballverse.Core.Params;
using Ballverse.Core.Projects;
using Ballverse.Core.Replay;
using Ballverse.Sim;
using Ballverse.Studio;
using Ballverse.UI.ViewModels;

namespace Ballverse.App
{
    /// <summary>Base of the App's view models: <see cref="Changed"/> raised on the main thread; a throwing subscriber is reported.</summary>
    public abstract class ViewModelBase : IObservableViewModel
    {
        Action<string, Exception> _report;

        protected ViewModelBase(Action<string, Exception> report)
        {
            _report = report;
        }

        public event Action Changed;

        protected void Notify()
        {
            Action handler = Changed;
            if (handler == null) return;
            foreach (Delegate d in handler.GetInvocationList())
            {
                try
                {
                    ((Action)d)();
                }
                catch (Exception e)
                {
                    _report?.Invoke(GetType().Name + ".Changed", e);
                }
            }
        }
    }

    /// <summary><see cref="ILiveRunRecording"/> over Studio's <see cref="RunRecording"/> (recent-runs ring).</summary>
    public sealed class RunRecordingAdapter : ILiveRunRecording
    {
        readonly RunRecording _run;

        public RunRecordingAdapter(RunRecording run)
        {
            _run = run ?? throw new ArgumentNullException(nameof(run));
        }

        public string ReplayPath => _run.ReplayPath;
        public string RenderStreamPath => _run.RenderStreamPath;
        public string EventsPath => _run.EventsPath;

        public string Commit(string modeId, int durationTicks, string winnerId)
        {
            return _run.Commit(modeId, durationTicks, winnerId).Id;
        }

        public void Abandon() => _run.Abandon();
    }

    /// <summary>Reads the mode, parameters and seed of a recorded clip (its <c>clip.bvr</c>) to run it again (spec §3.4 "Перегенерировать").</summary>
    public static class ClipParams
    {
        public static bool TryRead(string clipDirectory, ModeRegistry modes, out IModeDefinition mode, out ParamSet values, out ulong seed)
        {
            return TryRead(clipDirectory, modes, out mode, out values, out seed, out _);
        }

        /// <summary>As above, also returning the replay's live parameter changes (PARM records, in record order).</summary>
        public static bool TryRead(string clipDirectory, ModeRegistry modes, out IModeDefinition mode, out ParamSet values, out ulong seed,
            out IReadOnlyList<ReplayParamChange> changes)
        {
            mode = null;
            values = null;
            seed = 0;
            changes = Array.Empty<ReplayParamChange>();
            if (string.IsNullOrEmpty(clipDirectory)) return false;
            string path = Path.Combine(clipDirectory, ProjectPaths.ClipReplayFile);
            try
            {
                if (!File.Exists(path)) return false;
                ReplayFile replay = ReplayReader.Read(File.ReadAllBytes(path));
                if (!modes.TryGet(replay.Header.ModeId, out mode)) return false;
                ReplayParamsResult stored = ReplayParams.ReadParams(replay.Header, mode.Schema);
                seed = replay.Header.Seed;
                values = stored.Values.With(CommonParams.Seed, ParamValue.Seed(seed));
                changes = replay.ParamChanges;
                return true;
            }
            catch (Exception e) when (e is IOException || e is InvalidDataException || e is UnauthorizedAccessException
                                      || e is ArgumentException || e is FormatException || e is UnsupportedReplayVersionException)
            {
                mode = null;
                values = null;
                return false;
            }
        }
    }

    /// <summary>
    /// The look of a clip over time (spec §2.2: a live look change during the run — theme, colours, trail, glow… — is recorded
    /// as PARM and the export repeats it): the replay's start values with the look PARM records applied up to the shown tick.
    /// <see cref="Advance"/> is allocation-free while the look does not change (seeking backwards recomputes). Main thread.
    /// </summary>
    public sealed class ClipLookTimeline
    {
        /// <summary>Parameters the renderer's look reads (Core <c>LookSettings.Apply</c>).</summary>
        public static readonly IReadOnlyList<string> LookParams = Array.AsReadOnly(new[]
        {
            CommonParams.Theme, CommonParams.BallColors, CommonParams.BallSkin, CommonParams.Trail, CommonParams.Background,
            CommonParams.BackgroundColor, CommonParams.Glow, CommonParams.ImpactFx, Ballverse.Core.RenderGeometry.LookSettings.BreakFxParam,
            CommonParams.Labels, CommonParams.Hud,
        });

        readonly ParamSet _initial;
        readonly ParamSchema _schema;
        readonly List<ReplayParamChange> _changes;
        int _applied = -1;

        public ClipLookTimeline(ParamSet initial, IReadOnlyList<ReplayParamChange> changes, ParamSchema schema)
        {
            _initial = initial ?? throw new ArgumentNullException(nameof(initial));
            _schema = schema ?? throw new ArgumentNullException(nameof(schema));
            _changes = new List<ReplayParamChange>();
            if (changes != null)
            {
                foreach (ReplayParamChange c in changes)
                {
                    if (c != null && IsLook(c.ParamId)) _changes.Add(c);
                }
            }
            // Stable by tick (record order within a tick), as ReplayParams.ParamsAtTick expects.
            for (int i = 1; i < _changes.Count; i++)
            {
                ReplayParamChange c = _changes[i];
                int j = i - 1;
                while (j >= 0 && _changes[j].Tick > c.Tick)
                {
                    _changes[j + 1] = _changes[j];
                    j--;
                }
                _changes[j + 1] = c;
            }
        }

        public ParamSet Initial => _initial;

        /// <summary>Look changes recorded in the clip.</summary>
        public int ChangeCount => _changes.Count;

        public static bool TryRead(string clipDirectory, ModeRegistry modes, out ClipLookTimeline timeline)
        {
            timeline = null;
            if (!ClipParams.TryRead(clipDirectory, modes, out IModeDefinition mode, out ParamSet values, out _, out IReadOnlyList<ReplayParamChange> changes))
                return false;
            timeline = new ClipLookTimeline(values, changes, mode.Schema);
            return true;
        }

        /// <summary>
        /// The look in effect during <paramref name="tick"/>: true (with <paramref name="values"/>) on the first call and whenever
        /// it differs from the last call's, else false.
        /// </summary>
        public bool Advance(int tick, out ParamSet values)
        {
            int count = 0;
            while (count < _changes.Count && _changes[count].Tick <= (uint)Math.Max(0, tick)) count++;
            if (count == _applied)
            {
                values = null;
                return false;
            }
            _applied = count;
            values = count == 0 ? _initial : ReplayParams.ParamsAtTick(_initial, _changes, (uint)Math.Max(0, tick), _schema);
            return true;
        }

        /// <summary>The look values at <paramref name="tick"/> (no state change).</summary>
        public ParamSet ValuesAt(int tick)
        {
            return ReplayParams.ParamsAtTick(_initial, _changes, (uint)Math.Max(0, tick), _schema);
        }

        static bool IsLook(string id)
        {
            for (int i = 0; i < LookParams.Count; i++)
            {
                if (string.Equals(LookParams[i], id, StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }
}
