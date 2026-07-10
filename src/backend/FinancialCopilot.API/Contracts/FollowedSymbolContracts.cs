namespace FinancialCopilot.API.Contracts;

public sealed record FollowedSymbolsResponse(
    IReadOnlyCollection<FollowedSymbolResponse> Symbols);

public sealed record FollowedSymbolResponse(
    string ExternalCompanyId,
    string Symbol,
    string CompanyName,
    string? CompanyNameEnglish,
    DateTimeOffset FollowedAtUtc,
    string? Source);

public sealed record ReplaceFollowedSymbolsRequest(
    IReadOnlyCollection<string>? ExternalCompanyIds,
    string? Source);
