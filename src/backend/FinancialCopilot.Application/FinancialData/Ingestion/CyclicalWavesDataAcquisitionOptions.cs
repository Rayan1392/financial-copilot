namespace FinancialCopilot.Application.FinancialData.Ingestion;

public sealed class CyclicalWavesDataAcquisitionOptions
{
    public const string SectionName = "CyclicalWavesDataAcquisition";

    public bool Enabled { get; init; }

    public string Schedule { get; init; } = "0 2 * * *";

    public int RequestDelayMilliseconds { get; init; } = 1_000;

    public int TimeoutSeconds { get; init; } = 30;

    public int RetryCount { get; init; } = 2;
}
