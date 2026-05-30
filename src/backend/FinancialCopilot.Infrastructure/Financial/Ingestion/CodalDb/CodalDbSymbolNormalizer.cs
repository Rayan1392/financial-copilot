using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Domain.Financial.Services;
using FinancialCopilot.Domain.Financial.ValueObjects;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.CodalDb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CodalDb;

/// <summary>
/// Normalizes the CodalDB <c>Symbols</c> payload (a JSON array of <see cref="CodalDbCompanyRecord"/>)
/// into the enriched company master-data model: company rows with English name, identifiers and
/// ISINs, industry/group/market dimension references, and a canonical <c>SymbolCode</c> resolved
/// by <see cref="CanonicalSymbolLinkageResolver"/>. All CodalDB-specific mapping is confined here.
/// </summary>
public sealed class CodalDbSymbolNormalizer(
    FinancialIngestionDbContext dbContext,
    CanonicalSymbolLinkageResolver linkageResolver,
    ILogger<CodalDbSymbolNormalizer> logger) : IFinancialPayloadNormalizer
{
    public const string CodalDbProviderName = "CodalDb";

    public string ProviderName => CodalDbProviderName;

    public ProviderDataset Dataset => ProviderDataset.Symbols;

    public async Task<int> NormalizeAsync(ProviderRawPayload payload, CancellationToken cancellationToken)
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
        var symbols = await dbContext.Symbols
            .Where(s => s.ProviderName == ProviderName)
            .ToDictionaryAsync(s => s.ExternalSymbolId, StringComparer.OrdinalIgnoreCase, cancellationToken);
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

            var identifiers = new CompanyIdentifiers(
                companySymbol: record.CompanySymbol,
                tseSymbol: record.CoTSESymbol,
                instrumentCode: record.InstCode,
                companyIsin: record.TseCIsinCode,
                symbolIsin: record.TseSIsinCode);
            var resolution = linkageResolver.Resolve(identifiers);

            if (resolution.SymbolCode is null)
            {
                logger.LogWarning(
                    "CodalDb company {CompanyId} has no usable symbol identifier; symbol row skipped.",
                    externalCompanyId);
                continue;
            }

            if (resolution.Basis is not (SymbolLinkageBasis.SymbolIsin or SymbolLinkageBasis.CompanyIsin))
            {
                logger.LogWarning(
                    "CodalDb company {CompanyId} canonical symbol resolved by {Basis} (no ISIN); " +
                    "cross-provider alignment with CyclicalWaves may not hold.",
                    externalCompanyId,
                    resolution.Basis);
            }

            if (!symbols.TryGetValue(externalCompanyId, out var symbol))
            {
                symbol = new NormalizedSymbolRow
                {
                    Id = Guid.NewGuid(),
                    CompanyId = company.Id,
                    ProviderName = ProviderName,
                    ExternalSymbolId = externalCompanyId
                };
                dbContext.Symbols.Add(symbol);
                symbols[externalCompanyId] = symbol;
            }

            symbol.CompanyId = company.Id;
            symbol.SymbolCode = resolution.SymbolCode.Value;
            symbol.LinkageBasis = resolution.Basis.ToString();
            symbol.LastSynchronizedAt = payload.ReceivedAt;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return distinctCompanies.Count;
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
