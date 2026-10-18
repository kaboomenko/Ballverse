namespace Ballverse.Core.RenderStream
{
    /// <summary>
    /// Hard limits of the .bvs v1 shape table, label table and grid layer (spec §7.4, §7.7, §2.9). The writer rejects
    /// definitions beyond them and the reader treats them as allocation guards (<see cref="System.IO.InvalidDataException"/>).
    /// </summary>
    public static class RenderStreamLimits
    {
        /// <summary>Largest number of distinct shapes in one stream (shape ids are [0, MaxShapes)).</summary>
        public const int MaxShapes = 65536;
        /// <summary>Fewest polygon vertices.</summary>
        public const int MinPolygonVertices = 3;
        /// <summary>Most polygon vertices (M11 fragments need ≤ 8; arenas and stars a few dozen).</summary>
        public const int MaxPolygonVertices = 64;
        /// <summary>Fewest points of an open polyline (a closed one needs <see cref="MinPolygonVertices"/>).</summary>
        public const int MinPolylinePoints = 2;
        /// <summary>Most points of one polyline (maze walls, track pieces, finger-drawn obstacles).</summary>
        public const int MaxPolylinePoints = 4096;
        /// <summary>Most gaps of one ring (M01 needs 1–3; spiral platforms and wheels a few more).</summary>
        public const int MaxRingGaps = 64;
        /// <summary>Largest encoded shape table section in bytes, including its count prefix.</summary>
        public const int MaxShapeTableBytes = 16 * 1024 * 1024;

        /// <summary>Largest number of label strings (label ids are [1, MaxLabels]; 0 = no label).</summary>
        public const int MaxLabels = 1024;
        /// <summary>Longest label in UTF-16 code units (names and numbers drawn on balls, M03/M06/M07/M09).</summary>
        public const int MaxLabelLength = 32;
        /// <summary>Longest label in UTF-8 bytes (3 bytes per UTF-16 unit at most).</summary>
        public const int MaxLabelUtf8Bytes = MaxLabelLength * 3;

        /// <summary>Largest grid width (columns) of the grid layer (M08 Color War).</summary>
        public const int MaxGridWidth = 128;
        /// <summary>Largest grid height (rows) of the grid layer.</summary>
        public const int MaxGridHeight = 256;
    }
}
