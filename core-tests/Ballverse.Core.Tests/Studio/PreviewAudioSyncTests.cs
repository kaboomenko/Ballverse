using System;
using System.Collections.Generic;
using Ballverse.Core.Studio;
using Xunit;

namespace Ballverse.Core.Tests.Studio
{
    /// <summary>Preview A/V sync (QA round 1 blocker: a stalled audio cursor froze the picture).</summary>
    public class PreviewAudioSyncTests
    {
        const long Tick = 16_667;        // 60 Hz preview frames
        const long Block = 21_333;       // 1024 frames at 48 kHz per audio callback
        const long Latency = 4 * Block;  // 4 DSP buffers

        /// <summary>Audio thread stand-in: the cursor advances one block per callback period while it runs.</summary>
        sealed class FakeAudio
        {
            public long CursorUs;
            public bool Running = true;
            long _sinceBlock;

            public void Seek(long us)
            {
                CursorUs = us;
                _sinceBlock = 0;
            }

            public void Elapse(long us)
            {
                if (!Running) return;
                _sinceBlock += us;
                while (_sinceBlock >= Block)
                {
                    _sinceBlock -= Block;
                    CursorUs += Block;
                }
            }
        }

        static (PlaybackClock clock, PreviewAudioSync sync, FakeAudio audio) Start(long atUs)
        {
            var clock = new PlaybackClock(60_000_000, 60);
            var sync = PreviewAudioSync.ForOutput(60, Block, Latency);
            var audio = new FakeAudio();
            clock.Seek(atUs);
            clock.Play();
            audio.Seek(atUs);
            sync.Start(atUs, audio.CursorUs);
            return (clock, sync, audio);
        }

        static void Step(PlaybackClock clock, PreviewAudioSync sync, FakeAudio audio, int ticks, List<long> positions = null)
        {
            for (int i = 0; i < ticks; i++)
            {
                audio.Elapse(Tick);
                if (sync.NeedsAudioResync)
                {
                    audio.Seek(clock.PositionUs);
                    sync.Start(clock.PositionUs, audio.CursorUs);
                }
                long p = sync.Advance(clock, Tick, true, audio.CursorUs, Latency);
                positions?.Add(p);
            }
        }

        [Fact]
        public void DeadAudioOutputDoesNotFreezeThePicture()
        {
            var (clock, sync, audio) = Start(3_000_000);
            audio.Running = false; // no AudioListener / interruption: OnAudioFilterRead never runs
            Step(clock, sync, audio, 120);
            Assert.True(clock.IsPlaying);
            Assert.True(sync.AudioStalled);
            Assert.Equal(PreviewClockSource.WallClock, sync.Source);
            // 2 s of frames minus at most the stall window.
            Assert.InRange(clock.PositionUs, 3_000_000 + 2_000_000 - sync.StallUs - Tick, 3_000_000 + 2_000_000 + Tick);
        }

        [Fact]
        public void PlayHoldsThePictureDuringTheDspLatencyInsteadOfJumpingBack()
        {
            var (clock, sync, audio) = Start(3_000_000);
            var positions = new List<long>();
            Step(clock, sync, audio, 60, positions);
            long previous = 3_000_000;
            foreach (long p in positions)
            {
                Assert.True(p >= previous, "picture moved back from " + previous + " to " + p);
                previous = p;
            }
            Assert.Equal(PreviewClockSource.Audio, sync.Source);
            long audible = audio.CursorUs - Latency;
            Assert.InRange(clock.PositionUs - audible, -Block - Tick, Block + Tick); // within about one frame of the sound
        }

        [Fact]
        public void PictureTracksRunningAudio()
        {
            var (clock, sync, audio) = Start(0);
            Step(clock, sync, audio, 600);
            Assert.Equal(PreviewClockSource.Audio, sync.Source);
            Assert.False(sync.AudioStalled);
            long audible = audio.CursorUs - Latency;
            Assert.InRange(Math.Abs(clock.PositionUs - audible), 0, 2 * Tick);
        }

        [Fact]
        public void AudioThatResumesIsResyncedToThePicture()
        {
            var (clock, sync, audio) = Start(1_000_000);
            Step(clock, sync, audio, 30);
            audio.Running = false;
            Step(clock, sync, audio, 60);
            Assert.True(sync.AudioStalled);
            long whileStalled = clock.PositionUs;
            audio.Running = true;
            var positions = new List<long>();
            Step(clock, sync, audio, 60, positions);
            Assert.False(sync.AudioStalled);
            Assert.Equal(PreviewClockSource.Audio, sync.Source);
            // No jump back to the stale audio position.
            long previous = whileStalled;
            foreach (long p in positions)
            {
                Assert.True(p >= previous);
                previous = p;
            }
            Assert.InRange(Math.Abs(clock.PositionUs - (audio.CursorUs - Latency)), 0, 2 * Tick);
        }

        [Fact]
        public void WithoutAMixTheClockRunsFree()
        {
            var clock = new PlaybackClock(10_000_000, 60);
            var sync = PreviewAudioSync.ForOutput(60, Block, Latency);
            clock.Play();
            for (int i = 0; i < 60; i++) sync.Advance(clock, Tick, false, 0, Latency);
            Assert.Equal(60 * Tick, clock.PositionUs);
            Assert.Equal(PreviewClockSource.WallClock, sync.Source);
            clock.Pause();
            sync.Advance(clock, Tick, true, 0, Latency);
            Assert.Equal(PreviewClockSource.Stopped, sync.Source);
        }

        [Fact]
        public void ThresholdsCoverBigAudioBuffers()
        {
            Assert.Equal(2 * 16_667, PreviewAudioSync.StallThresholdUs(60, 1_000));
            Assert.Equal(3 * 85_333, PreviewAudioSync.StallThresholdUs(60, 85_333));
            Assert.Throws<ArgumentOutOfRangeException>(() => new PreviewAudioSync(0, 0));
            Assert.Throws<ArgumentNullException>(() => PreviewAudioSync.ForOutput(30, 0, 0).Advance(null, 1, true, 0, 0));
        }
    }
}
