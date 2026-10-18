namespace Ballverse.Core.Layout
{
    /// <summary>
    /// Shared corner-placement step for the watermark (spec §3.10) and PiP magnets (spec §3.3): slide a rect vertically
    /// away from its corner until it no longer intersects any mask.
    /// </summary>
    internal static class MaskAvoidance
    {
        /// <summary>
        /// Starting at <paramref name="start"/>, repeatedly moves the rect past every mask it intersects (down: to the lowest
        /// mask bottom + gap; up: to the highest mask top - gap - height). Succeeds when the rect is mask-free while staying
        /// within [minY, maxBottom]; otherwise returns <paramref name="start"/> unchanged with <paramref name="clear"/> false
        /// (or true if start itself was clear). Terminates after at most masks.Count + 1 steps because each step passes at
        /// least one mask for good.
        /// </summary>
        public static IntRect PushVertical(IntRect start, RectUnion masks, bool down, int gap, int minY, int maxBottom, out bool clear)
        {
            IntRect r = start;
            for (int step = 0; step <= masks.Count; step++)
            {
                bool any = false;
                int edge = down ? int.MinValue : int.MaxValue;
                for (int i = 0; i < masks.Count; i++)
                {
                    IntRect m = masks[i];
                    if (!m.Intersects(r)) continue;
                    any = true;
                    if (down)
                    {
                        if (m.Bottom > edge) edge = m.Bottom;
                    }
                    else if (m.Y < edge)
                    {
                        edge = m.Y;
                    }
                }
                if (!any)
                {
                    clear = true;
                    return r;
                }
                int ny = down ? edge + gap : edge - gap - r.Height;
                if (down ? ny + r.Height > maxBottom : ny < minY) break;
                r = r.WithPosition(r.X, ny);
            }
            clear = false;
            return start;
        }
    }
}
