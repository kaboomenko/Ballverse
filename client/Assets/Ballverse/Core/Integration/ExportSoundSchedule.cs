using System;
using System.Collections.Generic;
using Ballverse.Core.Audio;
using Ballverse.Core.Timeline;

namespace Ballverse.Core.Integration
{
    /// <summary>
    /// Places simulation sound events on the export's output timeline (spec §3.6, §3.7 "в экспорте — 0", §7.8): every time the
    /// studio timeline starts playing an event's tick (<see cref="StudioTimeline.GetTickOutputTimesUs"/>: the first pass at
    /// speed S with finale slow-mo, every <see cref="SimAfterEnd.Loop"/> and <see cref="SimAfterEnd.SlowMoReplay"/> repeat,
    /// and <see cref="SimAfterEnd.ContinueSim"/> ticks) becomes one <see cref="ScheduledSoundEvent"/> at that time in 48 kHz
    /// frames. Frozen frames (cover, pre-roll, Freeze, end of stream) start no tick and so play no event.
    /// <para><b>Decision</b> (timeline open issue): events repeat with every visible repetition of their tick, so a looped or
    /// replayed finale sounds as it looks; the mixer's rate limits run on the output timeline, so a dense replay is thinned
    /// exactly as the listener would hear it.</para>
    /// Frames: <c>floor(µs · 48000 / 10⁶)</c> of the timeline's whole-µs time (rounded up from the exact time), which gives
    /// exactly tick × 800 frames at 1× (the µs round-up is below one frame).
    /// </summary>
    public static class ExportSoundSchedule
    {
        /// <summary>Scheduled events sorted by frame (stable: equal frames keep event order), ready for <see cref="EventMixer.RenderOffline(IReadOnlyList{ScheduledSoundEvent}, Span{float})"/>.</summary>
        public static ScheduledSoundEvent[] Build(StudioTimeline timeline, IReadOnlyList<SoundEvent> events)
        {
            if (timeline == null) throw new ArgumentNullException(nameof(timeline));
            if (events == null) throw new ArgumentNullException(nameof(events));
            var result = new List<ScheduledSoundEvent>(events.Count);
            var times = new List<long>(4);
            for (int i = 0; i < events.Count; i++)
            {
                SoundEvent e = events[i];
                times.Clear();
                timeline.GetTickOutputTimesUs(e.Tick, times);
                for (int k = 0; k < times.Count; k++) result.Add(new ScheduledSoundEvent(e, AudioConstants.MicrosToFrames(times[k])));
            }
            var array = result.ToArray();
            StableSortByFrame(array);
            return array;
        }

        /// <summary>Output length of the export's audio, frames: the video duration (FrameCount / fps) at 48 kHz, rounded up.</summary>
        public static long AudioFrameCount(StudioTimeline timeline)
        {
            if (timeline == null) throw new ArgumentNullException(nameof(timeline));
            return CeilDiv((long)timeline.FrameCount * AudioConstants.SampleRate, timeline.Fps);
        }

        static long CeilDiv(long a, long b)
        {
            return (a + b - 1) / b;
        }

        static void StableSortByFrame(ScheduledSoundEvent[] a)
        {
            // Introsort over (frame, original index): deterministic and equal to a stable sort by frame.
            var order = new int[a.Length];
            for (int i = 0; i < order.Length; i++) order[i] = i;
            var copy = (ScheduledSoundEvent[])a.Clone();
            Array.Sort(order, (x, y) =>
            {
                int c = copy[x].Frame.CompareTo(copy[y].Frame);
                return c != 0 ? c : x.CompareTo(y);
            });
            for (int i = 0; i < order.Length; i++) a[i] = copy[order[i]];
        }
    }
}
