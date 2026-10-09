using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Semantics.Navigation;

namespace FinancialCopilot.Infrastructure.Financial.Semantics;

/// <summary>Reuses the application canonical resolver; never treats a provider external ID as Companies.Id.</summary>
public sealed class StructuredQueryCompanyBinder(ICanonicalQueryEntityResolver resolver) : IStructuredQueryCompanyBinder
{
    public async Task<StructuredCompanyBinding> ResolveAsync(string? mention, Guid tenantId,
        CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty)
            return new(StructuredCompanyBindingStatus.TenantScopeRequired, null, [], null, "tenant_binding_missing");

        var resolution = await resolver.ResolveMentionAsync(mention, cancellationToken);
        return resolution switch
        {
            EntityResolutionResult.Resolved resolved when resolved.Entity.EntityType == "Company"
                && resolved.Entity.CanonicalId != Guid.Empty => new(StructuredCompanyBindingStatus.Found,
                    resolved.Entity.CanonicalId, [resolved.Entity.CanonicalId], resolved.Entity.IdentityProvenance, null),
            EntityResolutionResult.Resolved => new(StructuredCompanyBindingStatus.NotFound, null, [], null,
                "resolved_entity_is_not_company"),
            EntityResolutionResult.Ambiguous ambiguous => new(StructuredCompanyBindingStatus.Ambiguous, null,
                ambiguous.Candidates.Select(candidate => candidate.Entity.CanonicalId).Where(id => id != Guid.Empty)
                    .Distinct().Order().ToArray(), "canonical-company-resolution", "company_identity_ambiguous"),
            EntityResolutionResult.NotFound => new(StructuredCompanyBindingStatus.NotFound, null, [], null,
                "company_identity_not_found"),
            _ => new(StructuredCompanyBindingStatus.Missing, null, [], null, "company_reference_missing")
        };
    }
}
