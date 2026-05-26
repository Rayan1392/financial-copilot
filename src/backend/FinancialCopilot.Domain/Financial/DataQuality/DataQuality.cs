namespace FinancialCopilot.Domain.Financial.DataQuality;

public enum FinancialDataWarningCode
{
    MissingData,
    StaleData
}

public sealed record FinancialDataWarning(FinancialDataWarningCode Code, string Message);

public sealed record FinancialObservationQuality(
    DateTimeOffset ObservedAt,
    DateTimeOffset LastSynchronizedAt,
    IReadOnlyCollection<FinancialDataWarning> Warnings)
{
    public bool HasMissingData => Warnings.Any(warning => warning.Code == FinancialDataWarningCode.MissingData);

    public bool IsStale => Warnings.Any(warning => warning.Code == FinancialDataWarningCode.StaleData);

    public static FinancialObservationQuality Current(
        DateTimeOffset observedAt,
        DateTimeOffset lastSynchronizedAt) =>
        new(observedAt, lastSynchronizedAt, []);
}
