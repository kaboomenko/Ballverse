using System;
using System.Globalization;

namespace Ballverse.Core.Generation
{
    /// <summary>
    /// Spectacle profile of every mode M01–M15 (spec §2.4 step 4: "weights w1…w5 are per mode and live in Remote Config", §9.3
    /// <c>generation_weights</c>). Immutable; <see cref="With"/> returns a new table. The Remote Config key is parsed only by the
    /// Config module; <c>Ballverse.Core.Integration.RemoteConfigAdapters</c> merges its typed entries into this table.
    /// </summary>
    public sealed class SpectacleProfileTable
    {
        /// <summary>
        /// Built-in defaults. All numbers are initial guesses to be tuned through Remote Config with the balance probe (spec §8.5).
        /// Interactive-only modes (M04, M10–M12) have w1 = 0 because their duration is not selected (spec §2.4); M05 Helix Drop
        /// keeps w1 because its default "Авто-ролик" and "Бот" controls select the duration; with the "Игрок" control the
        /// session is interactive and scores with w1 = 0 against threshold − w1 (release-1 card: "w1 … равен 0 в «Игроке»",
        /// see <see cref="GenerationSession.ScoringWeights"/>). Thresholds are roughly 55% of the reachable
        /// maximum w1 + w2 + w3 + w4.
        /// </summary>
        public static readonly SpectacleProfileTable Default = new SpectacleProfileTable(new[]
        {
            //       w1   w2   w3   w4   w5   dMin dMax  threshold
            P(1.0, 0.8, 0.6, 0.5, 1.5, 1.5, 8.0, 1.6),  // M01 ring_escape: ring hits and breaks
            P(1.0, 0.8, 0.4, 0.6, 1.5, 1.0, 6.0, 1.5),  // M02 geometry_bounce: wall hits, births, escapes
            P(1.0, 0.6, 1.0, 0.4, 1.5, 2.0, 12.0, 1.6), // M03 marble_race: tension = leader gap at the finish
            P(0.0, 0.8, 0.3, 0.6, 1.2, 3.0, 25.0, 0.9), // M04 peg_drop (interactive)
            P(1.0, 0.8, 0.5, 0.4, 1.5, 1.5, 5.0, 1.5),  // M05 helix_drop: bounces and platform breaks (spec: 1.5–5/s)
            P(1.0, 0.7, 1.0, 0.4, 1.5, 0.5, 4.0, 1.7),  // M06 ball_battle: weapon hits; tension = winner's HP left
            P(1.0, 0.6, 0.8, 0.4, 1.2, 0.3, 3.0, 1.5),  // M07 last_standing: eliminations and edge hits
            P(1.0, 0.6, 1.0, 0.8, 1.0, 1.0, 10.0, 1.8), // M08 color_war: tension = final percentage gap
            P(1.0, 0.8, 0.6, 0.6, 1.2, 2.0, 20.0, 1.7), // M09 multiply: gate passes
            P(0.0, 0.6, 0.4, 0.8, 1.2, 0.3, 3.0, 1.0),  // M10 maze_escape (interactive)
            P(0.0, 0.8, 0.3, 0.6, 1.5, 1.0, 10.0, 0.9), // M11 smash (interactive)
            P(0.0, 1.0, 0.3, 0.6, 2.0, 1.0, 6.0, 1.0),  // M12 chain_reaction (interactive): a broken chain is stagnation
            P(1.0, 0.8, 0.2, 0.4, 0.6, 0.5, 6.0, 1.3),  // M13 pendulum_wave: meditative, mild stagnation penalty
            P(1.0, 0.6, 0.4, 0.8, 0.8, 0.2, 3.0, 1.5),  // M14 orbit: trails matter, few collisions
            P(1.0, 0.6, 0.2, 0.8, 0.8, 0.2, 4.0, 1.4),  // M15 liquid_sand: filling and layer events
        });

        readonly SpectacleProfile[] _byMode;

        SpectacleProfileTable(SpectacleProfile[] byMode)
        {
            _byMode = byMode;
        }

        /// <summary>Profile of a mode.</summary>
        public SpectacleProfile Get(ModeCode mode)
        {
            int i = (int)mode - 1;
            if (i < 0 || i >= _byMode.Length) throw new ArgumentOutOfRangeException(nameof(mode));
            return _byMode[i];
        }

        /// <summary>Returns a copy with one mode replaced.</summary>
        public SpectacleProfileTable With(ModeCode mode, SpectacleProfile profile)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            int i = (int)mode - 1;
            if (i < 0 || i >= _byMode.Length) throw new ArgumentOutOfRangeException(nameof(mode));
            var copy = (SpectacleProfile[])_byMode.Clone();
            copy[i] = profile;
            return new SpectacleProfileTable(copy);
        }

        static SpectacleProfile P(double w1, double w2, double w3, double w4, double w5, double densityMin, double densityMax, double threshold)
        {
            return new SpectacleProfile(new SpectacleWeights(w1, w2, w3, w4, w5), new DensityRange(densityMin, densityMax), threshold);
        }

        /// <summary>Debug description of one mode's profile (invariant culture).</summary>
        public string Describe(ModeCode mode)
        {
            SpectacleProfile p = Get(mode);
            SpectacleWeights w = p.Weights;
            return string.Format(CultureInfo.InvariantCulture, "{0}: w=({1},{2},{3},{4},{5}) density={6} threshold={7}",
                mode, w.W1, w.W2, w.W3, w.W4, w.W5, p.Density, p.QualityThreshold);
        }
    }
}
