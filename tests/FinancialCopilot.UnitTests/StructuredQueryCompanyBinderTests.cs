using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Semantics.Navigation;
using FinancialCopilot.Infrastructure.Financial.Semantics;

namespace FinancialCopilot.UnitTests;

public sealed class StructuredQueryCompanyBinderTests
{
    private static readonly Guid TenantId = Guid.Parse("5f655e78-07c5-4805-a757-a8d1927ab1d2");
    private static readonly Guid CompanyId = Guid.Parse("d6ea49d8-98e6-4ea4-868b-81c7b8e2c53a");

    [Fact]
    public async Task Resolved_binding_uses_internal_company_id_and_requires_tenant_scope()
    {
        var resolver = new FakeResolver(new EntityResolutionResult.Resolved(
            new CanonicalQueryEntity(CompanyId, "کگل", "گل گهر", "Company", "exact_ticker", "provider-external-id"),
            new EntityResolutionEvidence("exact_ticker", 1m)));
        var binder = new StructuredQueryCompanyBinder(resolver);

        var missingScope = await binder.ResolveAsync("کگل", Guid.Empty, CancellationToken.None);
        var binding = await binder.ResolveAsync("کگل", TenantId, CancellationToken.None);

        Assert.Equal(StructuredCompanyBindingStatus.TenantScopeRequired, missingScope.Status);
        Assert.Equal(StructuredCompanyBindingStatus.Found, binding.Status);
        Assert.Equal(CompanyId, binding.CompanyId);
        Assert.NotEqual(Guid.Parse("00000000-0000-0000-0000-000000000001"), binding.CompanyId);
        Assert.Equal("exact_ticker", binding.IdentityProvenance);
        Assert.Single(resolver.Mentions);
    }

    [Fact]
    public async Task Ambiguous_and_missing_results_remain_typed()
    {
        var first = new CanonicalQueryEntity(CompanyId, "نماد۱", "شرکت یک", "Company", "exact_ticker");
        var second = new CanonicalQueryEntity(Guid.NewGuid(), "نماد۲", "شرکت دو", "Company", "exact_ticker");
        var resolver = new FakeResolver(
            new EntityResolutionResult.Ambiguous([
                new EntityResolutionCandidate(first, 1m, "exact_ticker"),
                new EntityResolutionCandidate(second, 1m, "exact_ticker")]),
            new EntityResolutionResult.NotFound("unknown"));
        var binder = new StructuredQueryCompanyBinder(resolver);

        var ambiguous = await binder.ResolveAsync("شرکت", TenantId, CancellationToken.None);
        var notFound = await binder.ResolveAsync("unknown", TenantId, CancellationToken.None);

        Assert.Equal(StructuredCompanyBindingStatus.Ambiguous, ambiguous.Status);
        Assert.Null(ambiguous.CompanyId);
        Assert.Equal(2, ambiguous.CandidateCompanyIds.Count);
        Assert.Equal(StructuredCompanyBindingStatus.NotFound, notFound.Status);
    }

    private sealed class FakeResolver(params EntityResolutionResult[] results) : ICanonicalQueryEntityResolver
    {
        private int _index;
        public List<string?> Mentions { get; } = [];
        public Task<EntityResolutionResult> ResolveMentionAsync(string? mention, CancellationToken cancellationToken = default)
        {
            Mentions.Add(mention);
            var result = results[Math.Min(_index++, results.Length - 1)];
            return Task.FromResult(result);
        }
        public Task<EntityResolutionResult> ResolveExactTickerFromTextAsync(string? text, CancellationToken cancellationToken = default) => ResolveMentionAsync(text, cancellationToken);
        public Task<EntityResolutionResult> ResolveFromInterpretationAsync(QueryInterpretation interpretation, CancellationToken cancellationToken = default) => ResolveMentionAsync(null, cancellationToken);
        public Task<IReadOnlyList<EntityResolutionResult.Resolved>> ResolveAllFromInterpretationAsync(QueryInterpretation interpretation, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EntityResolutionResult.Resolved>>([]);
    }
}
