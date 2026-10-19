namespace Ballverse.UI.Accessibility
{
    /// <summary>What the screen-reader bridge does after reading the visible UI.</summary>
    public enum ScreenReaderStep
    {
        /// <summary>Nothing this time.</summary>
        Idle = 0,
        /// <summary>Elements that should be read have no layout yet: keep the change pending and read again next frame.</summary>
        Wait = 1,
        /// <summary>Publish a new hierarchy and announce a screen change (screen, sheet, dialog or language changed).</summary>
        PublishScreen = 2,
        /// <summary>Publish a new hierarchy and announce a layout change (something appeared or disappeared on the screen).</summary>
        PublishLayout = 3,
        /// <summary>Same nodes as published: only move their frames.</summary>
        RefreshFrames = 4,
    }

    /// <summary>
    /// When the screen-reader hierarchy (spec §4.8, VoiceOver / TalkBack) is rebuilt. Pure.
    /// <para>
    /// A rebuild must read laid-out elements: a VisualElement added this frame has no layout (NaN bounds) and unresolved
    /// styles until the next panel update, so reading it at once would drop it. A change (<see cref="MarkDirty"/>) is
    /// therefore read every frame until the reachable elements all have a layout, at most
    /// <see cref="MaxLayoutWaitSeconds"/>. Besides, the visible UI is read every <see cref="CheckSeconds"/> and the
    /// hierarchy republished when its nodes differ, which covers what no event reports: transitions that end, a result
    /// card or a hint that appears, chrome that hides and shows.
    /// </para>
    /// </summary>
    public sealed class ScreenReaderSync
    {
        public const float CheckSeconds = 0.5f;
        public const float MaxLayoutWaitSeconds = 1f;
        /// <summary>Periodic reads in a row that may find unlaid elements before the nodes found are published anyway.</summary>
        public const int MaxNotReadyChecks = 2;

        bool _dirty = true;
        float _sinceCheck;
        float _waited;
        int _notReadyChecks;

        /// <summary>A change is pending.</summary>
        public bool Dirty => _dirty;

        /// <summary>Something visible changed (screen, sheet, dialog, language, screen reader turned on).</summary>
        public void MarkDirty()
        {
            _dirty = true;
        }

        /// <summary>Advances time. True when the visible UI must be read now: a change is pending or a check is due.</summary>
        public bool Due(float deltaTime)
        {
            if (deltaTime > 0f)
            {
                _sinceCheck += deltaTime;
                if (_dirty) _waited += deltaTime;
            }
            if (_dirty) return true;
            if (_sinceCheck < CheckSeconds) return false;
            _sinceCheck = 0f;
            return true;
        }

        /// <param name="layoutReady">Every shown element of the reachable layers has a layout.</param>
        /// <param name="changed">The nodes read differ from the published ones.</param>
        public ScreenReaderStep Decide(bool layoutReady, bool changed)
        {
            if (_dirty)
            {
                if (!layoutReady && _waited < MaxLayoutWaitSeconds) return ScreenReaderStep.Wait;
                _dirty = false;
                _waited = 0f;
                _sinceCheck = 0f;
                _notReadyChecks = 0;
                return ScreenReaderStep.PublishScreen;
            }
            if (!layoutReady && ++_notReadyChecks < MaxNotReadyChecks) return ScreenReaderStep.Idle;
            _notReadyChecks = 0;
            return changed ? ScreenReaderStep.PublishLayout : ScreenReaderStep.RefreshFrames;
        }
    }
}
