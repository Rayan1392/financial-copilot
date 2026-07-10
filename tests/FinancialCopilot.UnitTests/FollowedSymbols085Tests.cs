using FinancialCopilot.Application.Authentication;
using FinancialCopilot.Application.FinancialData.FollowedSymbols;
using FinancialCopilot.Domain.Financial.FollowedSymbols;

namespace FinancialCopilot.UnitTests;

public sealed class FollowedSymbols085Tests
{
    private static readonly CurrentActor Actor = new(
        ActorType.User,
        Guid.Parse("11111111-1111-1111-1111-111111111111"),
        Guid.Parse("22222222-2222-2222-2222-222222222222"),
        AuthenticationMode.WebAppUser,
        UserId: Guid.Parse("11111111-1111-1111-1111-111111111111"));

    [Fact]
    public async Task Follow_IsIdempotent_ForSameActorAndCompany()
    {
        var repository = new InMemoryFollowedSymbolRepository();
        var resolver = new InMemoryCompanyResolver(
            new CanonicalFollowedCompany("100", "FOO", "Foo Company", "Foo"));
        var timeProvider = new FixedTimeProvider(DateTimeOffset.Parse("2026-07-10T08:00:00Z"));
        var useCase = new FollowSymbolUseCase(repository, resolver, timeProvider);

        var first = await useCase.ExecuteAsync(new FollowSymbolCommand(Actor, "100"), CancellationToken.None);
        var second = await useCase.ExecuteAsync(new FollowSymbolCommand(Actor, "100"), CancellationToken.None);
        var all = await repository.GetAsync(new FollowedSymbolActor(Actor.TenantId, Actor.ActorId, "User"), CancellationToken.None);

        Assert.Equal(first.FollowedAtUtc, second.FollowedAtUtc);
        Assert.Single(all);
    }

    [Fact]
    public async Task Replace_PreservesExistingFollowedAt_AndRemovesMissingSymbols()
    {
        var repository = new InMemoryFollowedSymbolRepository();
        var resolver = new InMemoryCompanyResolver(
            new CanonicalFollowedCompany("100", "FOO", "Foo Company", null),
            new CanonicalFollowedCompany("200", "BAR", "Bar Company", null));
        var timeProvider = new FixedTimeProvider(DateTimeOffset.Parse("2026-07-10T08:00:00Z"));
        var follow = new FollowSymbolUseCase(repository, resolver, timeProvider);
        await follow.ExecuteAsync(new FollowSymbolCommand(Actor, "100"), CancellationToken.None);
        var original = Assert.Single(await repository.GetAsync(
            new FollowedSymbolActor(Actor.TenantId, Actor.ActorId, "User"),
            CancellationToken.None));
        timeProvider.UtcNow = DateTimeOffset.Parse("2026-07-10T09:00:00Z");
        var replace = new ReplaceMyFollowedSymbolsUseCase(repository, resolver, timeProvider);

        var result = await replace.ExecuteAsync(
            new ReplaceMyFollowedSymbolsCommand(Actor, ["100", "200", "200"]),
            CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Equal(original.FollowedAtUtc, result.Single(item => item.ExternalCompanyId == "100").FollowedAtUtc);
        Assert.Contains(result, item => item.ExternalCompanyId == "200");
    }

    [Fact]
    public async Task Follow_RejectsUnknownCompany()
    {
        var useCase = new FollowSymbolUseCase(
            new InMemoryFollowedSymbolRepository(),
            new InMemoryCompanyResolver(),
            TimeProvider.System);

        await Assert.ThrowsAsync<FollowedSymbolValidationException>(() =>
            useCase.ExecuteAsync(new FollowSymbolCommand(Actor, "missing"), CancellationToken.None));
    }

    private sealed class InMemoryCompanyResolver(params CanonicalFollowedCompany[] companies) : IFollowedCompanyResolver
    {
        private readonly Dictionary<string, CanonicalFollowedCompany> _companies = companies
            .ToDictionary(company => company.ExternalCompanyId, StringComparer.Ordinal);

        public Task<CanonicalFollowedCompany?> ResolveAsync(
            string externalCompanyId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_companies.GetValueOrDefault(externalCompanyId));

        public Task<IReadOnlyDictionary<string, CanonicalFollowedCompany>> ResolveManyAsync(
            IReadOnlyCollection<string> externalCompanyIds,
            CancellationToken cancellationToken)
        {
            IReadOnlyDictionary<string, CanonicalFollowedCompany> result = externalCompanyIds
                .Where(_companies.ContainsKey)
                .ToDictionary(id => id, id => _companies[id], StringComparer.Ordinal);
            return Task.FromResult(result);
        }
    }

    private sealed class InMemoryFollowedSymbolRepository : IFollowedSymbolRepository
    {
        private readonly List<FollowedSymbol> _items = [];

        public Task<IReadOnlyCollection<FollowedSymbol>> GetAsync(
            FollowedSymbolActor actor,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyCollection<FollowedSymbol>>(_items
                .Where(item => item.Actor == actor)
                .OrderByDescending(item => item.FollowedAtUtc)
                .ThenBy(item => item.Symbol, StringComparer.Ordinal)
                .ToArray());

        public Task<FollowedSymbol?> FindAsync(
            FollowedSymbolActor actor,
            string externalCompanyId,
            CancellationToken cancellationToken) =>
            Task.FromResult(_items.FirstOrDefault(item =>
                item.Actor == actor &&
                item.ExternalCompanyId == externalCompanyId));

        public Task SaveAsync(FollowedSymbol followedSymbol, CancellationToken cancellationToken)
        {
            _items.RemoveAll(item =>
                item.Actor == followedSymbol.Actor &&
                item.ExternalCompanyId == followedSymbol.ExternalCompanyId);
            _items.Add(followedSymbol);
            return Task.CompletedTask;
        }

        public Task ReplaceAsync(
            FollowedSymbolActor actor,
            IReadOnlyCollection<FollowedSymbol> followedSymbols,
            CancellationToken cancellationToken)
        {
            _items.RemoveAll(item => item.Actor == actor);
            _items.AddRange(followedSymbols);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(
            FollowedSymbolActor actor,
            string externalCompanyId,
            CancellationToken cancellationToken)
        {
            _items.RemoveAll(item =>
                item.Actor == actor &&
                item.ExternalCompanyId == externalCompanyId);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;

        public override DateTimeOffset GetUtcNow() => UtcNow;
    }
}
