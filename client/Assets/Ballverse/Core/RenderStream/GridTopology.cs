namespace Ballverse.Core.RenderStream
{
    /// <summary>
    /// How the cells of the grid layer are laid out (spec §2.9 M08 "сетка 30×53 по умолчанию или гексы"). Cell data is
    /// the same row-major palette array for both; only the geometry differs. Stored as one byte.
    /// <para>
    /// The geometry is normative for both the simulation (which cell a ball touches) and the renderer (where the mask is
    /// drawn); use <see cref="RenderGridSpec.CellCenter"/> and <see cref="RenderGridSpec.TryGetCell"/> rather than
    /// re-deriving it. Below, the bounds are the dequantized <see cref="RenderGridSpec"/> bounds in metres, x = column,
    /// y = row (row 0 at the bottom, at MinY).
    /// </para>
    /// </summary>
    public enum GridTopology : byte
    {
        /// <summary>
        /// Square (rectangular) cells tiling the bounds: cell width w = (MaxX − MinX) / Width, height
        /// h = (MaxY − MinY) / Height; cell (x, y) covers [MinX + x·w, MinX + (x+1)·w] × [MinY + y·h, MinY + (y+1)·h]
        /// and its centre is (MinX + (x + ½)·w, MinY + (y + ½)·h).
        /// </summary>
        Square = 0,

        /// <summary>
        /// Pointy-top hexagons (a vertex at the top and bottom) in rows; odd rows (y = 1, 3, …) are shifted right by half
        /// a cell width, and consecutive rows overlap by a quarter hex height (row pitch ¾ of the hex height).
        /// The bounds hold Width + ½ cell widths across and 0.75·Height + 0.25 hex heights up, so
        /// w = (MaxX − MinX) / (Width + 0.5), hex height H = (MaxY − MinY) / (0.75·Height + 0.25), and the centre of cell
        /// (x, y) is (MinX + (x + ½ + ½·(y mod 2))·w, MinY + (½ + ¾·y)·H). The hexagons are regular when H = w·2/√3 and
        /// otherwise stretched along the axes to fill the bounds. A point belongs to the cell whose centre is nearest in
        /// the unstretched (regular) grid, i.e. to the hexagon containing it; points of the bounds rectangle along the
        /// jagged edges, outside every hexagon, belong to the nearest edge cell.
        /// </summary>
        HexOddRowsShifted = 1,
    }
}
