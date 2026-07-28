using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

public sealed class NadpcoApiCompanyNormalizer(
    FinancialIngestionDbContext dbContext,
    ILogger<NadpcoApiCompanyNormalizer> logger) : IFinancialPayloadNormalizer
{
    // Spec 051: the persisted source name is the Noavaran Amin current API source (was "NadpcoApi").
    public const string NadpcoApiProviderName = ProviderSources.NoavaranCurrentApiName;

    public string ProviderName => NadpcoApiProviderName;

    public ProviderDataset Dataset => ProviderDataset.Symbols;

    public async Task<NormalizationOutcome> NormalizeAsync(ProviderRawPayload payload, CancellationToken cancellationToken)
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
            company.CompanyCode = Trim(record.CoCode);
            company.CompanySymbol = Trim(record.CoSymbol);
            company.CompanySymbolEnglish = Trim(record.CoSymbolEnglish);
            company.CompanySymbolPinglish = Trim(record.CoSymbolPinglish);
            company.TseSymbol = Trim(record.CoSymbol) ?? Trim(record.CoSymbolEnglish);
            company.InstrumentCode = Trim(record.TseCode);
            company.CompanyIsin = Trim(record.TseCIsinCode);
            company.SymbolIsin = Trim(record.TseSIsinCode);
            company.InstrumentRefPlaceholder = null;
            company.IndustryId = industryId;
            company.GroupId = groupId;
            company.MarketId = marketId;
            company.PrecedencyRight = record.PrecedencyRight;
            company.AcceptionDateJalali = Trim(record.AcceptionDate);
            company.AcceptionDateGregorian = Trim(record.AcceptionDateGre);
            company.EnlistedDateJalali = Trim(record.EnlistedDate);
            company.EnlistedDateGregorian = Trim(record.EnlistedDateGre);
            company.IpoDateJalali = Trim(record.IpoDate);
            company.IpoDateGregorian = Trim(record.IpoDateGre);
            company.FundTypeId = record.FundTypeID;
            company.FundTypeTitle = Trim(record.FundTypeTitle);
            company.NationalId = Trim(record.NationalID);
            company.InExchange = record.InExchange;
            company.EstablishmentDateJalali = Trim(record.EstablishmentDate);
            company.EstablishmentDateGregorian = Trim(record.EstablishmentDateGre);
            company.BusinessStartDateJalali = Trim(record.BusinessStartDate);
            company.BusinessStartDateGregorian = Trim(record.BusinessStartDateGre);
            company.RegistrationDateJalali = Trim(record.RegistrationDate);
            company.RegistrationDateGregorian = Trim(record.RegistrationDateGre);
            company.RegistrationNumber = Trim(record.RegistrationNumber);
            company.RegistrationProvince = Trim(record.RegistrationProvince);
            company.RegistrationCity = Trim(record.RegistrationCity);
            company.MarketBoard = Trim(record.MarketBoard);
            company.SourceModifiedAt = null;
            company.LastSynchronizedAt = payload.ReceivedAt;

            // Spec 068: Symbols table removed. The canonical symbol/linkage is stored on the company
            // row via TseSymbol / InstrumentCode fields already set above — no separate symbol row needed.
            LogMissingTseCodeFallback(record, externalCompanyId);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new NormalizationOutcome(distinctCompanies.Count);
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

    private void LogMissingTseCodeFallback(NadpcoApiCompanyRecord record, string externalCompanyId)
    {
        var tseCode = Trim(record.TseCode);
        var symbolIsin = Trim(record.TseSIsinCode);
        if (tseCode is not null || symbolIsin is null)
        {
            return;
        }

        logger.LogWarning(
            "NADPCO company {ExternalCompanyId}: TseCode was missing; using SymbolIsin {SymbolIsin} as a resolution fallback.",
            externalCompanyId,
            symbolIsin);
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
