namespace FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;

internal sealed class CyclicalWavesAcquisitionRequestContext
{
    private int _attemptCount;

    public DateTimeOffset? FirstRequestedAtUtc { get; private set; }
    public DateTimeOffset? LastRequestedAtUtc { get; private set; }
    public short AttemptCount => checked((short)_attemptCount);

    public void RecordAttempt(DateTimeOffset requestedAtUtc)
    {
        FirstRequestedAtUtc ??= requestedAtUtc;
        LastRequestedAtUtc = requestedAtUtc;
        _attemptCount++;
    }
}

internal static class CyclicalWavesAcquisitionRequestOptions
{
    public static readonly HttpRequestOptionsKey<CyclicalWavesAcquisitionRequestContext> Context =
        new("CyclicalWavesDataAcquisition.AttemptContext");
}
