using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Domain.Financial.FollowedSymbols;

namespace FinancialCopilot.Application.FinancialData.FollowedSymbols;

public sealed record FollowedSymbolDto(
    string ExternalCompanyId,
    string Symbol,
    string CompanyName,
    string? CompanyNameEnglish,
    DateTimeOffset FollowedAtUtc,
    string? Source);

public sealed record GetMyFollowedSymbolsQuery(CurrentActor Actor);

public sealed record FollowSymbolCommand(
    CurrentActor Actor,
    string ExternalCompanyId,
    string? Source = null);

public sealed record UnfollowSymbolCommand(
    CurrentActor Actor,
    string ExternalCompanyId);

public sealed record ReplaceMyFollowedSymbolsCommand(
    CurrentActor Actor,
    IReadOnlyCollection<string> ExternalCompanyIds,
    string? Source = null);

public interface IGetMyFollowedSymbolsUseCase
{
    Task<IReadOnlyCollection<FollowedSymbolDto>> ExecuteAsync(
        GetMyFollowedSymbolsQuery query,
        CancellationToken cancellationToken);
}

public interface IFollowSymbolUseCase
{
    Task<FollowedSymbolDto> ExecuteAsync(
        FollowSymbolCommand command,
        CancellationToken cancellationToken);
}

public interface IUnfollowSymbolUseCase
{
    Task<IReadOnlyCollection<FollowedSymbolDto>> ExecuteAsync(
        UnfollowSymbolCommand command,
        CancellationToken cancellationToken);
}

public interface IReplaceMyFollowedSymbolsUseCase
{
    Task<IReadOnlyCollection<FollowedSymbolDto>> ExecuteAsync(
        ReplaceMyFollowedSymbolsCommand command,
        CancellationToken cancellationToken);
}

public interface IFollowedSymbolRepository
{
    Task<IReadOnlyCollection<FollowedSymbol>> GetAsync(
        FollowedSymbolActor actor,
        CancellationToken cancellationToken);

    Task<FollowedSymbol?> FindAsync(
        FollowedSymbolActor actor,
        string externalCompanyId,
        CancellationToken cancellationToken);

    Task SaveAsync(FollowedSymbol followedSymbol, CancellationToken cancellationToken);

    Task ReplaceAsync(
        FollowedSymbolActor actor,
        IReadOnlyCollection<FollowedSymbol> followedSymbols,
        CancellationToken cancellationToken);

    Task RemoveAsync(
        FollowedSymbolActor actor,
        string externalCompanyId,
        CancellationToken cancellationToken);
}

public interface IFollowedCompanyResolver
{
    Task<CanonicalFollowedCompany?> ResolveAsync(
        string externalCompanyId,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, CanonicalFollowedCompany>> ResolveManyAsync(
        IReadOnlyCollection<string> externalCompanyIds,
        CancellationToken cancellationToken);
}

public sealed class FollowedSymbolValidationException(string message) : InvalidOperationException(message);
