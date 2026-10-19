using System;
using System.Collections.Generic;
using Ballverse.Core.Audio;

namespace Ballverse.Sim
{
    /// <summary>
    /// Sample banks of the built-in sound packs synthesized in code (48 kHz mono), so release-1 builds have working sound
    /// without recorded assets: bells and chimes for Notes / Melody, bubbles for Pop, inharmonic clinks and noise shatters for
    /// Glass, synthesized kit pieces for Drums, filtered clicks for ASMR and square waves for Chiptune. Every sample id the
    /// Core pack rules reference (<see cref="SoundPackCatalog"/>) gets a sound; the pitch rules of the mixer shift them.
    /// Deterministic (no clock, own noise generator). Recorded OGG banks can replace a pack behind the same ids (spec §7.8).
    /// Building a bank allocates (≈ 0.2–1.5 MB per pack); do it off the audio thread and keep it.
    /// </summary>
    public static class ProceduralSampleBank
    {
        const int Rate = AudioConstants.SampleRate;

        /// <summary>A bank with every sample of <paramref name="id"/>; empty for <see cref="SoundPackId.None"/>.</summary>
        public static InMemorySampleBank Create(SoundPackId id)
        {
            var bank = new InMemorySampleBank();
            SoundPack pack = SoundPackCatalog.Get(id);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (SoundRule rule in pack.Rules)
            {
                if (rule.Samples == null) continue;
                foreach (string sample in rule.Samples)
                {
                    if (string.IsNullOrEmpty(sample) || !seen.Add(sample)) continue;
                    bank.TryAdd(sample, new AudioSample(Synthesize(id, sample, rule.EventType), 1));
                }
            }
            return bank;
        }

        /// <summary>Parses a pack id as stored in parameters / projects ("Notes", "Pop", …); unknown → Notes.</summary>
        public static SoundPackId ParsePack(string id)
        {
            if (!string.IsNullOrEmpty(id) && Enum.TryParse(id, true, out SoundPackId parsed) && Enum.IsDefined(typeof(SoundPackId), parsed)) return parsed;
            return SoundPackId.Notes;
        }

