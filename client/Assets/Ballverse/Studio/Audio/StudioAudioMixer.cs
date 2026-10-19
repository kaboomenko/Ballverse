using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Ballverse.Core.Audio;
using Ballverse.Core.Integration;
using Ballverse.Core.Projects;
using Ballverse.Core.RenderStream;
using Ballverse.Core.Replay;
using Ballverse.Core.Studio;
using Ballverse.Core.Timeline;
using Ballverse.Media;

namespace Ballverse.Studio
{
    /// <summary>Sound assets of a clip: the pack, its decoded samples and the melody (spec §2.7, §3.6).</summary>
    public sealed class StudioSoundAssets
    {
        public StudioSoundAssets(SoundPack pack, ISampleBank bank, Melody melody)
        {
            Pack = pack;
            Bank = bank;
            Melody = melody;
        }

        public SoundPack Pack { get; }
        public ISampleBank Bank { get; }
        public Melody Melody { get; }
    }

    /// <summary>
    /// Supplies the decoded audio assets the studio mixes (the App connects the audio track's loaders): the clip's sound pack +
    /// sample bank + melody, and built-in music tracks as PCM. Called on the main thread; the returned objects are then read
    /// from the mix worker thread, so they must not change afterwards.
    /// </summary>
    public interface IStudioSoundProvider
    {
        /// <summary>Pack named by <c>clip.soundPack</c> (and <c>clip.melody</c>), or null for no simulation sounds.</summary>
        Task<StudioSoundAssets> LoadSoundsAsync(string soundPackId, string melodyId, CancellationToken ct);

        /// <summary>Built-in music track as interleaved stereo float at 48 kHz from its start, or null when unknown.</summary>
        Task<float[]> LoadMusicAsync(string trackId, CancellationToken ct);
    }

    /// <summary>
    /// The studio's offline mix (spec §3.6, §7.5 step 3, §7.8) for preview and export: simulation events from the clip's replay
    /// (body size and pan from its render stream, <see cref="RenderStreamSoundBodySource"/>), the user video's audio decoded by the
    /// media bridge (to a PCM file, cached per video + trim), music from <see cref="IStudioSoundProvider"/>, mixed and mastered by
    /// Core <see cref="StudioMixBuilder"/> on a worker thread (−14 LUFS, −1 dBTP). Preview plays the buffer, export writes it to
    /// the job's PCM file — the same samples. Start calls on the main thread; the heavy part runs on the thread pool.
    /// <para>Concurrency: one mix runs at a time (a request waits for the running one; a cancelled waiter gives up at once), the
    /// running mix stops within about a second of its token being cancelled, and overlapping requests for the same user audio share
    /// one decode (each decode writes a file of its own and is renamed into the cache), so a superseded preview remix and an export
    /// can overlap safely.</para>
    /// </summary>
    public sealed class StudioAudioMixer
    {
        readonly IMediaBridge _bridge;
        readonly IStudioSoundProvider _sounds;
        readonly string _cacheDir;
        readonly object _gate = new object();
        readonly Dictionary<string, string> _decoded = new Dictionary<string, string>(StringComparer.Ordinal);
        readonly Dictionary<string, Task<string>> _decoding = new Dictionary<string, Task<string>>(StringComparer.Ordinal);
        readonly SemaphoreSlim _mixSlot = new SemaphoreSlim(1, 1);
        int _attempt;

        /// <param name="bridge">Media bridge (user audio decode).</param>
        /// <param name="sounds">Sound pack and music provider; null = no simulation sounds, no music.</param>
        /// <param name="cacheDir">Folder for decoded user audio (export scratch); created if missing.</param>
        public StudioAudioMixer(IMediaBridge bridge, IStudioSoundProvider sounds, string cacheDir)
        {
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
            _sounds = sounds;
            if (string.IsNullOrEmpty(cacheDir)) throw new ArgumentException("cache folder required", nameof(cacheDir));
            _cacheDir = cacheDir;
        }

