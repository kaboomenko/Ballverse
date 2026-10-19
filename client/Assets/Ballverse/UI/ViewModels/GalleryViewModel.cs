using System.Collections.Generic;
using UnityEngine;

namespace Ballverse.UI.ViewModels
{
    /// <summary>One card of the mode gallery (release 1: three free modes, no locks).</summary>
    public sealed class ModeCardData
    {
        /// <summary>Stable mode id, e.g. "ring_escape".</summary>
        public string ModeId { get; }
        public string NameKey { get; }
        public string DescriptionKey { get; }
        /// <summary>"Интерактив" badge.</summary>
        public bool Interactive { get; }
        /// <summary>"Победитель" badge.</summary>
        public bool HasWinner { get; }
        /// <summary>"Новое" badge.</summary>
        public bool IsNew { get; }

        public ModeCardData(string modeId, string nameKey, string descriptionKey, bool interactive, bool hasWinner, bool isNew)
        {
            ModeId = modeId;
            NameKey = nameKey;
            DescriptionKey = descriptionKey;
            Interactive = interactive;
            HasWinner = hasWinner;
            IsNew = isNew;
        }

        /// <summary>Convention for mode strings: <c>mode.{id}.name</c> / <c>mode.{id}.description</c>.</summary>
        public static ModeCardData ForMode(string modeId, bool interactive, bool hasWinner, bool isNew)
        {
            return new ModeCardData(modeId, "mode." + modeId + ".name", "mode." + modeId + ".description", interactive, hasWinner, isNew);
        }
    }

    /// <summary>Tab "Режимы" (spec §4.3 gallery, release-1 scope).</summary>
    public interface IGalleryViewModel : IObservableViewModel
    {
        /// <summary>Cards in display order (order is Remote Config driven in the app).</summary>
        IReadOnlyList<ModeCardData> Modes { get; }
        /// <summary>Looping 3–4 s preview of a mode (e.g. a VideoPlayer target texture); null shows the placeholder.</summary>
        Texture GetPreview(string modeId);
        /// <summary>Start / stop preview playback while the gallery is on screen.</summary>
        void SetPreviewsActive(bool active);
        /// <summary>Prepare the simulation session of a mode; the UI then opens the simulation screen.</summary>
        void OpenMode(string modeId);
    }
}