        static float[] Synthesize(SoundPackId pack, string sample, SoundEventType type)
        {
            uint seed = Hash(sample);
            string name = sample.Substring(sample.IndexOf('/') + 1);
            switch (pack)
            {
                case SoundPackId.Notes:
                case SoundPackId.Melody:
                    if (name == "fanfare") return Arpeggio(new[] { 72, 76, 79, 84 }, 0.09, Bell);
                    if (name == "drop") return Sweep(880, 220, 0.35, 0.6f);
                    if (name == "tick") return Click(seed, 0.03, 4000);
                    if (name == "ding") return Tone(1568, 0.5, Bell);
                    if (name == "piano") return Tone(MidiHz(60), 1.2, Piano);
                    if (name == "chime") return Tone(MidiHz(72), 1.0, Chime);
                    return Tone(MidiHz(72), 0.9, Bell);
                case SoundPackId.Pop:
                    if (name == "burst") return NoiseBurst(seed, 0.18, 0.5f, 2500);
                    if (name == "deflate") return Sweep(600, 120, 0.4, 0.5f);
                    if (name == "party") return Arpeggio(new[] { 67, 72, 76, 79 }, 0.07, Bubble);
                    if (name == "bloop") return Sweep(300, 700, 0.12, 0.8f);
                    return Sweep(500 + (seed % 5) * 60, 180 + (seed % 3) * 30, 0.1, 0.9f);
                case SoundPackId.Glass:
                    if (name.StartsWith("shatter", StringComparison.Ordinal)) return Shatter(seed, 0.7);
                    if (name == "crack") return NoiseBurst(seed, 0.12, 0.7f, 6000);
                    if (name == "chimes") return Arpeggio(new[] { 84, 88, 91, 96 }, 0.08, Glassy);
                    if (name == "ting") return Tone(2637, 0.6, Glassy);
                    return Tone(1800 + (seed % 7) * 120, 0.6, Glassy);
                case SoundPackId.Drums:
                    switch (name)
                    {
                        case "kick": return Sweep(150, 45, 0.35, 1f);
                        case "tom": return Sweep(220, 110, 0.3, 0.9f);
                        case "snare": return Mix(NoiseBurst(seed, 0.2, 0.7f, 5000), Tone(190, 0.15, Drum), 0.6f);
                        case "hat": return NoiseBurst(seed, 0.06, 0.5f, 9000);
                        case "crash": return NoiseBurst(seed, 1.0, 0.6f, 7000);
                        case "rim": return Click(seed, 0.04, 2500);
                        case "shaker": return NoiseBurst(seed, 0.08, 0.3f, 8000);
                        case "cowbell": return Mix(Tone(540, 0.25, Drum), Tone(800, 0.25, Drum), 0.5f);
                        default: return NoiseBurst(seed, 0.15, 0.6f, 4000);
                    }
                case SoundPackId.Asmr:
                    if (name == "rustle") return NoiseBurst(seed, 0.35, 0.25f, 3000);
                    if (name == "tap_roll") return Arpeggio(new[] { 60, 62, 64, 67 }, 0.06, Wood);
                    if (name.StartsWith("wood", StringComparison.Ordinal)) return Tone(400 + (seed % 5) * 40, 0.12, Wood);
                    if (name == "snap") return Click(seed, 0.05, 3500);
                    return Click(seed, 0.03, 2000 + (seed % 4) * 500);
                case SoundPackId.Chiptune:
                    switch (name)
                    {
                        case "square": return Square(MidiHz(72), 0.16);
                        case "noise": return NoiseBurst(seed, 0.2, 0.5f, 12000);
                        case "down": return SquareSweep(880, 110, 0.3);
                        case "jingle": return Arpeggio(new[] { 72, 76, 79, 84 }, 0.08, SquareShape);
                        case "coin": return Mix(Square(MidiHz(83), 0.06), Delay(Square(MidiHz(88), 0.18), 0.06), 1f);
                        default: return Square(MidiHz(84), 0.05);
                    }
                default:
                    return Tone(MidiHz(72), 0.4, Bell);
            }
        }

        // ---- shapes: phase (cycles) → sample, t (seconds), duration ----

        delegate float Shape(double phase, double t, double duration);

        static float Bell(double p, double t, double d) =>
            (float)((Math.Sin(2 * Math.PI * p) + 0.35 * Math.Sin(4 * Math.PI * p) + 0.12 * Math.Sin(6.8 * Math.PI * p)) * Math.Exp(-4.5 * t));

        static float Chime(double p, double t, double d) =>
            (float)((Math.Sin(2 * Math.PI * p) + 0.5 * Math.Sin(5.04 * Math.PI * p)) * Math.Exp(-3.5 * t));

        static float Piano(double p, double t, double d) =>
            (float)((Math.Sin(2 * Math.PI * p) + 0.5 * Math.Sin(4 * Math.PI * p) + 0.25 * Math.Sin(6 * Math.PI * p)) * Math.Exp(-3 * t) * Math.Min(1.0, t * 400));

        static float Glassy(double p, double t, double d) =>
            (float)((Math.Sin(2 * Math.PI * p) + 0.6 * Math.Sin(2 * Math.PI * p * 2.76) + 0.3 * Math.Sin(2 * Math.PI * p * 5.4)) * Math.Exp(-6 * t));

        static float Bubble(double p, double t, double d) => (float)(Math.Sin(2 * Math.PI * p * (1 + t * 3)) * Math.Exp(-12 * t));

        static float Wood(double p, double t, double d) => (float)(Math.Sin(2 * Math.PI * p) * Math.Exp(-30 * t));

        static float Drum(double p, double t, double d) => (float)(Math.Sin(2 * Math.PI * p) * Math.Exp(-14 * t));