        /// <summary>
        /// Renders the mix of <paramref name="doc"/> for <paramref name="timeline"/> (its fps sets the length).
        /// </summary>
        /// <param name="clipDir">Absolute clip folder (clip.bvr, clip.bvs).</param>
        /// <param name="projectDir">Absolute project folder (user video), or null without a video.</param>
        public async Task<StudioMixResult> RenderAsync(ProjectDocument doc, StudioTimeline timeline, string clipDir, string projectDir, CancellationToken ct)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (timeline == null) throw new ArgumentNullException(nameof(timeline));
            ct.ThrowIfCancellationRequested();
            bool hasVideo = doc.UserVideo != null && projectDir != null;
            var settings = ProjectComposition.BuildMixSettings(doc.Audio, hasVideo);

            string userPcm = null;
            if (hasVideo && settings.UserEnabled && timeline.Spec.Video != null)
            {
                string videoPath = Path.Combine(projectDir, doc.UserVideo.File.Replace('/', Path.DirectorySeparatorChar));
                userPcm = await DecodeUserAudioAsync(videoPath, timeline.Spec.Video, ct);
            }
            StudioSoundAssets sounds = null;
            float[] music = null;
            if (_sounds != null)
            {
                if (!string.IsNullOrEmpty(doc.Clip?.SoundPack) && settings.SimGain > 0f)
                    sounds = await _sounds.LoadSoundsAsync(doc.Clip.SoundPack, doc.Clip.Melody, ct);
                if (doc.Audio?.Music?.Track != null && settings.MusicGain > 0f)
                    music = await _sounds.LoadMusicAsync(doc.Audio.Music.Track, ct);
            }
            ct.ThrowIfCancellationRequested();
            string replayPath = Path.Combine(clipDir, doc.Clip?.Replay ?? ProjectPaths.ClipReplayFile);
            string streamPath = Path.Combine(clipDir, ProjectPaths.ClipRenderFile);
            await _mixSlot.WaitAsync(ct);
            try
            {
                return await Task.Run(() => Mix(timeline, settings, sounds, music, replayPath, streamPath, userPcm, ct), ct);
            }
            finally
            {
                _mixSlot.Release();
            }
        }

        /// <summary>Renders the mix and writes it to <paramref name="pcmPath"/> (Media <see cref="PcmFile"/> format); returns its frame count.</summary>
        public async Task<long> RenderToFileAsync(ProjectDocument doc, StudioTimeline timeline, string clipDir, string projectDir, string pcmPath, CancellationToken ct)
        {
            StudioMixResult mix = await RenderAsync(doc, timeline, clipDir, projectDir, ct);
            await Task.Run(() => PcmFile.Write(pcmPath, mix.Samples, 0, mix.Samples.Length), ct);
            return mix.FrameCount;
        }

