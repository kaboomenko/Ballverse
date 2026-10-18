namespace Ballverse.Core.RenderStream
{
    /// <summary>
    /// One entry of the .bvs block index (spec §7.4: blocks of 1 s, each starting with a keyframe, for instant seeking).
    /// </summary>
    public struct RenderStreamBlockInfo
    {
        /// <summary>Tick of the block's keyframe.</summary>
        public int FirstTick;
        /// <summary>Frames in the block (equal to the header's block length except possibly for the last block).</summary>
        public int FrameCount;
        /// <summary>Byte offset of the compressed block, relative to the start of the header.</summary>
        public long Offset;
        public int CompressedLength;
        public int RawLength;
        /// <summary>XXH64 of the decompressed block bytes.</summary>
        public ulong RawHash;
    }
}
