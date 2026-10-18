namespace Ballverse.Core.Replay
{
    /// <summary>
    /// Constants of the replay <c>.bvr</c> binary format (spec §7.4 "Реплей .bvr: формат").
    /// Layout: header (magic, formatVersion, modeId, modeSimVersion, appVersion, physicsBackend, tickRate, seed,
    /// paramsJson, paramsHash, durationTicks) followed by TLV chunks (tag[4] · length u32 · payload).
    /// Fixed-width fields are little-endian; strings are a LEB128 byte length followed by UTF-8 bytes.
    /// </summary>
    public static class BvrFormat
    {
        /// <summary>File magic, 4 ASCII bytes.</summary>
        public const string Magic = "BVR1";

        /// <summary>The only format version this build reads and writes.</summary>
        public const ushort CurrentVersion = 1;

        /// <summary>Input events (spec §2.6).</summary>
        public const string TagInput = "INPT";
        /// <summary>Live parameter changes.</summary>
        public const string TagParam = "PARM";
        /// <summary>State hashes, every 60 ticks (spec §7.4).</summary>
        public const string TagHash = "HASH";
        /// <summary>Simulation events for sound and VFX (spec §2.7).</summary>
        public const string TagEvent = "EVNT";
        /// <summary>Run result: winner, scores, finish time.</summary>
        public const string TagResult = "RSLT";

        /// <summary>Upper bound for any string field, UTF-8 bytes (allocation guard).</summary>
        public const int MaxStringBytes = 1 << 20;
        /// <summary>Maximum length of the header modeId; it must match <c>[a-z0-9_]{1,64}</c> (spec §2.8 ids such as "ring_escape").</summary>
        public const int MaxModeIdLength = 64;
        /// <summary>Maximum length of the header appVersion; printable ASCII (0x20..0x7E) only, may be empty.</summary>
        public const int MaxAppVersionLength = 64;
        /// <summary>Upper bound for the header paramsJson, UTF-8 bytes.</summary>
        public const int MaxParamsJsonBytes = 256 << 10;
        /// <summary>Upper bound for a known chunk payload; the writer never gets close (it flushes much earlier).</summary>
        public const int MaxChunkPayloadBytes = 16 << 20;
        /// <summary>Upper bound for the number of ints in one input payload ("small list", spec §2.6).</summary>
        public const int MaxInputValues = 64;
        /// <summary>Upper bound for the number of scores in the result record.</summary>
        public const int MaxResultScores = 4096;

        /// <summary>Default number of records per chunk before the writer flushes a stream.</summary>
        public const int DefaultRecordsPerChunk = 256;

        /// <summary>The writer also flushes a stream once its buffered payload reaches this size.</summary>
        internal const int FlushThresholdBytes = 256 << 10;

        internal static readonly uint MagicValue = TagValue(Magic);
        internal static readonly uint InputTagValue = TagValue(TagInput);
        internal static readonly uint ParamTagValue = TagValue(TagParam);
        internal static readonly uint HashTagValue = TagValue(TagHash);
        internal static readonly uint EventTagValue = TagValue(TagEvent);
        internal static readonly uint ResultTagValue = TagValue(TagResult);

        /// <summary>Little-endian value of a 4-character ASCII tag, as it appears on disk.</summary>
        internal static uint TagValue(string tag)
        {
            return (uint)tag[0] | ((uint)tag[1] << 8) | ((uint)tag[2] << 16) | ((uint)tag[3] << 24);
        }
    }
}
