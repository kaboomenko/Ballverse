using System;
using Ballverse.Core.Integration;

namespace Ballverse.Core.Modes
{
    /// <summary>
    /// Optional interface of an <see cref="IModeDefinition"/> whose live sound events carry values that the generic replay →
    /// sound conversion cannot know (CONTRACT §3 rule 4): a pitch size tied to progress instead of body size, an emitter that is
    /// not the body named by the EVNT record, a bodiless sound. The export and the Studio preview rebuild sound from EVNT
    /// records with a mode-agnostic <see cref="ISoundBodySource"/> over the render stream; wrapping it with
    /// <see cref="WrapSoundBodySource"/> makes the converted list equal the live list field for field (spec §2.7, §7.8).
    /// Modes whose live events follow the generic convention (EVNT body id = render id, size = body diameter) need not
    /// implement it.
    /// </summary>
    public interface IModeSoundMapping
    {
        /// <summary>
        /// A source that answers like the mode's live run, delegating size / pan of the emitting body to
        /// <paramref name="inner"/> (normally the render-stream source of the clip). Never null.
        /// </summary>
        ISoundBodySource WrapSoundBodySource(ISoundBodySource inner);
    }

    /// <summary>
    /// Mode-agnostic resolution of <see cref="IModeSoundMapping"/> for code that only knows the replay header's
    /// <c>modeId</c> (Studio mixer, export). Usage:
    /// <c>ReplaySoundEvents.Convert(replay, ModeSoundMapping.ForReplay(ReleaseModes.CreateRegistry(), replay.Header.ModeId, new RenderStreamSoundBodySource(reader)))</c>.
    /// </summary>
    public static class ModeSoundMapping
    {
        /// <summary><paramref name="inner"/> wrapped by <paramref name="mode"/> when it implements <see cref="IModeSoundMapping"/>, else <paramref name="inner"/>.</summary>
        public static ISoundBodySource ForMode(IModeDefinition mode, ISoundBodySource inner)
        {
            if (inner == null) throw new ArgumentNullException(nameof(inner));
            if (mode is IModeSoundMapping mapping)
            {
                ISoundBodySource wrapped = mapping.WrapSoundBodySource(inner);
                return wrapped ?? inner;
            }
            return inner;
        }

        /// <summary>
        /// The source for a replay of mode <paramref name="modeId"/>: wrapped by the registered mode, or <paramref name="inner"/>
        /// unchanged when the id is unknown or null (a clip of a mode this build does not have still mixes generically).
        /// </summary>
        public static ISoundBodySource ForReplay(ModeRegistry registry, string modeId, ISoundBodySource inner)
        {
            if (registry == null) throw new ArgumentNullException(nameof(registry));
            if (inner == null) throw new ArgumentNullException(nameof(inner));
            if (modeId != null && registry.TryGet(modeId, out IModeDefinition mode)) return ForMode(mode, inner);
            return inner;
        }
    }
}
