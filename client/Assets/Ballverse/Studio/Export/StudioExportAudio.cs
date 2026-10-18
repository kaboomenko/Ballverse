using System;
using System.Threading;
using System.Threading.Tasks;

namespace Ballverse.Studio
{
    /// <summary><see cref="IExportAudioRenderer"/> over <see cref="StudioAudioMixer"/> for a project folder.</summary>
    public sealed class StudioExportAudio : IExportAudioRenderer
    {
        readonly StudioAudioMixer _mixer;
        readonly Func<string, string> _projectDirOf;

        /// <param name="mixer">The studio mixer.</param>
        /// <param name="projectDirOf">Absolute project folder of a project id (clip files and user video live there).</param>
        public StudioExportAudio(StudioAudioMixer mixer, Func<string, string> projectDirOf)
        {
            _mixer = mixer ?? throw new ArgumentNullException(nameof(mixer));
            _projectDirOf = projectDirOf ?? throw new ArgumentNullException(nameof(projectDirOf));
        }

        public Task<long> RenderMixAsync(Ballverse.Core.Studio.ExportJob job, string pcmPath, CancellationToken ct)
        {
            string dir = _projectDirOf(job.State.ProjectId);
            return _mixer.RenderToFileAsync(job.State.Project, job.Timeline, dir, dir, pcmPath, ct);
        }
    }
}
