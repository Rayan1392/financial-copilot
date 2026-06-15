using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Test stub for <see cref="ICompanyResolverService"/> that always returns null.
/// Used to satisfy constructor injection in tests that do not exercise company resolution.
/// </summary>
internal sealed class NullCompanyResolverService : ICompanyResolverService
{
    public static readonly NullCompanyResolverService Instance = new();

    public Task<ResolvedCompany?> ResolveBySymbolAsync(string symbol, CancellationToken ct = default) =>
        Task.FromResult<ResolvedCompany?>(null);
}
