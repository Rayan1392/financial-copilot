using FinancialCopilot.Application.FinancialData.CyclicalWavesV2;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;

namespace FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWavesV2;

/// <summary>
/// CyclicalWaves v2 provider settings (section <c>CyclicalWavesV2</c>). Credentials come from secrets or
/// environment variables and are required only while ingestion is enabled.
/// </summary>
public sealed class CyclicalWavesV2Options
{
    public const string SectionName = "CyclicalWavesV2";

    public string AuthBaseAddress { get; set; } = "https://auth.cycfx.com/api/";
    public string DataBaseAddress { get; set; } = "https://api.tahlilapp.com/api/";
    public string GaugeBaseUrl { get; set; } = "https://api.tahlilapp.com/api/";
    public string ClientId { get; set; } = "default";
    public string Identifier { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public bool IngestionEnabled { get; set; }
    public bool DailySyncEnabled { get; set; }
    /// <summary>Full runs. Default 02:30 and 15:30 UTC: 06:00 (before the market) and 19:00 Tehran, every day.</summary>
    public string DailySyncCronUtc { get; set; } = "30 2,15 * * *";
    /// <summary>Gauge details and equilibrium ranges during market hours (Saturday to Wednesday, Tehran time).</summary>
    public bool IntradaySyncEnabled { get; set; }
    public string IntradayWindowStartTehran { get; set; } = "09:00";
    public string IntradayWindowEndTehran { get; set; } = "12:30";
    public int IntradayIntervalMinutes { get; set; } = 30;
    /// <summary>A CodalMirror monthly report or financial statement post refreshes the symbol's gauges.</summary>
    public bool CodalGaugeRefreshEnabled { get; set; }
    /// <summary>Minutes until the second fetch after an announcement; 0 turns the second fetch off.</summary>
    public int CodalGaugeRecheckMinutes { get; set; } = 30;
    public int IncomeStatementLimit { get; set; } = 12;
    public int AnalysisPageSize { get; set; } = 100;
    public int AnalysisIncrementalOverlapDays { get; set; } = 2;
    public int RequestDelayMilliseconds { get; set; } = 300;
    public int TimeoutSeconds { get; set; } = 30;
    public int RetryCount { get; set; } = 2;
    public int RefreshSkewSeconds { get; set; } = 60;
    public int RequestedRunPollSeconds { get; set; } = 30;
    public int LeaseMinutes { get; set; } = 30;

    public bool IsValid(out string error)
    {
        error = Validate() ?? string.Empty;
        return error.Length == 0;
    }

    private string? Validate()
    {
        if (!IsHttps(AuthBaseAddress) || !IsHttps(DataBaseAddress) || !IsHttps(GaugeBaseUrl))
        {
            return "CyclicalWavesV2 base addresses must be absolute HTTPS URLs.";
        }

        if (string.IsNullOrWhiteSpace(ClientId))
        {
            return "CyclicalWavesV2:ClientId is required.";
        }

        if (!CyclicalWavesUtcCronSchedule.IsValid(DailySyncCronUtc))
        {
            return "CyclicalWavesV2:DailySyncCronUtc must be a five-field UTC cron expression.";
        }

        if (!CyclicalWavesV2IntradaySchedule.IsValid(IntradayWindowStartTehran, IntradayWindowEndTehran, IntradayIntervalMinutes))
        {
            return "CyclicalWavesV2 market-hours sync needs IntradayWindowStartTehran before IntradayWindowEndTehran (HH:mm) " +
                   $"and IntradayIntervalMinutes {CyclicalWavesV2IntradaySchedule.MinimumIntervalMinutes}-{CyclicalWavesV2IntradaySchedule.MaximumIntervalMinutes}.";
        }

        if (CodalGaugeRecheckMinutes is < 0 or > 1_440)
        {
            return "CyclicalWavesV2:CodalGaugeRecheckMinutes must be between 0 and 1440.";
        }

        if (IncomeStatementLimit is < 1 or > 40 ||
            AnalysisPageSize is < 1 or > 100 ||
            AnalysisIncrementalOverlapDays is < 0 or > 30 ||
            RequestDelayMilliseconds is < 0 or > 60_000 ||
            TimeoutSeconds is < 1 or > 300 ||
            RetryCount is < 0 or > 5 ||
            RefreshSkewSeconds is < 0 or > 600 ||
            RequestedRunPollSeconds is < 5 or > 3_600 ||
            LeaseMinutes is < 5 or > 720)
        {
            return "CyclicalWavesV2 limits are out of range (statement limit 1-40, page size 1-100, overlap 0-30 days, " +
                   "delay 0-60000 ms, timeout 1-300 s, retries 0-5, refresh skew 0-600 s, poll 5-3600 s, lease 5-720 min).";
        }

        if (IngestionEnabled && (string.IsNullOrWhiteSpace(Identifier) || string.IsNullOrWhiteSpace(Password)))
        {
            return "CyclicalWavesV2 credentials are required while ingestion is enabled.";
        }

        return null;
    }

    private static bool IsHttps(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps;
}
