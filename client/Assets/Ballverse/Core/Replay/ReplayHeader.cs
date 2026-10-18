using System;

namespace Ballverse.Core.Replay
{
    /// <summary>
    /// Header of a <c>.bvr</c> replay (spec §7.4): identifies the mode and build that produced the run and everything
    /// needed to re-simulate it (seed, parameters). Immutable.
    /// </summary>
    public sealed class ReplayHeader
    {
        /// <summary>Creates a header. <see cref="ParamsHash"/> is computed from <paramref name="paramsJson"/>.</summary>
        /// <param name="modeId">Stable mode id, e.g. "ring_escape" (spec §2.8): <c>[a-z0-9_]{1,64}</c>.</param>
        /// <param name="modeSimVersion">Version of the mode's simulation code; bumped when physics/rules change results (spec §3.9).</param>
        /// <param name="appVersion">Application version string: printable ASCII, at most <see cref="BvrFormat.MaxAppVersionLength"/> chars, may be empty.</param>
        /// <param name="physicsBackend">Physics engine id (u8).</param>
        /// <param name="seed">Run seed (spec §7.4 rule 2).</param>
        /// <param name="paramsJson">Mode parameters as JSON (UTF-8 ≤ <see cref="BvrFormat.MaxParamsJsonBytes"/>); the writer and reader require strict RFC 8259 syntax.</param>
        /// <param name="durationTicks">Run length in ticks; record ticks lie in 0..durationTicks. The writer writes its own value in Finish.</param>
        public ReplayHeader(string modeId, ushort modeSimVersion, string appVersion, PhysicsBackend physicsBackend,
            ulong seed, string paramsJson, uint durationTicks = 0)
        {
            if (modeId == null) throw new ArgumentNullException(nameof(modeId));
            if (appVersion == null) throw new ArgumentNullException(nameof(appVersion));
            string error = CheckModeId(modeId);
            if (error != null) throw new ArgumentException(error, nameof(modeId));
            error = CheckAppVersion(appVersion);
            if (error != null) throw new ArgumentException(error, nameof(appVersion));
            byte[] paramsBytes = BvrEncoding.EncodeString(paramsJson, BvrFormat.MaxParamsJsonBytes, nameof(paramsJson));

            ModeId = modeId;
            ModeSimVersion = modeSimVersion;
            AppVersion = appVersion;
            PhysicsBackend = physicsBackend;
            Seed = seed;
            ParamsJson = paramsJson;
            ParamsHash = XxHash64.Hash(paramsBytes);
            DurationTicks = durationTicks;
        }

        /// <summary>Format version written/read; always <see cref="BvrFormat.CurrentVersion"/> in this build.</summary>
        public ushort FormatVersion => BvrFormat.CurrentVersion;

        public string ModeId { get; }

        public ushort ModeSimVersion { get; }

        public string AppVersion { get; }

        public PhysicsBackend PhysicsBackend { get; }

        /// <summary>Always <see cref="SimConstants.TickRate"/> (60): the only allowed step (spec §2.1, §7.4 rule 1).</summary>
        public ushort TickRate => SimConstants.TickRate;

        public ulong Seed { get; }

        public string ParamsJson { get; }

        /// <summary>
        /// XXH64 (seed 0) of the UTF-8 bytes of <see cref="ParamsJson"/>. Computed, never supplied; the reader rejects a file whose
        /// stored hash does not match its paramsJson (corruption check).
        /// </summary>
        public ulong ParamsHash { get; }

        public uint DurationTicks { get; }

        /// <summary>Copy with a different <see cref="DurationTicks"/>.</summary>
        public ReplayHeader WithDurationTicks(uint durationTicks)
        {
            return new ReplayHeader(ModeId, ModeSimVersion, AppVersion, PhysicsBackend, Seed, ParamsJson, durationTicks);
        }

        /// <summary>Returns null when <paramref name="modeId"/> matches <c>[a-z0-9_]{1,64}</c>, else an error message.</summary>
        internal static string CheckModeId(string modeId)
        {
            if (modeId.Length == 0 || modeId.Length > BvrFormat.MaxModeIdLength)
                return "modeId must be 1.." + BvrFormat.MaxModeIdLength + " characters";
            for (int i = 0; i < modeId.Length; i++)
            {
                char c = modeId[i];
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_'))
                    return "modeId may contain only a-z, 0-9 and '_'";
            }
            return null;
        }

        /// <summary>Returns null when <paramref name="appVersion"/> is printable ASCII of at most 64 chars, else an error message.</summary>
        internal static string CheckAppVersion(string appVersion)
        {
            if (appVersion.Length > BvrFormat.MaxAppVersionLength)
                return "appVersion is longer than " + BvrFormat.MaxAppVersionLength + " characters";
            for (int i = 0; i < appVersion.Length; i++)
            {
                if (appVersion[i] < 0x20 || appVersion[i] > 0x7E) return "appVersion may contain only printable ASCII";
            }
            return null;
        }

        /// <summary>The paramsHash definition of this format: XXH64 (seed 0) of the strict UTF-8 bytes of the JSON text.</summary>
        public static ulong ComputeParamsHash(string paramsJson)
        {
            return XxHash64.Hash(BvrEncoding.EncodeString(paramsJson, int.MaxValue, nameof(paramsJson)));
        }
    }
}
