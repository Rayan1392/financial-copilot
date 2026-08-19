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

        // The canonical resolver is scoped together with this adapter and uses the
        // same FinancialIngestionDbContext instance. EF Core does not permit
        // overlapping operations on that context, so resolve mentions sequentially.
        var companyResults = await ResolveCompaniesAsync(mentions, cancellationToken);
        var resolvedCompanies = companyResults
            .OfType<EntityResolutionResult.Resolved>()
            .GroupBy(result => result.Entity.CanonicalId)
            .Select(group => group.First())
            .ToArray();
        var ambiguousCompanies = companyResults.OfType<EntityResolutionResult.Ambiguous>().FirstOrDefault();
        var companyNotFound = companyResults.OfType<EntityResolutionResult.NotFound>().FirstOrDefault();

        var industryResults = await ResolveIndustriesAsync(mentions, cancellationToken);
        var explicitIndustry = industryResults.OfType<IndustryResolutionResult.Resolved>().FirstOrDefault();
        var ambiguousIndustry = industryResults.OfType<IndustryResolutionResult.Ambiguous>().FirstOrDefault();
        var industryNotFound = industryResults.OfType<IndustryResolutionResult.NotFound>().FirstOrDefault();

        if (ambiguousCompanies is not null)
            return AmbiguousCompanies(ambiguousCompanies);
        if (ambiguousIndustry is not null)
            return AmbiguousIndustries(ambiguousIndustry);

        var requiresPair = string.Equals(capabilityCode, "symbol_pair_within_industry", StringComparison.Ordinal);
        if (requiresPair && resolvedCompanies.Length != 2)
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

        var companyIds = resolvedCompanies.Select(result => result.Entity.CanonicalId).ToArray();
        var memberships = companyIds.Length == 0
            ? []
            : await (
                    from eligible in db.NoavaranEligibleCompanies.AsNoTracking()
                    join industryGroup in db.IndustryGroups.AsNoTracking()
                        on new { Id = eligible.GroupId!.Value, eligible.ProviderName }
                        equals new { industryGroup.Id, industryGroup.ProviderName }
                    where companyIds.Contains(eligible.Id) && eligible.GroupId != null
                    select new
                    {
                        eligible.Id,
                        eligible.IndustryId,
                        GroupId = industryGroup.Id,
                        GroupTitle = industryGroup.Name,
                        GroupExternalId = industryGroup.ExternalId
                    })
                .ToArrayAsync(cancellationToken);
        var companyIndustryIds = memberships
            .Where(row => row.IndustryId.HasValue)
            .Select(row => row.IndustryId!.Value)
            .Distinct()
            .ToArray();
        var selectedIndustry = explicitIndustry?.Industry;

        if (companyIds.Length > 0 && memberships.Select(row => row.Id).Distinct().Count() != companyIds.Length)
            return new(IndustryRelativeValuationResolutionStatus.NotFound, CompanyIds: companyIds, Detail: "CompanyOrSymbol");
        var companyGroups = memberships
            .GroupBy(row => row.GroupId)
            .Select(group => group.First())
            .ToArray();
        if (companyGroups.Length > 1)
            return new(
                IndustryRelativeValuationResolutionStatus.DifferentIndustries,
                CompanyIds: companyIds,
                Symbols: resolvedCompanies.Select(result => result.Entity.DisplaySymbol).ToArray(),
                Detail: DialogueOutcomeReasonCodes.DifferentIndustries);

        if (companyIds.Length > 0 && selectedIndustry is not null &&
            (companyIndustryIds.Length != 1 || selectedIndustry.CanonicalId != companyIndustryIds[0]))
            return new(
                IndustryRelativeValuationResolutionStatus.InvalidIndustryMembership,
                IndustryId: selectedIndustry.CanonicalId,
                IndustryName: selectedIndustry.DisplayName,
                CompanyIds: companyIds,
                Symbols: resolvedCompanies.Select(result => result.Entity.DisplaySymbol).ToArray(),
                Detail: DialogueOutcomeReasonCodes.InvalidIndustryMembership,
                CandidateIds: [selectedIndustry.CanonicalId]);

        if (companyGroups.Length == 0 && selectedIndustry is not null)
        {
            var eligibleGroups = await (
                    from eligible in db.NoavaranEligibleCompanies.AsNoTracking()
                    join industryGroup in db.IndustryGroups.AsNoTracking()
                        on new { Id = eligible.GroupId!.Value, eligible.ProviderName }
                        equals new { industryGroup.Id, industryGroup.ProviderName }
                    where eligible.IndustryId == selectedIndustry.CanonicalId && eligible.GroupId != null
                    select new { GroupId = industryGroup.Id, GroupTitle = industryGroup.Name })
                .Distinct()
                .ToArrayAsync(cancellationToken);
            if (eligibleGroups.Length == 1)
                return new(
                    IndustryRelativeValuationResolutionStatus.Resolved,
                    eligibleGroups[0].GroupId,
                    eligibleGroups[0].GroupTitle,
                    selectedIndustry.CanonicalId,
                    selectedIndustry.DisplayName,
                    CompanyIds: [],
                    Symbols: []);
            if (eligibleGroups.Length > 1)
                return new(
                    IndustryRelativeValuationResolutionStatus.Ambiguous,
                    IndustryId: selectedIndustry.CanonicalId,
                    IndustryName: selectedIndustry.DisplayName,
                    Detail: DialogueOutcomeReasonCodes.EntityAmbiguous,
                    Candidates: eligibleGroups.Select(group => group.GroupTitle).ToArray(),
                    CandidateIds: eligibleGroups.Select(group => group.GroupId).ToArray());
            return new(
                IndustryRelativeValuationResolutionStatus.NotFound,
                IndustryId: selectedIndustry.CanonicalId,
                IndustryName: selectedIndustry.DisplayName,
                Detail: "IndustryGroup");
        }

        if (companyGroups.Length == 0)
            return new(IndustryRelativeValuationResolutionStatus.Missing, CompanyIds: companyIds, Detail: "IndustryGroup");

        var companyGroup = companyGroups[0];
        return new(
            IndustryRelativeValuationResolutionStatus.Resolved,
            companyGroup.GroupId,
            companyGroup.GroupTitle,
            selectedIndustry?.CanonicalId ?? (companyIndustryIds.Length == 1 ? companyIndustryIds[0] : null),
            selectedIndustry?.DisplayName,
            companyIds,
            resolvedCompanies.Select(result => result.Entity.DisplaySymbol).ToArray());
    }

    private static bool IsIndustryRequired(string capabilityCode) =>
        capabilityCode is "industry_relative_valuation_ranking" or "industry_relative_valuation_summary";

    private async Task<EntityResolutionResult[]> ResolveCompaniesAsync(
        IReadOnlyList<EntityMention> mentions,
        CancellationToken cancellationToken)
    {
        var results = new List<EntityResolutionResult>(mentions.Count);
        foreach (var mention in mentions)
            results.Add(await companyResolver.ResolveMentionAsync(mention.Text, cancellationToken));
        return results.ToArray();
    }

    private async Task<IndustryResolutionResult[]> ResolveIndustriesAsync(
        IReadOnlyList<EntityMention> mentions,
        CancellationToken cancellationToken)
    {
        var results = new List<IndustryResolutionResult>(mentions.Count);
        foreach (var mention in mentions)
            results.Add(await industryResolver.ResolveIndustryMentionAsync(mention.Text, cancellationToken));
        return results.ToArray();
    }

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
