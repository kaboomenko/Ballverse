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
    /// Incremental: every look change is decoded once when the clip opens; <see cref="Advance"/> finds the tick's position by
    /// binary search and applies only the changes since the last call to a running look (a backward seek replays the decoded
    /// values from the start: array writes, no parsing). It allocates nothing while the look does not change and one small
    /// look-only <see cref="ParamSet"/> on a frame that crosses changes (however many: changes within one frame coalesce). Main
    /// thread.
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
        // Decoded look changes, stable by tick (record order within a tick, as ReplayParams.ParamsAtTick applies them).
        readonly uint[] _ticks;
        readonly int[] _params;
        readonly ParamValue[] _values;
        // Look values of the start set by LookParams index (null = absent) and the running look.
        readonly ParamValue[] _start;
        readonly ParamValue[] _current;
        readonly List<KeyValuePair<string, ParamValue>> _scratch;
        int _applied = -1;

        public ClipLookTimeline(ParamSet initial, IReadOnlyList<ReplayParamChange> changes, ParamSchema schema)
        {
            _initial = initial ?? throw new ArgumentNullException(nameof(initial));
            if (schema == null) throw new ArgumentNullException(nameof(schema));
            var decoded = new List<KeyValuePair<uint, KeyValuePair<int, ParamValue>>>();
            if (changes != null)
            {
                foreach (ReplayParamChange c in changes)
                {
                    if (c == null) continue;
                    int index = LookIndex(c.ParamId);
                    if (index < 0 || !ReplayParams.TryReadParamChange(c, schema, out ParamValue v, out _)) continue;
                    decoded.Add(new KeyValuePair<uint, KeyValuePair<int, ParamValue>>(c.Tick, new KeyValuePair<int, ParamValue>(index, v)));
                }
            }
            // Stable insertion sort by tick (records are written in tick order, so this is linear in practice).
            for (int i = 1; i < decoded.Count; i++)
            {
                var c = decoded[i];
                int j = i - 1;
                while (j >= 0 && decoded[j].Key > c.Key)
                {
                    decoded[j + 1] = decoded[j];
                    j--;
                }
                decoded[j + 1] = c;
            }
            _ticks = new uint[decoded.Count];
            _params = new int[decoded.Count];
            _values = new ParamValue[decoded.Count];
            for (int i = 0; i < decoded.Count; i++)
            {
                _ticks[i] = decoded[i].Key;
                _params[i] = decoded[i].Value.Key;
                _values[i] = decoded[i].Value.Value;
            }
            _start = new ParamValue[LookParams.Count];
            _current = new ParamValue[LookParams.Count];
            for (int i = 0; i < _start.Length; i++)
            {
                if (initial.TryGet(LookParams[i], out ParamValue v)) _start[i] = v;
            }
            _scratch = new List<KeyValuePair<string, ParamValue>>(LookParams.Count);
        }

        public ParamSet Initial => _initial;

        /// <summary>Decodable look changes recorded in the clip.</summary>
        public int ChangeCount => _ticks.Length;

        public static bool TryRead(string clipDirectory, ModeRegistry modes, out ClipLookTimeline timeline)
        {
            timeline = null;
            if (!ClipParams.TryRead(clipDirectory, modes, out IModeDefinition mode, out ParamSet values, out _, out IReadOnlyList<ReplayParamChange> changes))
                return false;
            timeline = new ClipLookTimeline(values, changes, mode.Schema);
            return true;
        }

        /// <summary>
        /// The look in effect during <paramref name="tick"/> (the look parameters only — what <c>LookSettings.Apply</c> reads):
        /// true (with <paramref name="values"/>) on the first call and whenever it differs from the last call's, else false.
        /// </summary>
        public bool Advance(int tick, out ParamSet values)
        {
            int count = CountAt(tick);
            if (count == _applied)
            {
                values = null;
                return false;
            }
            int from = _applied;
            if (from < 0 || count < from)
            {
                Array.Copy(_start, _current, _start.Length);
                from = 0;
            }
            for (int i = from; i < count; i++) _current[_params[i]] = _values[i];
            _applied = count;
            _scratch.Clear();
            for (int i = 0; i < _current.Length; i++)
            {
                if (_current[i] != null) _scratch.Add(new KeyValuePair<string, ParamValue>(LookParams[i], _current[i]));
            }
            values = new ParamSet(_scratch);
            return true;
        }

        /// <summary>The whole parameter set at <paramref name="tick"/>: the start values with the look changes applied (no state change).</summary>
        public ParamSet ValuesAt(int tick)
        {
            int count = CountAt(tick);
            if (count == 0) return _initial;
            var look = new ParamValue[LookParams.Count];
            for (int i = 0; i < count; i++) look[_params[i]] = _values[i];
            var overlay = new List<KeyValuePair<string, ParamValue>>();
            for (int i = 0; i < look.Length; i++)
            {
                if (look[i] != null) overlay.Add(new KeyValuePair<string, ParamValue>(LookParams[i], look[i]));
            }
            return _initial.Overlay(new ParamSet(overlay));
        }

        /// <summary>Number of changes whose tick is ≤ <paramref name="tick"/> (binary search; a change applies at its tick's boundary).</summary>
        int CountAt(int tick)
        {
            uint t = (uint)Math.Max(0, tick);
            int lo = 0, hi = _ticks.Length;
            while (lo < hi)
            {
                int mid = (lo + hi) >> 1;
                if (_ticks[mid] <= t) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        static int LookIndex(string id)
        {
            for (int i = 0; i < LookParams.Count; i++)
            {
                if (string.Equals(LookParams[i], id, StringComparison.Ordinal)) return i;
            }
            return -1;
        }
    }
}
