using System;
using System.Collections.Generic;

namespace Ballverse.Core.Replay
{
    /// <summary>
    /// A whole replay in memory (spec §7.4): header plus the record streams in tick order. Produced by <see cref="ReplayReader"/>,
    /// consumed by <see cref="ReplayWriter.Write(System.IO.Stream, ReplayFile, int)"/>. Lists are copied on construction.
    /// </summary>
    public sealed class ReplayFile
    {
        public ReplayFile(ReplayHeader header, IReadOnlyList<ReplayInput> inputs, IReadOnlyList<ReplayParamChange> paramChanges,
            IReadOnlyList<ReplayStateHash> hashes, IReadOnlyList<ReplayEvent> events, ReplayResult result, int skippedChunkCount = 0)
        {
            Header = header ?? throw new ArgumentNullException(nameof(header));
            Inputs = Copy(inputs);
            ParamChanges = Copy(paramChanges);
            Hashes = Copy(hashes);
            Events = Copy(events);
            Result = result;
            SkippedChunkCount = skippedChunkCount;
        }

        public ReplayHeader Header { get; }

        public IReadOnlyList<ReplayInput> Inputs { get; }

        public IReadOnlyList<ReplayParamChange> ParamChanges { get; }

        public IReadOnlyList<ReplayStateHash> Hashes { get; }

        public IReadOnlyList<ReplayEvent> Events { get; }

        /// <summary>Run result, or null when the file has no RSLT chunk (e.g. a run aborted before the finale).</summary>
        public ReplayResult Result { get; }

        /// <summary>Number of chunks with unknown tags that the reader skipped (forward compatibility).</summary>
        public int SkippedChunkCount { get; }

        static T[] Copy<T>(IReadOnlyList<T> list)
        {
            if (list == null) return Array.Empty<T>();
            var a = new T[list.Count];
            for (int i = 0; i < a.Length; i++) a[i] = list[i];
            return a;
        }
    }
}