        /// <summary>Deletes the cached decoded user audio files.</summary>
        public void ClearCache()
        {
            List<string> paths;
            lock (_gate)
            {
                paths = new List<string>(_decoded.Values);
                _decoded.Clear();
            }
            foreach (string path in paths)
            {
                try
                {
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (IOException)
                {
                    // removed with the export scratch at the next start
                }
            }
        }

        async Task<string> DecodeUserAudioAsync(string videoPath, UserVideoTiming video, CancellationToken ct)
        {
            if (!File.Exists(videoPath)) return null;
            var info = new FileInfo(videoPath);
            string key = videoPath + "|" + info.Length + "|" + info.LastWriteTimeUtc.Ticks + "|" + video.TrimInUs + "|" + video.LengthUs;
            Task<string> decode;
            lock (_gate)
            {
                if (_decoded.TryGetValue(key, out string cached) && File.Exists(cached)) return cached;
                if (!_decoding.TryGetValue(key, out decode))
                {
                    // Not cancelled by any one caller: others may be waiting for it, and the result is cached for the next remix.
                    decode = DecodeToCacheAsync(key, videoPath, video);
                    _decoding[key] = decode;
                }
            }
            return await WithCancellation(decode, ct);
        }

        async Task<string> DecodeToCacheAsync(string key, string videoPath, UserVideoTiming video)
        {
            try
            {
                Directory.CreateDirectory(_cacheDir);
                string final = Path.Combine(_cacheDir, "user_" + StableHash(key) + ".pcm");
                string attempt = Path.Combine(_cacheDir, "user_" + StableHash(key) + "_" + Interlocked.Increment(ref _attempt) + StudioFiles.TempSuffix + ".pcm");
                PcmFileInfo decoded = await _bridge.DecodeAudioToFileAsync(videoPath, attempt, new AudioDecodeRange(video.TrimInUs, video.LengthUs), CancellationToken.None);
                if (decoded == null || decoded.FrameCount <= 0)
                {
                    TryDelete(decoded?.Path ?? attempt);
                    return null;
                }
                string written = decoded.Path ?? attempt;
                if (File.Exists(final)) File.Delete(final);
                File.Move(written, final);
                lock (_gate) _decoded[key] = final;
                return final;
            }
            finally
            {
                lock (_gate) _decoding.Remove(key);
            }
        }

        static async Task<T> WithCancellation<T>(Task<T> task, CancellationToken ct)
        {
            if (!ct.CanBeCanceled || task.IsCompleted) return await task;
            var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (ct.Register(() => cancelled.TrySetResult(true)))
            {
                if (await Task.WhenAny(task, cancelled.Task) != task) ct.ThrowIfCancellationRequested();
            }
            return await task;
        }

        static void TryDelete(string path)
        {
            try
            {
                if (path != null && File.Exists(path)) File.Delete(path);
            }
            catch (IOException)
            {
                // export scratch is cleared at startup
            }
        }

        static StudioMixResult Mix(StudioTimeline timeline, Ballverse.Core.Audio.Mastering.TrackMixSettings settings, StudioSoundAssets sounds,
            float[] music, string replayPath, string streamPath, string userPcm, CancellationToken ct)
        {
            IReadOnlyList<SoundEvent> events = null;
            if (sounds?.Pack != null && sounds.Bank != null && File.Exists(replayPath))
            {
                ReplayFile replay;
                using (var fs = new FileStream(replayPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    replay = ReplayReader.Read(fs);
                }
                ct.ThrowIfCancellationRequested();
                if (File.Exists(streamPath))
                {
                    using (var stream = new FileStream(streamPath, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.RandomAccess))
                    using (var reader = new RenderStreamReader(stream, true))
                    {
                        events = ReplaySoundEvents.Convert(replay, new RenderStreamSoundBodySource(reader));
                    }
                }
                else
                {
                    events = ReplaySoundEvents.Convert(replay, new UniformSoundBodySource(RenderStreamSoundBodySource.DefaultSize));
                }
            }
            ct.ThrowIfCancellationRequested();
            PcmFileSource user = userPcm != null && File.Exists(userPcm) ? new PcmFileSource(userPcm) : null;
            try
            {
                return StudioMixBuilder.Render(new StudioMixInputs
                {
                    Timeline = timeline,
                    SimEvents = events,
                    Pack = sounds?.Pack,
                    Bank = sounds?.Bank,
                    Melody = sounds?.Melody,
                    UserAudio = user,
                    Music = music,
                    Settings = settings,
                }, ct);
            }
            finally
            {
                user?.Dispose();
            }
        }

        static string StableHash(string s)
        {
            ulong h = 1469598103934665603UL;
            foreach (char c in s)
            {
                h ^= c;
                h *= 1099511628211UL;
            }
            return h.ToString("x16", System.Globalization.CultureInfo.InvariantCulture);
        }
    }
}
