using FinancialCopilot.Application.FinancialData.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;

public sealed class CyclicalWavesLegacyOptions
{
    public const string SectionName = "CyclicalWavesLegacy";

    /// <summary>Feature 148 retired every legacy endpoint. Only tests that exercise the old clients set this.</summary>
    public bool AllowCalls { get; set; }
}

/// <summary>
/// First handler on every legacy CyclicalWaves typed client (back1.cyclicalwaves.com). It fails the call
/// before any I/O, including the login that the legacy auth handler would otherwise send.
/// </summary>
public sealed class LegacyCyclicalWavesCallBlockingHandler(
    string clientName,
    IOptions<CyclicalWavesLegacyOptions> options,
    TimeProvider timeProvider,
    ILogger<LegacyCyclicalWavesCallBlockingHandler> logger) : DelegatingHandler
{
    private static readonly object LogGate = new();
    private static readonly Dictionary<string, DateTimeOffset> LastLoggedAt = new(StringComparer.Ordinal);

    public string ClientName { get; } = clientName;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (options.Value.AllowCalls)
        {
            return base.SendAsync(request, cancellationToken);
        }

        LogBlockedCall();
        throw new FinancialProviderException(
            FinancialProviderErrorCode.ProviderRetired,
            "The legacy CyclicalWaves integration was retired by Feature 148; no request was sent.");
    }

    // One line per client per minute: enough to notice a stray caller without flooding the logs.
    private void LogBlockedCall()
    {
        var now = timeProvider.GetUtcNow();
        lock (LogGate)
        {
            if (LastLoggedAt.TryGetValue(ClientName, out var last) && now - last < TimeSpan.FromMinutes(1))
            {
                return;
            }

            LastLoggedAt[ClientName] = now;
        }

        logger.LogWarning("Blocked a call to the retired legacy CyclicalWaves client {ClientName}.", ClientName);
    }
}
