namespace Ballverse.Core.Projects
{
    /// <summary>
    /// <c>output.cover</c>: cover frame (spec §3.4 "Обложка"). In 1.0 the first frame of the reel is the cover; with
    /// <see cref="PutFirst"/> the frame at <see cref="AtUs"/> is placed first for 0.1 s ("Поставить выбранный кадр первым на 0,1 с").
    /// Not in the §7.4 example; added at schema 1.
    /// </summary>
    public sealed class CoverSettings
    {
        /// <summary>Chosen cover frame on the reel timeline, microseconds; kept inside the reel by the validator.</summary>
        public long AtUs { get; set; }

        /// <summary>Show the chosen frame first for 0.1 s (default off).</summary>
        public bool PutFirst { get; set; }
    }
}
