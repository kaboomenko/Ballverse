namespace Ballverse.Core.Audio
{
    /// <summary>Outcome of one event in <see cref="SoundEventProcessor"/> (spec §2.7 mixer rules).</summary>
    public enum SoundDecision
    {
        /// <summary>Accepted; a voice should start.</summary>
        Played = 0,
        /// <summary>The pack has no rule for this event type (or is <c>None</c>).</summary>
        NoRule = 1,
        /// <summary>Impulse below i0 (or NaN): silent, consumes no rate budget and does not advance the melody.</summary>
        BelowThreshold = 2,
        /// <summary>The same body sounded less than 50 ms ago.</summary>
        BodyRateLimited = 3,
        /// <summary>40 sounds already in the last second.</summary>
        GlobalRateLimited = 4,
        /// <summary>20 melody notes already in the last second; the melody did not advance.</summary>
        MelodyRateLimited = 5,
        /// <summary>Accepted (counts toward limits, melody advanced) but the resulting gain is 0, e.g. a velocity-0 rest.</summary>
        Muted = 6,
    }
}
