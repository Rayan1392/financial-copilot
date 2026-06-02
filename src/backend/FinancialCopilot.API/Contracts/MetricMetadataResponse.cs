namespace FinancialCopilot.API.Contracts;

public sealed record MetricMetadataResponse(
    IReadOnlyCollection<MetricDefinitionResponse> Metrics);

public sealed record MetricDefinitionResponse(
    string MetricCode,
    string MetricVersion,
    string DisplayName,
    string Description,
    string Category,
    string Unit,
    IReadOnlyCollection<string> SupportedPeriods,
    IReadOnlyCollection<MetricAliasResponse> Aliases,
    IReadOnlyCollection<string> CalculationPolicyVersions);

public sealed record MetricAliasResponse(string Expression, string Language);

public sealed record PeriodMetadataResponse(
    string Code,
    string DisplayName,
    string DisplayNameFa);

public sealed record SymbolMetadataResponse(
    string SymbolCode,
    string CompanyName,
    string? CompanyNameEnglish,
    string? IndustryName);

public sealed record IndustryMetadataResponse(
    string IndustryId,
    string DisplayName);
