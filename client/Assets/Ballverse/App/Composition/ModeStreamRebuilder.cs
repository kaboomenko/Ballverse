using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ballverse.Core.Modes;
using Ballverse.Core.RenderGeometry;
using Ballverse.Core.Replay;
using Ballverse.Studio;

namespace Ballverse.App
{
    /// <summary>
    /// Studio's <see cref="IRenderStreamRebuilder"/> over the mode framework (spec §7.4 "поток рендера — кэш, восстанавливается из
    /// реплея пересчётом с проверкой хэшей"): Core <see cref="ModeReplay.Resimulate(IModeDefinition, byte[], ModeReplayOptions)"/>
    /// re-runs the replay's mode, seed, parameters, inputs and live edits with the same code as the live run, writes the render
    /// stream and the render events, and verifies every recorded HASH. A replay of another mode version, or one whose hashes do
    /// not match on this build, is refused (<see cref="InvalidDataException"/>). Runs on a worker thread.
    /// </summary>
    public sealed class ModeStreamRebuilder : IRenderStreamRebuilder
    {
        readonly ModeRegistry _modes;

        public ModeStreamRebuilder(ModeRegistry modes)
        {
            _modes = modes ?? throw new ArgumentNullException(nameof(modes));
        }

        public Task RebuildAsync(string replayPath, string renderStreamPath, string eventsPath, IProgress<double> progress, CancellationToken ct)
        {
            return Task.Run(() => Rebuild(replayPath, renderStreamPath, eventsPath, progress, ct), ct);
        }

        /// <summary>The synchronous rebuild (tests, or callers already on a worker).</summary>
        public void Rebuild(string replayPath, string renderStreamPath, string eventsPath, IProgress<double> progress, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            byte[] bvr = File.ReadAllBytes(replayPath);
            ReplayFile replay;
            try
            {
                replay = ReplayReader.Read(bvr);
            }
            catch (Exception e) when (e is FormatException || e is ArgumentException || e is UnsupportedReplayVersionException || e is EndOfStreamException)
            {
                throw new InvalidDataException("Unreadable replay: " + e.Message, e);
            }
            if (!_modes.TryGet(replay.Header.ModeId, out IModeDefinition mode))
                throw new InvalidDataException("The replay's mode '" + replay.Header.ModeId + "' is not in this build");
            progress?.Report(0.05);
            var events = new RenderEventLog(1024);
            ModeReplayResult result;
            using (var stream = new FileStream(renderStreamPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 64 * 1024))
            {
                result = ModeReplay.Resimulate(mode, replay, new ModeReplayOptions
                {
                    RenderStream = stream,
                    LeaveRenderStreamOpen = true,
                    RenderEvents = events,
                    StopAtFirstMismatch = true,
                });
            }
            ct.ThrowIfCancellationRequested();
            if (!result.IsVerified)
            {
                TryDelete(renderStreamPath);
                throw new InvalidDataException("Replay re-simulation " + result.Status + ": " + result.Message);
            }
            using (var fs = new FileStream(eventsPath, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024))
            {
                events.WriteTo(fs);
            }
            progress?.Report(1.0);
        }

        static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
                // the studio deletes its temporary paths itself
            }
        }
    }
}
