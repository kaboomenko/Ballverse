using System;
using System.Collections.Generic;

namespace Ballverse.Core.Audio
{
    /// <summary>
    /// Deterministic event mixer (spec §2.7 mixer rules, §7.8). Turns <see cref="SoundEvent"/>s into interleaved stereo float
    /// PCM at 48 kHz. The SAME code runs realtime (<see cref="Render"/> with arbitrary chunk sizes, events through
    /// <see cref="Queue"/>) and offline (<see cref="RenderOffline(IReadOnlyList{SoundEvent}, Span{float})"/>), and the output
    /// is bit-identical for any chunking and between the two paths as long as every realtime event is queued before the
    /// render position passes its frame (a late event starts at the current position instead). Events must be queued in
    /// non-decreasing output-frame order.
    /// <list type="bullet">
    /// <item>Placement: an event at tick t starts exactly at output frame (t − OriginTick) × 800; a
    /// <see cref="ScheduledSoundEvent"/> (or a queue entry with an output frame) starts at its frame instead, for playback
    /// speeds other than 1× (spec §2.2 timeScale, §3.4 studio speed and slow-mo; see <see cref="PlaybackTimeMap"/>).
    /// Rate limits use the output frame; sample variants use the simulation tick, so they match on every timeline.</item>
    /// <item>A sound whose start frame is before output frame 0 (e.g. triggered just before the trim point
    /// <see cref="EventMixerOptions.OriginTick"/>) starts at frame 0 part-way through its sample, exactly as it sounds at
    /// that moment in an untrimmed render; sounds already finished by frame 0 only update the limits and the melody.</item>
    /// <item>Decisions (pack rules, rate limits, melody) come from <see cref="SoundEventProcessor"/>.</item>
    /// <item>At most 32 voices. When full, the quietest voice by envelope-aware level gain × remaining/length is stolen;
    /// ties go to the earliest start frame, then the lowest slot. If the new sound is quieter than every playing voice it is
    /// the one dropped — except a melody note, which always steals: the melody already advanced, so dropping it would skip
    /// a note of the tune (spec §2.7 «каждая нота значима»).</item>
    /// <item>Declick: a stolen voice leaves the 32-voice pool at once and its remaining sound fades out linearly over
    /// <see cref="StealFadeFrames"/> frames (2.7 ms) in one of <see cref="ReleaseSlots"/> release slots. These fading tails
    /// are not voices (they cannot be stolen or restarted), but for up to 2.7 ms after a steal up to 40 sources are summed;
    /// a hard cut would click. Accepted reading of spec §2.7 "max 32 voices".</item>
    /// <item>Pitch: whole-semitone ratio as 32.32 fixed-point step, linear interpolation between source frames.</item>
    /// <item>Pan: constant power (<see cref="AudioMath.ConstantPowerPan"/>), applied to mono and to both channels of stereo samples.</item>
    /// </list>
    /// Output is not clipped (export mastering limits it, spec §3.6). No allocations per <see cref="Render"/> call.
    /// Not thread-safe except for the producer side of <see cref="Queue"/>.
    /// </summary>
    public sealed class EventMixer
    {
        /// <summary>Linear fade-out length of a stolen voice, frames (≈2.7 ms).</summary>
        public const int StealFadeFrames = 128;

        /// <summary>Release (fading) slots for stolen voices; the oldest fade is cut when all are busy.</summary>
        public const int ReleaseSlots = 8;

        const float FracScale = 1f / 16777216f;
        const double FixedOne = 4294967296.0;

        struct Voice
        {
            public bool Active;
            public string SampleId;
            public float[] Data;
            public int Channels;
            public int Frames;
            public long Pos;
            public long Step;
            public float GainL;
            public float GainR;
            public float Gain;
            public int Semitones;
            public long StartFrame;
            public int Fade;
        }

        interface IEventFeed
        {
            bool TryPeek(out SoundEvent e, out long frame);
            void Pop();
        }

        struct QueueFeed : IEventFeed
        {
            public SoundEventQueue Queue;
            public int OriginTick;

            public bool TryPeek(out SoundEvent e, out long frame)
            {
                if (!Queue.TryPeek(out e, out frame)) return false;
                if (frame == SoundEventQueue.AtTick) frame = AudioConstants.TickToFrame((long)e.Tick - OriginTick);
                return true;
            }

            public void Pop()
            {
                Queue.TryDequeue(out _);
            }
        }

        struct ArrayFeed : IEventFeed
        {
            public SoundEvent[] Events;
            public long[] Frames;
            public int Index;

            public bool TryPeek(out SoundEvent e, out long frame)
            {
                if (Index < Events.Length)
                {
                    e = Events[Index];
                    frame = Frames[Index];
                    return true;
                }
                e = default;
                frame = 0;
                return false;
            }

