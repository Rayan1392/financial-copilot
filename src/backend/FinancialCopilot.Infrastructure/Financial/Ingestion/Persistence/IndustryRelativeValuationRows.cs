namespace FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;

public sealed class IndustryRelativeValuationCalculationRow
{
    public Guid Id { get; set; }
    public DateOnly CalculationDate { get; set; }
    public Guid IndustryId { get; set; }
    public string IndustryExternalId { get; set; } = string.Empty;
    public string IndustryTitleSnapshot { get; set; } = string.Empty;
    public int CalculationVersion { get; set; }
    public string Status { get; set; } = "Pending";
    public string AlgorithmVersion { get; set; } = string.Empty;
    public string MembershipHash { get; set; } = string.Empty;
    public string SourceBarrierHash { get; set; } = string.Empty;
    public string SourceBarrierEvidenceJson { get; set; } = string.Empty;
    public DateTimeOffset CalculatedAtUtc { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
    public bool IsLatestEvaluation { get; set; }
    public bool IsSelectedCurrent { get; set; }
}

public sealed class IndustryRelativeValuationMetricRow
{
    public Guid Id { get; set; }
    public Guid CalculationId { get; set; }
    public string MetricKind { get; set; } = string.Empty;
    public int ValidCount { get; set; }
    public int OutlierCount { get; set; }
    public int CleanCount { get; set; }
    public decimal? Quartile1 { get; set; }
    public decimal? Quartile3 { get; set; }
    public decimal? LowerBound { get; set; }
    public decimal? UpperBound { get; set; }
    public decimal? CleanAverage { get; set; }
    public string Readiness { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
}

public sealed class CompanyIndustryRelativeValuationRow
{
    public Guid Id { get; set; }
    public Guid CalculationId { get; set; }
    public Guid CompanyId { get; set; }
    public string PeSourceObservationId { get; set; } = string.Empty;
    public Guid? PeSourceFactId { get; set; }
    public string PeSourceVersion { get; set; } = string.Empty;
    public DateTimeOffset? PeSourceObservationTimestamp { get; set; }
    public DateTimeOffset? PePersistedAtUtc { get; set; }
    public string PsSourceObservationId { get; set; } = string.Empty;
    public Guid? PsSourceFactId { get; set; }
    public string PsSourceVersion { get; set; } = string.Empty;
    public DateTimeOffset? PsSourceObservationTimestamp { get; set; }
    public DateTimeOffset? PsPersistedAtUtc { get; set; }
    public string EquilibriumSourceObservationId { get; set; } = string.Empty;
    public Guid? EquilibriumSourceFactId { get; set; }
    public string EquilibriumSourceVersion { get; set; } = string.Empty;
    public DateTimeOffset? EquilibriumSourceObservationTimestamp { get; set; }
    public DateTimeOffset? EquilibriumPersistedAtUtc { get; set; }
    public string PeSourceWatermark { get; set; } = string.Empty;
    public string PsSourceWatermark { get; set; } = string.Empty;
    public string EquilibriumSourceWatermark { get; set; } = string.Empty;
    public decimal? CurrentPE { get; set; }
    public decimal? HistoricalAveragePE { get; set; }
    public decimal? CurrentPS { get; set; }
    public decimal? HistoricalAveragePS { get; set; }
    public decimal? CurrentMarketPrice { get; set; }
    public decimal? EquilibriumPrice { get; set; }
    public decimal? PEPercent { get; set; }
    public decimal? PSPercent { get; set; }
    public decimal? EquilibriumPercent { get; set; }
    public bool PEIsValid { get; set; }
    public bool PSIsValid { get; set; }
    public bool EquilibriumIsValid { get; set; }
    public bool PEIsOutlier { get; set; }
    public bool PSIsOutlier { get; set; }
    public bool EquilibriumIsOutlier { get; set; }
    public string PEClassification { get; set; } = string.Empty;
    public string PSClassification { get; set; } = string.Empty;
    public string EquilibriumClassification { get; set; } = string.Empty;
    public string PEReason { get; set; } = string.Empty;
    public string PSReason { get; set; } = string.Empty;
    public string EquilibriumReason { get; set; } = string.Empty;
    public int PositiveMetricCount { get; set; }
    public int ValidMetricCount { get; set; }
    public int? GlobalRank { get; set; }
    public string RankVersion { get; set; } = string.Empty;
}

public sealed class IndustryWatchStateRow
{
    public Guid Id { get; set; }
    public Guid IndustryId { get; set; }
    public string State { get; set; } = "NotWatching";
    public int EntryStreak { get; set; }
    public int ExitStreak { get; set; }
    public Guid? LastEvaluatedCalculationId { get; set; }
    public DateOnly? LastTransitionDate { get; set; }
    public string LastTransitionReason { get; set; } = string.Empty;
    public string AlgorithmVersion { get; set; } = string.Empty;
}

public sealed class IndustryWatchTransitionRow
{
    public Guid Id { get; set; }
    public Guid IndustryId { get; set; }
    public Guid CalculationId { get; set; }
    public string EvaluationKind { get; set; } = string.Empty;
    public string PreviousState { get; set; } = string.Empty;
    public string NextState { get; set; } = string.Empty;
    public string EvaluationOutcome { get; set; } = string.Empty;
    public int PreviousEntryStreak { get; set; }
    public int NewEntryStreak { get; set; }
    public int PreviousExitStreak { get; set; }
    public int NewExitStreak { get; set; }
    public DateOnly TransitionDate { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string AlgorithmVersion { get; set; } = string.Empty;
    public string EventIdentity { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
}

public sealed class IndustryWatchEvaluationRow
{
    public Guid Id { get; set; }
    public Guid IndustryId { get; set; }
    public Guid CalculationId { get; set; }
    public string EvaluationKind { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public DateTimeOffset EvaluatedAtUtc { get; set; }
    public DateOnly CalculationDate { get; set; }
    public string PreviousState { get; set; } = string.Empty;
    public string NewState { get; set; } = string.Empty;
    public int PreviousEntryStreak { get; set; }
    public int NewEntryStreak { get; set; }
    public int PreviousExitStreak { get; set; }
    public int NewExitStreak { get; set; }
    public string TransitionReason { get; set; } = string.Empty;
    public string AlgorithmVersion { get; set; } = string.Empty;
    public bool IsEffective { get; set; } = true;
}

public sealed class IndustryRelativeValuationOutboxRow
{
    public Guid Id { get; set; }
    public Guid CalculationId { get; set; }
    public Guid IndustryId { get; set; }
    public string EventIdentity { get; set; } = string.Empty;
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset CreatedAtUtc { get; set; }
    public DateTimeOffset? PublishedAtUtc { get; set; }
}
