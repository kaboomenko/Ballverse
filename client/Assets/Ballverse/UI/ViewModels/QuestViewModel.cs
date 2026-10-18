namespace Ballverse.UI.ViewModels
{
    /// <summary>Content of the one-time "first video" celebration (spec §4.3, release 1).</summary>
    public sealed class QuestCelebrationData
    {
        /// <summary>The other release-1 mode to try next (M02 <c>geometry_bounce</c> or M05 <c>helix_drop</c>).</summary>
        public string SuggestedModeId { get; }
        /// <summary>Name key of that mode, e.g. <c>mode.helix_drop.name</c>.</summary>
        public string SuggestedModeNameKey { get; }

        public QuestCelebrationData(string suggestedModeId, string suggestedModeNameKey)
        {
            SuggestedModeId = suggestedModeId;
            SuggestedModeNameKey = suggestedModeNameKey;
        }
    }

    /// <summary>
    /// "Квест первого ролика" completion (spec §4.3): once all steps are done the shell shows a short celebration that
    /// suggests another release-1 mode and sells nothing; "Позже" is as large as the main button.
    /// </summary>
    public interface IQuestCelebrationViewModel : IObservableViewModel
    {
        /// <summary>Non-null when the quest was just completed and the celebration has not been shown yet.</summary>
        QuestCelebrationData Pending { get; }
        /// <summary>The celebration was shown and answered (never shown again).</summary>
        void MarkShown();
    }
}
