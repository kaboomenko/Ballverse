namespace Ballverse.Core.Projects
{
    /// <summary>
    /// <c>output.cover</c>: cover frame (spec §3.4 "Обложка"). In 1.0 the first frame of the reel is the cover; with
    /// <see cref="PutFirst"/> the content frame at <see cref="AtUs"/> is prepended for 0.1 s ("Поставить выбранный кадр первым на 0,1 с").
    /// Not in the §7.4 example; added at schema 1.
    /// </summary>
    public sealed class CoverSettings
    {
        /// <summary>
        /// Chosen cover frame on the content timeline, microseconds: measured from the start of the content, without the 0.1 s
        /// lead-in (<see cref="ProjectTiming.ContentDurationUs"/>; the lead-in only repeats this frame). The validator keeps it in
        /// [0, content − 1 µs]. Passed to the timeline as its cover time (spec §3.4).
        /// </summary>
        public long AtUs { get; set; }

        /// <summary>Show the chosen frame first for 0.1 s (default off).</summary>
        public bool PutFirst { get; set; }
    }
}
