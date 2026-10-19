using System;
using System.Threading;
using System.Threading.Tasks;

namespace Ballverse.Studio
{
    /// <summary>
    /// Rebuilds a clip's render stream from its replay (spec §7.5 preparation step 2 "Поток рендера: есть? иначе — пересчёт из
    /// реплея + сверка хэшей", §3.1 "без повторного запуска"). Render streams are a cache: <c>RunRetention</c> drops those of older
    /// runs beyond 500 MB and the OS may purge the cache folder, while the replay always stays. The App implements this on top of
    /// the simulation (Core.Sim re-runs the replay's seed and parameters, writes <c>clip.bvs</c> and <c>clip.bve</c> with the
    /// mode's recorder, and checks the replay's checkpoint hashes); the studio calls it whenever a clip without a stream is opened,
    /// exported or turned into a project.
    /// </summary>
    public interface IRenderStreamRebuilder
    {
        /// <summary>
        /// Re-simulates <paramref name="replayPath"/> and writes the render stream to <paramref name="renderStreamPath"/> and the
        /// render events to <paramref name="eventsPath"/> (both are temporary paths the studio renames into place on success).
        /// Throws <see cref="System.IO.InvalidDataException"/> when the replay is corrupt or the hashes do not match (the
        /// simulation is not reproducible on this build), <see cref="OperationCanceledException"/> on cancel. May run on a worker
        /// thread; completes on any thread.
        /// </summary>
        /// <param name="progress">0..1, optional.</param>
        Task RebuildAsync(string replayPath, string renderStreamPath, string eventsPath, IProgress<double> progress, CancellationToken ct);
    }

    /// <summary>Whether a run or saved clip can be turned into a project now (spec §3.1 "В видео").</summary>
    public enum ClipReadiness
    {
        /// <summary>Replay and render stream are there.</summary>
        Ready = 0,
        /// <summary>The render stream was evicted; it is rebuilt from the replay when the project is opened (takes a moment).</summary>
        NeedsRebuild = 1,
        /// <summary>No render stream and no way to rebuild it (no rebuilder connected), or no replay: "В видео" is not offered.</summary>
        Unavailable = 2,
        /// <summary>No such run or clip.</summary>
        Missing = 3,
    }

    /// <summary>The clip's render stream is missing and cannot be rebuilt.</summary>
    public sealed class RenderStreamUnavailableException : InvalidOperationException
    {
        public RenderStreamUnavailableException(string message)
            : base(message)
        {
        }
    }
}
