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
        /// <param name="modeId">Stable mode id, e.g. "ring_escape" (spec §2.8). Non-empty.</param>
        /// <param name="modeSimVersion">Version of the mode's simulation code; bumped when physics/rules change results (spec §3.9).</param>
        /// <param name="appVersion">Application version string, may be empty.</param>
        /// <param name="physicsBackend">Physics engine id (u8).</param>
        /// <param name="seed">Run seed (spec §7.4 rule 2).</param>
        /// <param name="paramsJson">Mode parameters as JSON (UTF-8 ≤ <see cref="BvrFormat.MaxParamsJsonBytes"/>).</param>
        /// <param name="durationTicks">Run length in ticks; record ticks lie in 0..durationTicks. The writer writes its own value in Finish.</param>
        public ReplayHeader(string modeId, ushort modeSimVersion, string appVersion, PhysicsBackend physicsBackend,
            ulong seed, string paramsJson, uint durationTicks = 0)
        {
            if (modeId == null) throw new ArgumentNullException(nameof(modeId));
            if (modeId.Length == 0) throw new ArgumentException("modeId must not be empty", nameof(modeId));
            BvrEncoding.EncodeString(modeId, BvrFormat.MaxStringBytes, nameof(modeId));
            BvrEncoding.EncodeString(appVersion, BvrFormat.MaxStringBytes, nameof(appVersion));
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

        /// <summary>The paramsHash definition of this format: XXH64 (seed 0) of the strict UTF-8 bytes of the JSON text.</summary>
        public static ulong ComputeParamsHash(string paramsJson)
        {
            return XxHash64.Hash(BvrEncoding.EncodeString(paramsJson, int.MaxValue, nameof(paramsJson)));
        }
    }
}
