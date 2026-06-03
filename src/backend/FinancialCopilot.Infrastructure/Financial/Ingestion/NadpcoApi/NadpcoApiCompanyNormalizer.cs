using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Domain.Financial.Services;
using FinancialCopilot.Domain.Financial.ValueObjects;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

public sealed class NadpcoApiCompanyNormalizer(
    FinancialIngestionDbContext dbContext,
    CanonicalSymbolLinkageResolver linkageResolver,
    ILogger<NadpcoApiCompanyNormalizer> logger) : IFinancialPayloadNormalizer
{
    public const string NadpcoApiProviderName = "NadpcoApi";

    public string ProviderName => NadpcoApiProviderName;

    public ProviderDataset Dataset => ProviderDataset.Symbols;

    public async Task<int> NormalizeAsync(ProviderRawPayload payload, CancellationToken cancellationToken)
    {
        var records = JsonSerializer.Deserialize<NadpcoApiCompanyRecord[]>(payload.Payload, JsonOptions) ??
            throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                "NADPCO company catalog payload is invalid.");

        var distinctCompanies = records
            .Where(record => record.CoID > 0)
            .GroupBy(record => record.CoID)
            .Select(group =>
            {
                LogDuplicateIdentifierWarning(group);
                return group.Last();
            })
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

            company.Name = Trim(record.CoTitle) ?? Trim(record.CoSymbol) ?? externalCompanyId;
            company.NameEnglish = Trim(record.CoTitleEnglish);
            company.CompanySymbol = Trim(record.CoSymbol);
            company.TseSymbol = Trim(record.CoSymbolEnglish) ?? Trim(record.CoSymbol);
            company.InstrumentCode = Trim(record.TseCode);
            company.CompanyIsin = Trim(record.TseCIsinCode);
            company.SymbolIsin = Trim(record.TseSIsinCode);
            company.InstrumentRefPlaceholder = null;
            company.IndustryId = industryId;
            company.GroupId = groupId;
            company.MarketId = marketId;
            company.SourceModifiedAt = null;
            company.LastSynchronizedAt = payload.ReceivedAt;

            LogDeferredFields(record);

            var resolution = linkageResolver.Resolve(
                new CompanyIdentifiers(
                    companySymbol: record.CoSymbolEnglish ?? record.CoSymbol,
                    tseSymbol: record.CoSymbol,
                    instrumentCode: record.TseCode,
                    companyIsin: record.TseCIsinCode,
                    symbolIsin: record.TseSIsinCode),
                CanonicalSymbolLinkagePriority.InstrumentCodeFirst);

            if (resolution.SymbolCode is null)
            {
                logger.LogWarning(
                    "NADPCO company {CompanyId} has no usable instrument, ISIN, or symbol identifier; symbol row skipped.",
                    externalCompanyId);
                continue;
            }

            if (resolution.Basis is not SymbolLinkageBasis.InstrumentCode)
            {
                logger.LogWarning(
                    "NADPCO company {CompanyId} canonical symbol resolved by {Basis} because TseCode was missing.",
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

    private void LogDuplicateIdentifierWarning(IGrouping<int, NadpcoApiCompanyRecord> group)
    {
        if (group.Count() <= 1)
        {
            return;
        }

        var identifiers = group
            .Select(record => $"{Trim(record.TseCode)}|{Trim(record.TseSIsinCode)}|{Trim(record.CoSymbol)}")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (identifiers.Length > 1)
        {
            logger.LogWarning(
                "NADPCO company {CompanyId} appeared multiple times with conflicting identifiers; last row wins.",
                group.Key);
        }
    }

    private void LogDeferredFields(NadpcoApiCompanyRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.AcceptionDate) ||
            !string.IsNullOrWhiteSpace(record.EnlistedDate) ||
            !string.IsNullOrWhiteSpace(record.IpoDate) ||
            !string.IsNullOrWhiteSpace(record.NationalID) ||
            !string.IsNullOrWhiteSpace(record.RegistrationNumber) ||
            !string.IsNullOrWhiteSpace(record.MarketBoard) ||
            record.FundTypeID is not null ||
            record.PrecedencyRight is not null ||
            record.InExchange is not null)
        {
            logger.LogInformation(
                "NADPCO company {CompanyId} contains catalog attributes without normalized columns; retained in raw payload evidence.",
                record.CoID);
        }
    }

    private Guid? ResolveIndustry(
        NadpcoApiCompanyRecord record,
        IDictionary<string, NormalizedIndustryRow> cache,
        DateTimeOffset at)
    {
        if (record.IndustryID is not { } id)
        {
            return null;
        }

        var externalId = id.ToString(CultureInfo.InvariantCulture);
        if (!cache.TryGetValue(externalId, out var row))
        {
            row = new NormalizedIndustryRow
            {
                Id = Guid.NewGuid(),
                ProviderName = ProviderName,
                ExternalId = externalId
            };
            dbContext.Industries.Add(row);
            cache[externalId] = row;
        }

        row.Name = Fallback(record.IndustryTitle, id);
        row.LastSynchronizedAt = at;
        return row.Id;
    }

    private Guid? ResolveGroup(
        NadpcoApiCompanyRecord record,
        IDictionary<string, NormalizedIndustryGroupRow> cache,
        DateTimeOffset at)
    {
        if (record.FloorID is not { } id)
        {
            return null;
        }

        var externalId = id.ToString(CultureInfo.InvariantCulture);
        if (!cache.TryGetValue(externalId, out var row))
        {
            row = new NormalizedIndustryGroupRow
            {
                Id = Guid.NewGuid(),
                ProviderName = ProviderName,
                ExternalId = externalId
            };
            dbContext.IndustryGroups.Add(row);
            cache[externalId] = row;
        }

        row.Name = Fallback(record.FloorTitle, id);
        row.LastSynchronizedAt = at;
        return row.Id;
    }

    private Guid? ResolveMarket(
        NadpcoApiCompanyRecord record,
        IDictionary<string, NormalizedMarketRow> cache,
        DateTimeOffset at)
    {
        if (record.MarketID is not { } id)
        {
            return null;
        }

        var externalId = id.ToString(CultureInfo.InvariantCulture);
        if (!cache.TryGetValue(externalId, out var row))
        {
            row = new NormalizedMarketRow
            {
                Id = Guid.NewGuid(),
                ProviderName = ProviderName,
                ExternalId = externalId
            };
            dbContext.Markets.Add(row);
            cache[externalId] = row;
        }

        row.Name = Fallback(record.MarketTitle, id);
        row.LastSynchronizedAt = at;
        return row.Id;
    }

    private static string Fallback(string? name, int id) =>
        string.IsNullOrWhiteSpace(name) ? id.ToString(CultureInfo.InvariantCulture) : name.Trim();

    private static string? Trim(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
