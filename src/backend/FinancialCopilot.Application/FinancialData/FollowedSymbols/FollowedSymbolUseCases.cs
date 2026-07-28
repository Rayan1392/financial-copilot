using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Domain.Financial.FollowedSymbols;
using static FinancialCopilot.Application.FinancialData.FollowedSymbols.FollowedSymbolUseCaseMapping;

namespace FinancialCopilot.Application.FinancialData.FollowedSymbols;

public sealed class GetMyFollowedSymbolsUseCase(
    IFollowedSymbolRepository repository) : IGetMyFollowedSymbolsUseCase
{
    public async Task<IReadOnlyCollection<FollowedSymbolDto>> ExecuteAsync(
        GetMyFollowedSymbolsQuery query,
        CancellationToken cancellationToken)
    {
        var followed = await repository.GetAsync(ToActor(query.Actor), cancellationToken);
        return followed.Select(ToDto).ToArray();
    }
}

public sealed class FollowSymbolUseCase(
    IFollowedSymbolRepository repository,
    IFollowedCompanyResolver companyResolver,
    TimeProvider timeProvider) : IFollowSymbolUseCase
{
    public async Task<FollowedSymbolDto> ExecuteAsync(
        FollowSymbolCommand command,
        CancellationToken cancellationToken)
    {
        var actor = ToActor(command.Actor);
        var externalCompanyId = NormalizeExternalCompanyId(command.ExternalCompanyId);
        var company = await companyResolver.ResolveAsync(externalCompanyId, cancellationToken)
            ?? throw new FollowedSymbolValidationException($"Unknown company external id '{externalCompanyId}'.");
        var now = timeProvider.GetUtcNow();
        var existing = await repository.FindAsync(actor, company.ExternalCompanyId, cancellationToken);
        if (existing is not null)
        {
            existing.RefreshCompanySnapshot(company, now);
            await repository.SaveAsync(existing, cancellationToken);
            return ToDto(existing);
        }

        var followed = FollowedSymbol.Follow(actor, company, now, command.Source);
        await repository.SaveAsync(followed, cancellationToken);
        return ToDto(followed);
    }
}

public sealed class UnfollowSymbolUseCase(
    IFollowedSymbolRepository repository) : IUnfollowSymbolUseCase
{
    public async Task<IReadOnlyCollection<FollowedSymbolDto>> ExecuteAsync(
        UnfollowSymbolCommand command,
        CancellationToken cancellationToken)
    {
        var actor = ToActor(command.Actor);
        await repository.RemoveAsync(actor, NormalizeExternalCompanyId(command.ExternalCompanyId), cancellationToken);
        var remaining = await repository.GetAsync(actor, cancellationToken);
        return remaining.Select(ToDto).ToArray();
    }
}

public sealed class ReplaceMyFollowedSymbolsUseCase(
    IFollowedSymbolRepository repository,
    IFollowedCompanyResolver companyResolver,
    TimeProvider timeProvider) : IReplaceMyFollowedSymbolsUseCase
{
    private const int MaxSymbolsPerReplace = 100;

    public async Task<IReadOnlyCollection<FollowedSymbolDto>> ExecuteAsync(
        ReplaceMyFollowedSymbolsCommand command,
        CancellationToken cancellationToken)
    {
        var actor = ToActor(command.Actor);
        var requestedIds = command.ExternalCompanyIds
            .Select(NormalizeExternalCompanyId)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (requestedIds.Length > MaxSymbolsPerReplace)
        {
            throw new FollowedSymbolValidationException($"At most {MaxSymbolsPerReplace} followed symbols can be replaced at once.");
        }

        var companies = await companyResolver.ResolveManyAsync(requestedIds, cancellationToken);
        var unknown = requestedIds.Where(id => !companies.ContainsKey(id)).ToArray();
        if (unknown.Length > 0)
        {
            throw new FollowedSymbolValidationException($"Unknown company external id(s): {string.Join(", ", unknown)}.");
        }

        var now = timeProvider.GetUtcNow();
        var existing = (await repository.GetAsync(actor, cancellationToken))
            .ToDictionary(item => item.ExternalCompanyId, StringComparer.Ordinal);
        var desired = new List<FollowedSymbol>(requestedIds.Length);
        foreach (var externalCompanyId in requestedIds)
        {
            var company = companies[externalCompanyId];
            if (existing.TryGetValue(externalCompanyId, out var followed))
            {
                followed.RefreshCompanySnapshot(company, now);
                desired.Add(followed);
                continue;
            }

            desired.Add(FollowedSymbol.Follow(actor, company, now, command.Source));
        }

        await repository.ReplaceAsync(actor, desired, cancellationToken);
        return desired
            .OrderByDescending(item => item.FollowedAtUtc)
            .ThenBy(item => item.Symbol, StringComparer.Ordinal)
            .Select(ToDto)
            .ToArray();
    }
}

internal static class FollowedSymbolUseCaseMapping
{
    public static FollowedSymbolActor ToActor(CurrentActor actor) =>
        new(actor.TenantId, actor.ActorId, actor.ActorType.ToString());

    public static FollowedSymbolDto ToDto(FollowedSymbol followedSymbol) =>
        new(
            followedSymbol.ExternalCompanyId,
            followedSymbol.Symbol,
            followedSymbol.CompanyName,
            followedSymbol.CompanyNameEnglish,
            followedSymbol.FollowedAtUtc,
            followedSymbol.Source);

    public static string NormalizeExternalCompanyId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FollowedSymbolValidationException("External company id is required.");
        }

        var normalized = value.Trim();
        if (normalized.Length > 64)
        {
            throw new FollowedSymbolValidationException("External company id must not exceed 64 characters.");
        }

        return normalized;
    }
}
