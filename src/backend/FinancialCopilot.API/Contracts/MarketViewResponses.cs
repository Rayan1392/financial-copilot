namespace FinancialCopilot.API.Contracts;

public sealed record UpdateWatchlistRequest(IReadOnlyCollection<string>? Symbols);

