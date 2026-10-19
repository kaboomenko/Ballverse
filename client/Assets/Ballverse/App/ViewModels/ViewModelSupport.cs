using System;
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
            mode = null;
            values = null;
            seed = 0;
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
}
