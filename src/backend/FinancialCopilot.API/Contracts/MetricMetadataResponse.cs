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
