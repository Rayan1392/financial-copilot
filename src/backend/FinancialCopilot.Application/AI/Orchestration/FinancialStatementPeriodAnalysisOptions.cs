namespace FinancialCopilot.Application.AI.Orchestration;

/// <summary>
/// Feature 081 change «period-analysis-feature-flag»: switches the fixed financial-statement period summary on or off.
/// When it is off, a V2 turn that 081 would answer ends <c>Unsupported</c> with <see cref="DisabledReasonCode"/>, so the
/// Feature 163 analysis offer can take it. A missing section keeps 081 on.
/// </summary>
public sealed class FinancialStatementPeriodAnalysisOptions
{
    public const string SectionName = "FinancialStatementPeriodAnalysis";

    /// <summary>Not a provider-policy code: Feature 163 eligibility accepts it (163 FR-1).</summary>
    public const string DisabledReasonCode = "financial_statement_period_analysis_disabled";

    public bool Enabled { get; set; } = true;
}
