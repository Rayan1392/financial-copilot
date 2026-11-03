namespace FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;

public enum NoavaranMonthlyReportProviderType
{
    ProductSales,
    ServiceSales
}

public enum NoavaranMonthlyReportRouteOutcome
{
    ProductSales,
    ServiceSales,
    Missing,
    Unknown,
    Unsupported,
    Malformed
}

public sealed record NoavaranMonthlyReportRoute(
    int? ReportingType,
    NoavaranMonthlyReportRouteOutcome Outcome,
    NoavaranMonthlyReportProviderType? ProviderType,
    string Diagnostic);

public interface INoavaranMonthlyReportTypeResolver
{
    NoavaranMonthlyReportRoute Resolve(int? reportingType);

    NoavaranMonthlyReportRoute Resolve(string? reportingType);
}

/// <summary>
/// The single authoritative ReportingType-to-endpoint mapping for Noavaran monthly reports.
/// </summary>
public sealed class NoavaranMonthlyReportTypeResolver : INoavaranMonthlyReportTypeResolver
{
    private static readonly IReadOnlyDictionary<int, NoavaranMonthlyReportProviderType> Supported =
        new Dictionary<int, NoavaranMonthlyReportProviderType>
        {
            [1_000_000] = NoavaranMonthlyReportProviderType.ProductSales,
            [1_000_005] = NoavaranMonthlyReportProviderType.ServiceSales,
            [1_000_008] = NoavaranMonthlyReportProviderType.ProductSales
        };

    private static readonly IReadOnlySet<int> KnownUnsupported = new HashSet<int>
    {
        1_000_001,
        1_000_002,
        1_000_003,
        1_000_004,
        1_000_006,
        1_000_007,
        1_000_009
    };

    public NoavaranMonthlyReportRoute Resolve(int? reportingType)
    {
        if (reportingType is null)
        {
            return new(null, NoavaranMonthlyReportRouteOutcome.Missing, null, "ReportingType is null.");
        }

        if (Supported.TryGetValue(reportingType.Value, out var providerType))
        {
            return new(
                reportingType,
                providerType == NoavaranMonthlyReportProviderType.ProductSales
                    ? NoavaranMonthlyReportRouteOutcome.ProductSales
                    : NoavaranMonthlyReportRouteOutcome.ServiceSales,
                providerType,
                $"ReportingType {reportingType.Value} is supported by {providerType}.");
        }

        if (KnownUnsupported.Contains(reportingType.Value))
        {
            return new(
                reportingType,
                NoavaranMonthlyReportRouteOutcome.Unsupported,
                null,
                $"ReportingType {reportingType.Value} has no verified monthly endpoint mapping.");
        }

        return new(
            reportingType,
            NoavaranMonthlyReportRouteOutcome.Unknown,
            null,
            $"ReportingType {reportingType.Value} is unknown.");
    }

    public NoavaranMonthlyReportRoute Resolve(string? reportingType)
    {
        if (string.IsNullOrWhiteSpace(reportingType))
        {
            return Resolve((int?)null);
        }

        if (!int.TryParse(reportingType, out var parsed))
        {
            return new(
                null,
                NoavaranMonthlyReportRouteOutcome.Malformed,
                null,
                $"ReportingType '{reportingType}' is malformed.");
        }

        return Resolve(parsed);
    }
}
