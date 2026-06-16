using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Domain.Financial.ValueObjects;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.CodalDb;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CodalDb;

/// <summary>
/// Normalizes the CodalDB <c>Symbols</c> payload (a JSON array of <see cref="CodalDbCompanyRecord"/>)
/// into the enriched company master-data model: company rows with English name, identifiers and
/// ISINs, industry/group/market dimension references. Symbols are no longer written (spec 068).
/// </summary>
public sealed class CodalDbSymbolNormalizer(
    FinancialIngestionDbContext dbContext) : IFinancialPayloadNormalizer
{
    // Spec 051: the persisted source name is the Noavaran Amin archive source (was "CodalDb").
    public const string CodalDbProviderName = ProviderSources.NoavaranArchiveSqlName;

    public string ProviderName => CodalDbProviderName;

    public ProviderDataset Dataset => ProviderDataset.Symbols;

    public async Task<NormalizationOutcome> NormalizeAsync(ProviderRawPayload payload, CancellationToken cancellationToken)
    {
        var records = JsonSerializer.Deserialize<CodalDbCompanyRecord[]>(payload.Payload, JsonOptions) ??
            throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                "CodalDb company payload is invalid.");

        // Last write wins for any duplicate CoID within the same payload.
        var distinctCompanies = records
            .Where(record => record is not null)
            .GroupBy(record => record.CoID)
            .Select(group => group.Last())
            .ToList();

        var companies = await dbContext.Companies
            .Where(c => c.ProviderName == ProviderName)
            .ToDictionaryAsync(c => c.ExternalCompanyId, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var industries = await dbContext.Industries
            .Where(r => r.ProviderName == ProviderName)
            .ToDictionaryAsync(r => r.ExternalId, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var groups = await dbContext.IndustryGroups
            .Where(r => r.ProviderName == ProviderName)
            .ToDictionaryAsync(r => r.ExternalId, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var markets = await dbContext.Markets
            .Where(r => r.ProviderName == ProviderName)
            .ToDictionaryAsync(r => r.ExternalId, StringComparer.OrdinalIgnoreCase, cancellationToken);

        foreach (var record in distinctCompanies)
        {
            var externalCompanyId = record.CoID.ToString(CultureInfo.InvariantCulture);

            var industryId = ResolveIndustry(record, industries, payload.ReceivedAt);
            var groupId = ResolveGroup(record, groups, payload.ReceivedAt);
            var marketId = ResolveMarket(record, markets, payload.ReceivedAt);

            if (!companies.TryGetValue(externalCompanyId, out var company))
            {
                company = new NormalizedCompanyRow
                {
                    Id = Guid.NewGuid(),
                    ProviderName = ProviderName,
                    ExternalCompanyId = externalCompanyId
                };
                dbContext.Companies.Add(company);
                companies[externalCompanyId] = company;
            }

            company.Name = Trim(record.CoName) ?? externalCompanyId;
            company.NameEnglish = Trim(record.CoNameEnglish);
            company.CompanySymbol = Trim(record.CompanySymbol);
            company.TseSymbol = Trim(record.CoTSESymbol);
            company.InstrumentCode = Trim(record.InstCode);
            company.CompanyIsin = Trim(record.TseCIsinCode);
            company.SymbolIsin = Trim(record.TseSIsinCode);
            company.InstrumentRefPlaceholder = Trim(record.InstrumentRef);
            company.IndustryId = industryId;
            company.GroupId = groupId;
            company.MarketId = marketId;
            company.SourceModifiedAt = record.ModifiedDateTime;
            company.LastSynchronizedAt = payload.ReceivedAt;

        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new NormalizationOutcome(distinctCompanies.Count);
    }

    private Guid? ResolveIndustry(
        CodalDbCompanyRecord record,
        IDictionary<string, NormalizedIndustryRow> cache,
        DateTimeOffset at)
    {
        if (record.IndustryID is not { } id)
        {
            return null;
        }

        var classification = new IndustryClassification(
            id.ToString(CultureInfo.InvariantCulture),
            Fallback(record.IndustryName, id));

        if (!cache.TryGetValue(classification.SourceId, out var row))
        {
            row = new NormalizedIndustryRow
            {
                Id = Guid.NewGuid(),
                ProviderName = ProviderName,
                ExternalId = classification.SourceId
            };
            dbContext.Industries.Add(row);
            cache[classification.SourceId] = row;
        }

        row.Name = classification.Name;
        row.LastSynchronizedAt = at;
        return row.Id;
    }

    private Guid? ResolveGroup(
        CodalDbCompanyRecord record,
        IDictionary<string, NormalizedIndustryGroupRow> cache,
        DateTimeOffset at)
    {
        if (record.GroupID is not { } id)
        {
            return null;
        }

        var classification = new GroupClassification(
            id.ToString(CultureInfo.InvariantCulture),
            Fallback(record.GroupName, id));

        if (!cache.TryGetValue(classification.SourceId, out var row))
        {
            row = new NormalizedIndustryGroupRow
            {
                Id = Guid.NewGuid(),
                ProviderName = ProviderName,
                ExternalId = classification.SourceId
            };
            dbContext.IndustryGroups.Add(row);
            cache[classification.SourceId] = row;
        }

        row.Name = classification.Name;
        row.LastSynchronizedAt = at;
        return row.Id;
    }

    private Guid? ResolveMarket(
        CodalDbCompanyRecord record,
        IDictionary<string, NormalizedMarketRow> cache,
        DateTimeOffset at)
    {
        if (record.MarketID is not { } id)
        {
            return null;
        }

        var classification = new MarketClassification(
            id.ToString(CultureInfo.InvariantCulture),
            Fallback(record.MarketName, id));

        if (!cache.TryGetValue(classification.SourceId, out var row))
        {
            row = new NormalizedMarketRow
            {
                Id = Guid.NewGuid(),
                ProviderName = ProviderName,
                ExternalId = classification.SourceId
            };
            dbContext.Markets.Add(row);
            cache[classification.SourceId] = row;
        }

        row.Name = classification.Name;
        row.LastSynchronizedAt = at;
        return row.Id;
    }

    private static string Fallback(string? name, int id) =>
        string.IsNullOrWhiteSpace(name) ? id.ToString(CultureInfo.InvariantCulture) : name.Trim();

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
