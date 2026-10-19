using System;
using Ballverse.Core.Audio;
using Ballverse.Core.RenderGeometry;
using Ballverse.Core.Replay;

namespace Ballverse.Core.Modes
{
    /// <summary>
    /// Events of one tick, emitted by the scene and drained by the session after the tick (spec §2.7, §7.4 EVNT, §7.7 VFX;
    /// RenderEventContract.md). The session sets <see cref="Tick"/> before the tick runs: it is the index of the frame that
    /// first shows the event (<c>World.Tick + 1</c> during a step, 0 during <see cref="Sim.IModeRules.Start"/>), and every Add
    /// method stamps it, so a mode never computes event ticks itself. Render event ids are assigned in emission order
    /// (unique within the tick, deterministic because emission follows the solver's sorted event order, §7.4 rule 4).
    /// Arrays grow geometrically and are reused: steady-state ticks do not allocate.
    /// </summary>
    public sealed class ModeEventBuffer
    {
        /// <summary>
        /// Replay body id of an event without an emitting body (finale, escape of a team): a fixed non-negative id outside every
        /// mode's render body ids. Sound events of such replay events get body id −1 (exempt from the per-body rate limit).
        /// </summary>
        public const int BodilessReplayBodyId = int.MaxValue;

        RenderEvent[] _render = new RenderEvent[32];
        SoundEvent[] _sound = new SoundEvent[32];
        ReplayEvent[] _replay = new ReplayEvent[32];
        int _renderCount;
        int _soundCount;
        int _replayCount;

        /// <summary>Frame tick stamped on every event added from now on (set by the session).</summary>
        public int Tick { get; set; }

        /// <summary>False when no render-event sink is attached (headless runs): modes may skip building render events.</summary>
        public bool WantsRender { get; set; } = true;

        /// <summary>False when no sound sink is attached.</summary>
        public bool WantsSound { get; set; } = true;

        /// <summary>False when no replay writer is attached.</summary>
        public bool WantsReplay { get; set; } = true;

        public ReadOnlySpan<RenderEvent> RenderEvents => new ReadOnlySpan<RenderEvent>(_render, 0, _renderCount);

        public ReadOnlySpan<SoundEvent> SoundEvents => new ReadOnlySpan<SoundEvent>(_sound, 0, _soundCount);

        public ReadOnlySpan<ReplayEvent> ReplayEvents => new ReadOnlySpan<ReplayEvent>(_replay, 0, _replayCount);

        /// <summary>Adds a render event; its <see cref="RenderEvent.Tick"/> and <see cref="RenderEvent.Id"/> are overwritten.</summary>
        public void AddRender(RenderEvent e)
        {
            if (!WantsRender) return;
            e.Tick = Tick;
            e.Id = _renderCount;
            if (_renderCount == _render.Length) Array.Resize(ref _render, _render.Length * 2);
            _render[_renderCount++] = e;
        }

        /// <summary>Adds a sound event; its <see cref="SoundEvent.Tick"/> is overwritten.</summary>
        public void AddSound(SoundEvent e)
        {
            if (!WantsSound) return;
            e.Tick = Tick;
            if (_soundCount == _sound.Length) Array.Resize(ref _sound, _sound.Length * 2);
            _sound[_soundCount++] = e;
        }

        /// <summary>
        /// Adds an EVNT record (spec §7.4) at <see cref="Tick"/>. <paramref name="bodyId"/> is the emitting body's render-stream id
        /// (so audio can take its size and pan from the stream, see <c>Integration.ISoundBodySource</c>) or
        /// <see cref="BodilessReplayBodyId"/>.
        /// </summary>
        public void AddReplay(ReplayEventType type, int bodyId, ushort impulse, long extra)
        {
            if (!WantsReplay) return;
            if (_replayCount == _replay.Length) Array.Resize(ref _replay, _replay.Length * 2);
            _replay[_replayCount++] = new ReplayEvent((uint)Tick, type, bodyId, impulse, extra);
        }

        /// <summary>Drops the drained events (called by the session after every tick).</summary>
        public void Clear()
        {
            _renderCount = 0;
            _soundCount = 0;
            _replayCount = 0;
        }
    }
}