        static float SquareShape(double p, double t, double d) => (float)((p - Math.Floor(p) < 0.5 ? 0.5 : -0.5) * Math.Exp(-5 * t));

        // ---- generators ----

        static float[] Tone(double hz, double seconds, Shape shape)
        {
            int n = (int)(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                data[i] = 0.45f * shape(hz * t, t, seconds) * Fade(i, n);
            }
            return data;
        }

        static float[] Arpeggio(int[] midi, double stepSeconds, Shape shape)
        {
            double noteSeconds = 0.6;
            int n = (int)((stepSeconds * (midi.Length - 1) + noteSeconds) * Rate);
            var data = new float[n];
            for (int k = 0; k < midi.Length; k++)
            {
                int start = (int)(k * stepSeconds * Rate);
                double hz = MidiHz(midi[k]);
                int len = (int)(noteSeconds * Rate);
                for (int i = 0; i < len && start + i < n; i++)
                {
                    double t = i / (double)Rate;
                    data[start + i] += 0.3f * shape(hz * t, t, noteSeconds) * Fade(i, len);
                }
            }
            return data;
        }

        static float[] Sweep(double fromHz, double toHz, double seconds, float level)
        {
            int n = (int)(seconds * Rate);
            var data = new float[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                double x = i / (double)n;
                double hz = fromHz * Math.Pow(toHz / fromHz, x);
                phase += hz / Rate;
                data[i] = (float)(0.5 * level * Math.Sin(2 * Math.PI * phase) * Math.Exp(-5 * x)) * Fade(i, n);
            }
            return data;
        }

        static float[] Square(double hz, double seconds)
        {
            int n = (int)(seconds * Rate);
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                data[i] = 0.6f * SquareShape(hz * t, t, seconds) * Fade(i, n);
            }
            return data;
        }

        static float[] SquareSweep(double fromHz, double toHz, double seconds)
        {
            int n = (int)(seconds * Rate);
            var data = new float[n];
            double phase = 0;
            for (int i = 0; i < n; i++)
            {
                double x = i / (double)n;
                phase += (fromHz + (toHz - fromHz) * x) / Rate;
                data[i] = (float)((phase - Math.Floor(phase) < 0.5 ? 0.25 : -0.25) * (1 - x)) * Fade(i, n);
            }
            return data;
        }

        static float[] NoiseBurst(uint seed, double seconds, float level, double cutoffHz)
        {
            int n = (int)(seconds * Rate);
            var data = new float[n];
            uint state = seed | 1u;
            double a = Math.Exp(-2 * Math.PI * cutoffHz / Rate);
            double y = 0;
            for (int i = 0; i < n; i++)
            {
                double x = Noise(ref state);
                // One-pole low-pass for the darker kinds, high-passed residue for bright ones.
                y = (1 - a) * x + a * y;
                double s = cutoffHz > 6000 ? x - y : y * 2.5;
                data[i] = (float)(level * s * Math.Exp(-6 * i / (double)n)) * Fade(i, n);
            }
            return data;
        }

        static float[] Shatter(uint seed, double seconds)
        {
            float[] noise = NoiseBurst(seed, seconds, 0.45f, 9000);
            uint state = seed ^ 0x9E3779B9u;
            for (int k = 0; k < 6; k++)
            {
                double hz = 2000 + (Noise(ref state) * 0.5 + 0.5) * 4000;
                int start = (int)((Noise(ref state) * 0.5 + 0.5) * 0.2 * Rate);
                int len = (int)(0.25 * Rate);
                for (int i = 0; i < len && start + i < noise.Length; i++)
                {
                    double t = i / (double)Rate;
                    noise[start + i] += 0.15f * Glassy(hz * t, t, 0.25) * Fade(i, len);
                }
            }
            return noise;
        }

