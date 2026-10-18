using System;
using System.Collections.Generic;
using System.IO;
using Ballverse.Core.Projects;
using Ballverse.Core.RenderStream;
using Ballverse.Core.Studio;

namespace Ballverse.Studio
{
    /// <summary>
    /// An opened clip folder (project, run or saved clip): random-access render stream reader (spec §7.4 "перемотка по опорным
    /// кадрам"), shape and label tables and the paths of the replay and render events. Frames are decoded into one reused
    /// <see cref="RenderFrame"/>. Main thread (the reader is not thread-safe); dispose to close the file.
    /// </summary>
    public sealed class ClipSource : IDisposable
    {
        readonly FileStream _stream;
        readonly RenderStreamReader _reader;
        readonly RenderFrame _frame = new RenderFrame();
        int _frameTick = -1;

        ClipSource(string directory, FileStream stream, RenderStreamReader reader)
        {
            Directory = directory;
            _stream = stream;
            _reader = reader;
            var labels = new string[reader.LabelCount + 1];
            for (int i = 1; i <= reader.LabelCount; i++) labels[i] = reader.GetLabel(i);
            Labels = Array.AsReadOnly(labels);
            string events = Path.Combine(directory, StudioFiles.ClipEventsFile);
            Info = new SimClipInfo(reader.Header.ModeId, reader.Header, reader.TickCount, reader.Shapes, Labels,
                Path.Combine(directory, ProjectPaths.ClipRenderFile), File.Exists(events) ? events : null,
                Path.Combine(directory, ProjectPaths.ClipReplayFile));
        }

        /// <summary>Opens <c>clip.bvs</c> in <paramref name="directory"/>.</summary>
        /// <exception cref="FileNotFoundException">The render stream is missing (it must be rebuilt from the replay first).</exception>
        /// <exception cref="InvalidDataException">The stream is corrupt or of an unknown version.</exception>
        public static ClipSource Open(string directory)
        {
            if (string.IsNullOrEmpty(directory)) throw new ArgumentException("directory required", nameof(directory));
            string path = Path.Combine(directory, ProjectPaths.ClipRenderFile);
            if (!File.Exists(path)) throw new FileNotFoundException("The clip has no render stream", path);
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.RandomAccess);
            try
            {
                return new ClipSource(directory, stream, new RenderStreamReader(stream, true));
            }
            catch
            {
                stream.Dispose();
                throw;
            }
        }

        public string Directory { get; }
        public SimClipInfo Info { get; }
        public RenderStreamReader Reader => _reader;
        public int TickCount => _reader.TickCount;
        public IReadOnlyList<ShapeDefinition> Shapes => _reader.Shapes;
        public IReadOnlyList<string> Labels { get; }

        /// <summary>The decoded frame of <paramref name="tick"/> (clamped to the stream); the instance is reused by the next call.</summary>
        public RenderFrame ReadFrame(int tick)
        {
            if (_reader.TickCount <= 0) return _frame;
            int t = tick < 0 ? 0 : tick >= _reader.TickCount ? _reader.TickCount - 1 : tick;
            if (t != _frameTick)
            {
                _reader.ReadFrame(t, _frame);
                _frameTick = t;
            }
            return _frame;
        }

        public void Dispose()
        {
            _reader.Dispose();
            _stream.Dispose();
        }
    }
}
