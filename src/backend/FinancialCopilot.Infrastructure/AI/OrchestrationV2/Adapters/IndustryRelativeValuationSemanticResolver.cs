using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2.Adapters;

// Feature 125 adapter. Canonical identity and ambiguity decisions belong to Feature 119;
// this class only combines canonical outcomes with Feature 125 membership constraints.
public sealed class IndustryRelativeValuationSemanticAdapter(
    ICanonicalQueryEntityResolver companyResolver,
    ICanonicalQueryIndustryResolver industryResolver,
    FinancialIngestionDbContext db) : IIndustryRelativeValuationSemanticResolver
{
    public async Task<IndustryRelativeValuationResolution> ResolveAsync(
        string capabilityCode,
        QueryInterpretation interpretation,
        CancellationToken cancellationToken = default)
    {
        var mentions = interpretation.EntityMentions
            .Where(mention => !QueryNormalization.IsEntityDistractor(mention.Text))
            .OrderBy(mention => mention.Start)
            .ToArray();

        var companyResults = await Task.WhenAll(
            mentions.Select(mention => companyResolver.ResolveMentionAsync(mention.Text, cancellationToken)));
        var resolvedCompanies = companyResults
            .OfType<EntityResolutionResult.Resolved>()
            .GroupBy(result => result.Entity.CanonicalId)
            .Select(group => group.First())
            .ToArray();
        var ambiguousCompanies = companyResults.OfType<EntityResolutionResult.Ambiguous>().FirstOrDefault();
        var companyNotFound = companyResults.OfType<EntityResolutionResult.NotFound>().FirstOrDefault();

        var industryResults = await Task.WhenAll(
            mentions.Select(mention => industryResolver.ResolveIndustryMentionAsync(mention.Text, cancellationToken)));
        var explicitIndustry = industryResults.OfType<IndustryResolutionResult.Resolved>().FirstOrDefault();
        var ambiguousIndustry = industryResults.OfType<IndustryResolutionResult.Ambiguous>().FirstOrDefault();
        var industryNotFound = industryResults.OfType<IndustryResolutionResult.NotFound>().FirstOrDefault();

        if (ambiguousCompanies is not null)
            return AmbiguousCompanies(ambiguousCompanies);
        if (ambiguousIndustry is not null)
            return AmbiguousIndustries(ambiguousIndustry);

        var requiresPair = string.Equals(capabilityCode, "symbol_pair_within_industry", StringComparison.Ordinal);
        if (requiresPair && resolvedCompanies.Length < 2)
            return companyNotFound is not null
                ? new(IndustryRelativeValuationResolutionStatus.NotFound, Detail: "CompanyOrSymbol", Candidates: [companyNotFound.NormalizedMention])
                : new(IndustryRelativeValuationResolutionStatus.Missing, Detail: "CompanyOrSymbol");

        if (resolvedCompanies.Length == 0 && explicitIndustry is null)
        {
            if (industryNotFound is not null && IsIndustryRequired(capabilityCode))
                return new(IndustryRelativeValuationResolutionStatus.NotFound, Detail: "Industry", Candidates: [industryNotFound.NormalizedMention]);
            if (companyNotFound is not null && IsCompanyRequired(capabilityCode))
                return new(IndustryRelativeValuationResolutionStatus.NotFound, Detail: "CompanyOrSymbol", Candidates: [companyNotFound.NormalizedMention]);
            return new(IndustryRelativeValuationResolutionStatus.Missing, Detail: IsIndustryRequired(capabilityCode) ? "Industry" : "CompanyOrSymbol");
        }

        var companyIds = resolvedCompanies.Select(result => result.Entity.CanonicalId).Take(2).ToArray();
        var memberships = companyIds.Length == 0
            ? []
            : await db.Companies.AsNoTracking()
                .Where(row => companyIds.Contains(row.Id))
                .Select(row => new { row.Id, row.IndustryId })
                .ToArrayAsync(cancellationToken);
        var companyIndustryIds = memberships
            .Where(row => row.IndustryId.HasValue)
            .Select(row => row.IndustryId!.Value)
            .Distinct()
            .ToArray();
        var selectedIndustry = explicitIndustry?.Industry;

        if (companyIds.Length > 0 && memberships.Length != companyIds.Length)
            return new(IndustryRelativeValuationResolutionStatus.NotFound, CompanyIds: companyIds, Detail: "CompanyOrSymbol");
        if (companyIndustryIds.Length == 0)
            return selectedIndustry is not null
                ? new(IndustryRelativeValuationResolutionStatus.Resolved, selectedIndustry.CanonicalId, selectedIndustry.DisplayName, CompanyIds: [], Symbols: [])
                : new(IndustryRelativeValuationResolutionStatus.Missing, CompanyIds: companyIds, Detail: "Industry");
        if (companyIndustryIds.Length > 1)
            return new(
                IndustryRelativeValuationResolutionStatus.DifferentIndustries,
                CompanyIds: companyIds,
                Symbols: resolvedCompanies.Select(result => result.Entity.DisplaySymbol).Take(2).ToArray(),
                Detail: DialogueOutcomeReasonCodes.DifferentIndustries);

        if (selectedIndustry is not null && selectedIndustry.CanonicalId != companyIndustryIds[0])
            return new(
                IndustryRelativeValuationResolutionStatus.InvalidIndustryMembership,
                selectedIndustry.CanonicalId,
                selectedIndustry.DisplayName,
                companyIds,
                resolvedCompanies.Select(result => result.Entity.DisplaySymbol).ToArray(),
                DialogueOutcomeReasonCodes.InvalidIndustryMembership,
                CandidateIds: [selectedIndustry.CanonicalId]);

        var industryId = selectedIndustry?.CanonicalId ?? companyIndustryIds[0];
        return new(
            IndustryRelativeValuationResolutionStatus.Resolved,
            industryId,
            selectedIndustry?.DisplayName,
            companyIds,
            resolvedCompanies.Select(result => result.Entity.DisplaySymbol).Take(2).ToArray());
    }

    private static bool IsIndustryRequired(string capabilityCode) =>
        capabilityCode is "industry_relative_valuation_ranking" or "industry_relative_valuation_summary";

    private static bool IsCompanyRequired(string capabilityCode) =>
        capabilityCode is "symbol_vs_industry_relative_valuation" or "symbol_pair_within_industry";

    private static IndustryRelativeValuationResolution AmbiguousCompanies(EntityResolutionResult.Ambiguous result) =>
        new(
            IndustryRelativeValuationResolutionStatus.Ambiguous,
            CompanyIds: result.Candidates.Select(candidate => candidate.Entity.CanonicalId).ToArray(),
            Symbols: result.Candidates.Select(candidate => candidate.Entity.DisplaySymbol).ToArray(),
            Candidates: result.Candidates.Select(candidate => candidate.Entity.DisplaySymbol).ToArray(),
            CandidateIds: result.Candidates.Select(candidate => candidate.Entity.CanonicalId).ToArray(),
            Detail: DialogueOutcomeReasonCodes.EntityAmbiguous);

    private static IndustryRelativeValuationResolution AmbiguousIndustries(IndustryResolutionResult.Ambiguous result) =>
        new(
            IndustryRelativeValuationResolutionStatus.Ambiguous,
            Candidates: result.Candidates.Select(candidate => candidate.DisplayName).ToArray(),
            CandidateIds: result.Candidates.Select(candidate => candidate.CanonicalId).ToArray(),
            Detail: DialogueOutcomeReasonCodes.EntityAmbiguous);
}