            public void Pop()
            {
                Index++;
            }
        }

        readonly SoundEventProcessor _processor;
        readonly ISampleBank _bank;
        readonly SoundEventQueue _queue;
        readonly float _masterGain;
        readonly int _originTick;
        readonly Voice[] _voices = new Voice[AudioConstants.MaxVoices];
        readonly Voice[] _releases = new Voice[ReleaseSlots];
        long _position;
        int _started;
        int _stolen;
        int _droppedQuiet;
        int _missing;

        /// <summary>
        /// Creates a mixer for one sound pack (validated; see <see cref="SoundEventProcessor"/>) and sample bank.
        /// Changing the sound pack in the studio = a new mixer over the same events; no re-simulation (spec §3.6, §7.8).
        /// </summary>
        public EventMixer(SoundPack pack, ISampleBank bank, Melody melody = null, EventMixerOptions options = null)
        {
            if (bank == null) throw new ArgumentNullException(nameof(bank));
            options = options ?? new EventMixerOptions();
            _processor = new SoundEventProcessor(pack, melody);
            _bank = bank;
            _queue = new SoundEventQueue(options.QueueCapacity);
            _masterGain = AudioMath.SanitizeGain(options.MasterGain, 0f, 4f, 1f);
            _originTick = options.OriginTick;
        }

        /// <summary>
        /// Realtime event input (producer: simulation thread). <c>TryEnqueue(e)</c> places the event at its tick (1× playback);
        /// <c>TryEnqueue(e, frame)</c> at an output frame, e.g. from a <see cref="PlaybackTimeMap"/> under timeScale ≠ 1.
        /// Entries must be queued in non-decreasing frame order.
        /// </summary>
        public SoundEventQueue Queue => _queue;

        public SoundEventProcessor Processor => _processor;

        /// <summary>Frames rendered so far (output frame of the next sample).</summary>
        public long Position => _position;