        static float[] Click(uint seed, double seconds, double hz)
        {
            int n = (int)(seconds * Rate);
            var data = new float[n];
            uint state = seed | 1u;
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)Rate;
                double env = Math.Exp(-120 * t);
                data[i] = (float)(0.5 * env * (Math.Sin(2 * Math.PI * hz * t) * 0.7 + Noise(ref state) * 0.3)) * Fade(i, n);
            }
            return data;
        }

        static float[] Mix(float[] a, float[] b, float bLevel)
        {
            var data = new float[Math.Max(a.Length, b.Length)];
            for (int i = 0; i < data.Length; i++)
            {
                float x = i < a.Length ? a[i] : 0f;
                float y = i < b.Length ? b[i] * bLevel : 0f;
                data[i] = x + y;
            }
            return data;
        }

        static float[] Delay(float[] a, double seconds)
        {
            int offset = (int)(seconds * Rate);
            var data = new float[a.Length + offset];
            Array.Copy(a, 0, data, offset, a.Length);
            return data;
        }

        static float Fade(int i, int n)
        {
            const int edge = 96;
            if (i < edge) return i / (float)edge;
            int left = n - 1 - i;
            return left < edge ? left / (float)edge : 1f;
        }

        static double Noise(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state / (double)uint.MaxValue) * 2.0 - 1.0;
        }

        internal static double MidiHz(int midi) => 440.0 * Math.Pow(2.0, (midi - 69) / 12.0);

        static uint Hash(string s)
        {
            uint h = 2166136261u;
            foreach (char c in s)
            {
                h ^= c;
                h *= 16777619u;
            }
            return h == 0 ? 1u : h;
        }
    }

    /// <summary>
    /// Notes of the built-in melodies of the Melody pack (spec §2.7: public-domain works in own arrangement). Ids follow
    /// <c>CommonParams.Melodies</c>. Melodies without notes here play the pack's pentatonic fallback (Core
    /// <see cref="SoundEventProcessor"/>) until their arrangement is added.
    /// </summary>
    public static class BuiltInMelodies
    {
        static readonly Dictionary<string, Melody> s_melodies = Build();

        public static bool TryGet(string id, out Melody melody)
        {
            if (id == null)
            {
                melody = null;
                return false;
            }
            return s_melodies.TryGetValue(id, out melody);
        }

        public static IReadOnlyCollection<string> Ids => s_melodies.Keys;

        static Dictionary<string, Melody> Build()
        {
            var d = new Dictionary<string, Melody>(StringComparer.Ordinal);
            // Beethoven, Symphony No. 9 — "Ode to Joy" theme (E E F G | G F E D | C C D E | E. D D-).
            Add(d, "ode_to_joy", "Ode to Joy",
                "64 64 65 67 67 65 64 62 60 60 62 64 64:1.5 62:0.5 62:2 " +
                "64 64 65 67 67 65 64 62 60 60 62 64 62:1.5 60:0.5 60:2 " +
                "62 62 64 60 62 64:0.5 65:0.5 64 60 62 64:0.5 65:0.5 64 62 60 62 55:2 " +
                "64 64 65 67 67 65 64 62 60 60 62 64 62:1.5 60:0.5 60:2");
            // Beethoven, "Für Elise" — opening motif.
            Add(d, "fur_elise", "Für Elise",
                "76:0.5 75:0.5 76:0.5 75:0.5 76:0.5 71:0.5 74:0.5 72:0.5 69:1.5 60:0.5 64:0.5 69:0.5 71:1.5 64:0.5 68:0.5 71:0.5 72:1.5 " +
                "64:0.5 76:0.5 75:0.5 76:0.5 75:0.5 76:0.5 71:0.5 74:0.5 72:0.5 69:1.5 60:0.5 64:0.5 69:0.5 71:1.5 64:0.5 72:0.5 71:0.5 69:2");
            return d;
        }

        static void Add(Dictionary<string, Melody> d, string id, string name, string notes)
        {
            var list = new List<MelodyNote>();
            foreach (string token in notes.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int colon = token.IndexOf(':');
                int pitch = int.Parse(colon < 0 ? token : token.Substring(0, colon), System.Globalization.CultureInfo.InvariantCulture);
                float weight = colon < 0 ? 1f : float.Parse(token.Substring(colon + 1), System.Globalization.CultureInfo.InvariantCulture);
                list.Add(new MelodyNote(pitch, weight, 0.9f));
            }
            if (Melody.TryCreate(id, name, list, out Melody melody, out _)) d[id] = melody;
        }
    }

    /// <summary>
    /// Built-in background music of the studio's "Звук" tool, synthesized as gentle chord loops (interleaved stereo float at
    /// 48 kHz, 30 s each) until licensed tracks are added (spec §3.6). Building a track allocates ≈ 11.5 MB; the studio keeps
    /// it only while it is selected.
    /// </summary>
    public static class ProceduralMusic
    {
        public const int LengthSeconds = 30;

        static readonly string[] s_ids = { "calm_waves", "neon_drive", "sunny_steps" };
        static readonly string[] s_names = { "Calm Waves", "Neon Drive", "Sunny Steps" };

        public static IReadOnlyList<string> Ids => s_ids;

        public static string NameOf(string id)
        {
            int i = Array.IndexOf(s_ids, id);
            return i >= 0 ? s_names[i] : id;
        }

        public static long LengthUs => LengthSeconds * 1000000L;

        /// <summary>The track's samples, or null for an unknown id.</summary>
        public static float[] Render(string id)
        {
            int index = Array.IndexOf(s_ids, id);
            if (index < 0) return null;
            int[][] chords;
            double bpm;
            switch (index)
            {
                case 0:
                    chords = new[] { new[] { 57, 60, 64 }, new[] { 53, 57, 60 }, new[] { 48, 52, 55 }, new[] { 55, 59, 62 } };
                    bpm = 72;
                    break;
                case 1:
                    chords = new[] { new[] { 45, 52, 57, 60 }, new[] { 41, 48, 53, 57 }, new[] { 43, 50, 55, 59 }, new[] { 40, 47, 52, 56 } };
                    bpm = 112;
                    break;
                default:
                    chords = new[] { new[] { 60, 64, 67 }, new[] { 65, 69, 72 }, new[] { 67, 71, 74 }, new[] { 60, 64, 67 } };
                    bpm = 96;
                    break;
            }
            int rate = AudioConstants.SampleRate;
            int frames = LengthSeconds * rate;
            var data = new float[frames * 2];
            double beat = 60.0 / bpm;
            int beatFrames = (int)(beat * rate);
            for (int b = 0; b * beatFrames < frames; b++)
            {
                int[] chord = chords[(b / 4) % chords.Length];
                int note = chord[b % chord.Length] + (index == 1 ? 12 : 0);
                double hz = ProceduralSampleBank.MidiHz(note);
                int start = b * beatFrames;
                int len = Math.Min(beatFrames * 2, frames - start);
                double pan = (b % 2 == 0) ? -0.3 : 0.3;
                for (int i = 0; i < len; i++)
                {
                    double t = i / (double)rate;
                    double env = Math.Exp(-3.0 * t) * Math.Min(1.0, t * 200);
                    double s = 0.12 * env * (Math.Sin(2 * Math.PI * hz * t) + 0.3 * Math.Sin(4 * Math.PI * hz * t));
                    int f = start + i;
                    data[f * 2] += (float)(s * (1 - pan));
                    data[f * 2 + 1] += (float)(s * (1 + pan));
                }
                if (b % 4 == 0)
                {
                    double bassHz = ProceduralSampleBank.MidiHz(chord[0] - 12);
                    int bassLen = Math.Min(beatFrames * 4, frames - start);
                    for (int i = 0; i < bassLen; i++)
                    {
                        double t = i / (double)rate;
                        double s = 0.1 * Math.Exp(-1.2 * t) * Math.Min(1.0, t * 100) * Math.Sin(2 * Math.PI * bassHz * t);
                        int f = start + i;
                        data[f * 2] += (float)s;
                        data[f * 2 + 1] += (float)s;
                    }
                }
            }
            return data;
        }
    }
}
