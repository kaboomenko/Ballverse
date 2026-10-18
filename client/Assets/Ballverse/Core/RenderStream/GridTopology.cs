namespace Ballverse.Core.RenderStream
{
    /// <summary>
    /// How the cells of the grid layer are laid out on screen (spec §2.9 M08 "сетка 30×53 по умолчанию или гексы").
    /// Cell data is the same row-major palette array for both; only drawing differs. Stored as one byte.
    /// </summary>
    public enum GridTopology : byte
    {
        /// <summary>Square cells: cell (x, y) covers column x, row y of the grid bounds.</summary>
        Square = 0,
        /// <summary>Hexagonal cells in rows; odd rows are shifted right by half a cell width.</summary>
        HexOddRowsShifted = 1,
    }
}