        public int ActiveVoiceCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _voices.Length; i++) if (_voices[i].Active) n++;
                return n;
            }
        }

        /// <summary>Voices started so far.</summary>
        public int VoicesStarted => _started;
        /// <summary>Voices stolen to make room.</summary>
        public int VoicesStolen => _stolen;
        /// <summary>New sounds dropped because they were quieter than every playing voice.</summary>
        public int VoicesDroppedQuiet => _droppedQuiet;
        /// <summary>Accepted sounds whose sample id is not in the bank.</summary>
        public int MissingSamples => _missing;

        /// <summary>Output frame of an event tick: (tick − OriginTick) × 800.</summary>
        public long FrameOfTick(int tick)
        {
            return AudioConstants.TickToFrame((long)tick - _originTick);
        }

        /// <summary>Snapshot of voice slot 0…31; false when the slot is free.</summary>
        public bool TryGetVoice(int slot, out VoiceInfo info)
        {
            if (slot < 0 || slot >= _voices.Length || !_voices[slot].Active)
            {
                info = default;
                return false;
            }
            ref Voice v = ref _voices[slot];
            info = new VoiceInfo
            {
                SampleId = v.SampleId,
                StartFrame = v.StartFrame,
                Gain = v.Gain,
                CurrentLevel = CurrentLevel(ref v),
                Semitones = v.Semitones,
                SourceFrame = v.Pos >> 32,
            };
            return true;
        }

        /// <summary>
        /// Realtime render: fills <paramref name="interleaved"/> (L, R, L, R…; even length) with the next frames, consuming
        /// queued events whose frame falls inside this chunk. Allocation-free.
        /// </summary>
        public void Render(Span<float> interleaved)
        {
            var feed = new QueueFeed { Queue = _queue, OriginTick = _originTick };
            RenderCore(interleaved, ref feed);
        }

        /// <summary>
        /// Offline render (export, spec §3.6/§7.8): resets the mixer, then renders <paramref name="events"/> (stably sorted by
        /// tick) into <paramref name="output"/> from frame 0. Bit-identical to realtime rendering of the same events.
        /// </summary>
        public void RenderOffline(IReadOnlyList<SoundEvent> events, Span<float> output)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            int n = events.Count;
            var frames = new long[n];
            for (int i = 0; i < n; i++) frames[i] = FrameOfTick(events[i].Tick);
            var copy = new SoundEvent[n];
            for (int i = 0; i < n; i++) copy[i] = events[i];
            RenderSorted(copy, frames, output);
        }

        /// <summary>
        /// Offline render of events with explicit output frames (studio speed S and finale slow-mo, spec §3.4): resets the
        /// mixer, stably sorts by frame and renders into <paramref name="output"/> from frame 0. Bit-identical to realtime
        /// rendering of the same entries queued with the same frames.
        /// </summary>
        public void RenderOffline(IReadOnlyList<ScheduledSoundEvent> events, Span<float> output)
        {
            if (events == null) throw new ArgumentNullException(nameof(events));
            int n = events.Count;
            var frames = new long[n];
            var copy = new SoundEvent[n];
            for (int i = 0; i < n; i++)
            {
                copy[i] = events[i].Event;
                frames[i] = events[i].Frame;
            }
            RenderSorted(copy, frames, output);
        }

        void RenderSorted(SoundEvent[] events, long[] frames, Span<float> output)
        {
            Reset();
            SortStable(events, frames);
            var feed = new ArrayFeed { Events = events, Frames = frames, Index = 0 };
            RenderCore(output, ref feed);
        }

        /// <summary>Convenience: a new mixer renders <paramref name="frameCount"/> frames of <paramref name="events"/>.</summary>
        public static float[] RenderOffline(SoundPack pack, ISampleBank bank, Melody melody, IReadOnlyList<SoundEvent> events,
            int frameCount, EventMixerOptions options = null)
        {
            if (frameCount < 0 || frameCount > int.MaxValue / 2) throw new ArgumentOutOfRangeException(nameof(frameCount));
            var mixer = new EventMixer(pack, bank, melody, options);
            var output = new float[frameCount * 2];
            mixer.RenderOffline(events, output);
            return output;
        }

        /// <summary>Stops all voices, clears limits, melody position, queue and position. Not concurrent with the producer.</summary>
        public void Reset()
        {
            Array.Clear(_voices, 0, _voices.Length);
            Array.Clear(_releases, 0, _releases.Length);
            _processor.Reset();
            _queue.Clear();
            _position = 0;
            _started = 0;
            _stolen = 0;
            _droppedQuiet = 0;
            _missing = 0;
        }

        void RenderCore<TFeed>(Span<float> buffer, ref TFeed feed) where TFeed : struct, IEventFeed
        {
            if ((buffer.Length & 1) != 0) throw new ArgumentException("interleaved stereo buffer length must be even", nameof(buffer));
            buffer.Clear();
            int frames = buffer.Length >> 1;
            long chunkStart = _position;
            long chunkEnd = chunkStart + frames;
            int cursor = 0;

            while (feed.TryPeek(out SoundEvent e, out long eventFrame))
            {
                if (eventFrame >= chunkEnd) break;
                feed.Pop();
                long at = eventFrame > chunkStart + cursor ? eventFrame : chunkStart + cursor;
                int target = (int)(at - chunkStart);
                if (target > cursor)
                {
                    RenderVoices(buffer, cursor, target);
                    cursor = target;
                }
                Trigger(in e, eventFrame, at);
            }
            RenderVoices(buffer, cursor, frames);
            _position = chunkEnd;
        }

        void Trigger(in SoundEvent e, long eventFrame, long at)
        {
            if (_processor.Process(in e, eventFrame, out VoiceRequest req) != SoundDecision.Played) return;
            if (!_bank.TryGetSample(req.SampleId, out AudioSample sample) || sample == null || sample.FrameCount == 0)
            {
                _missing++;
                return;
            }

            long step = (long)Math.Round(AudioMath.SemitoneRatio(req.Semitones) * FixedOne);
            long pos = 0;
            if (eventFrame < 0)
            {
                // Started before output frame 0: join part-way, at the source position an untrimmed render has here.
                // The finished-check runs in double so even long.MinValue (whose negation overflows) is caught before any
                // long arithmetic; past it, skip · step < FrameCount · 2^32 cannot overflow.
                if (-(double)eventFrame * step >= (double)sample.FrameCount * FixedOne) return; // already finished
                pos = -eventFrame * step;
            }

            int slot = -1;
            for (int i = 0; i < _voices.Length; i++)
            {
                if (!_voices[i].Active)
                {
                    slot = i;
                    break;
                }
            }
            if (slot < 0)
            {
                slot = QuietestSlot();
                double newLevel = (double)req.Gain * (sample.FrameCount - (pos >> 32)) / sample.FrameCount;
                if (req.MelodyIndex < 0 && newLevel < CurrentLevel(ref _voices[slot]))
                {
                    _droppedQuiet++;
                    return;
                }
                MoveToRelease(ref _voices[slot]);
                _stolen++;
            }

            AudioMath.ConstantPowerPan(req.Pan, out float panL, out float panR);
            float g = req.Gain * _masterGain;
            _voices[slot] = new Voice
            {
                Active = true,
                SampleId = req.SampleId,
                Data = sample.RawData,
                Channels = sample.Channels,
                Frames = sample.FrameCount,
                Pos = pos,
                Step = step,
                GainL = g * panL,
                GainR = g * panR,
                Gain = req.Gain,
                Semitones = req.Semitones,
                StartFrame = at,
                Fade = 0,
            };
            _started++;
        }

        int QuietestSlot()
        {
            int best = 0;
            double bestLevel = CurrentLevel(ref _voices[0]);
            for (int i = 1; i < _voices.Length; i++)
            {
                double level = CurrentLevel(ref _voices[i]);
                if (level < bestLevel || (level == bestLevel && _voices[i].StartFrame < _voices[best].StartFrame))
                {
                    best = i;
                    bestLevel = level;
                }
            }
            return best;
        }

        static double CurrentLevel(ref Voice v)
        {
            long remaining = v.Frames - (v.Pos >> 32);
            if (remaining < 0) remaining = 0;
            return (double)v.Gain * remaining / v.Frames;
        }

        void MoveToRelease(ref Voice stolen)
        {
            int slot = -1;
            for (int i = 0; i < _releases.Length; i++)
            {
                if (!_releases[i].Active)
                {
                    slot = i;
                    break;
                }
            }
            if (slot < 0)
            {
                // All fades busy: cut the one closest to silence (least fade left), lowest slot on ties.
                slot = 0;
                for (int i = 1; i < _releases.Length; i++)
                {
                    if (_releases[i].Fade < _releases[slot].Fade) slot = i;
                }
            }
            _releases[slot] = stolen;
            _releases[slot].Fade = StealFadeFrames;
            stolen.Active = false;
        }

        void RenderVoices(Span<float> buffer, int from, int to)
        {
            if (to <= from) return;
            for (int i = 0; i < _voices.Length; i++)
            {
                if (_voices[i].Active) RenderVoice(ref _voices[i], buffer, from, to);
            }
            for (int i = 0; i < _releases.Length; i++)
            {
                if (_releases[i].Active) RenderVoice(ref _releases[i], buffer, from, to);
            }
        }

        static void RenderVoice(ref Voice v, Span<float> buffer, int from, int to)
        {
            float[] d = v.Data;
            int frames = v.Frames;
            long pos = v.Pos;
            long step = v.Step;
            float gl = v.GainL;
            float gr = v.GainR;
            int fade = v.Fade;
            bool fading = fade > 0;
            const float fadeScale = 1f / StealFadeFrames;
            int o = from << 1;

            for (int i = from; i < to; i++)
            {
                long idx = pos >> 32;
                if (idx >= frames || (fading && fade <= 0))
                {
                    v.Active = false;
                    break;
                }
                float frac = ((pos & 0xFFFFFFFFL) >> 8) * FracScale;
                float l, r;
                if (v.Channels == 1)
                {
                    float s0 = d[idx];
                    float s1 = idx + 1 < frames ? d[idx + 1] : 0f;
                    float s = s0 + (s1 - s0) * frac;
                    l = s;
                    r = s;
                }
                else
                {
                    long k = idx << 1;
                    float l0 = d[k];
                    float r0 = d[k + 1];
                    float l1 = idx + 1 < frames ? d[k + 2] : 0f;
                    float r1 = idx + 1 < frames ? d[k + 3] : 0f;
                    l = l0 + (l1 - l0) * frac;
                    r = r0 + (r1 - r0) * frac;
                }
                if (fading)
                {
                    float f = fade * fadeScale;
                    buffer[o] += l * gl * f;
                    buffer[o + 1] += r * gr * f;
                    fade--;
                }
                else
                {
                    buffer[o] += l * gl;
                    buffer[o + 1] += r * gr;
                }
                o += 2;
                pos += step;
            }
            v.Pos = pos;
            v.Fade = fade;
            if (fading && fade <= 0) v.Active = false;
            if ((pos >> 32) >= frames) v.Active = false;
        }

        /// <summary>Sorts both arrays by frame; equal frames keep their input order.</summary>
        static void SortStable(SoundEvent[] events, long[] frames)
        {
            int n = events.Length;
            bool sorted = true;
            for (int i = 1; i < n && sorted; i++) sorted = frames[i - 1] <= frames[i];
            if (sorted) return;
            var keys = (long[])frames.Clone();
            var order = new int[n];
            for (int i = 0; i < n; i++) order[i] = i;
            Array.Sort(keys, order);
            for (int start = 0; start < n;)
            {
                int end = start + 1;
                while (end < n && keys[end] == keys[start]) end++;
                if (end - start > 1) Array.Sort(order, start, end - start);
                start = end;
            }
            var copy = (SoundEvent[])events.Clone();
            for (int i = 0; i < n; i++)
            {
                events[i] = copy[order[i]];
                frames[i] = keys[i];
            }
        }
    }
}
