namespace Ballverse.Core.Generation
{
    /// <summary>JSON shape of one tier entry in Remote Config <c>generation_budget_by_tier</c> (spec §9.3). Fields are optional.</summary>
    internal sealed class GenerationBudgetDto
    {
        public int? Candidates { get; set; }
        public long? TimeMs { get; set; }
    }
}
