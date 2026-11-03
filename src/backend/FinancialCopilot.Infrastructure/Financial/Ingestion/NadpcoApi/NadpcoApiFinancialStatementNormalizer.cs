using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.Periods;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

public sealed class NadpcoApiFinancialStatementNormalizer(
    FinancialIngestionDbContext dbContext) : IFinancialPayloadNormalizer
{
    public string ProviderName => NadpcoApiCompanyNormalizer.NadpcoApiProviderName;

    public ProviderDataset Dataset => ProviderDataset.FinancialStatements;

    public async Task<NormalizationOutcome> NormalizeAsync(ProviderRawPayload payload, CancellationToken cancellationToken)
    {
        var envelope = JsonSerializer.Deserialize<NadpcoFinancialStatementEnvelope>(payload.Payload, JsonOptions) ??
            throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                "NADPCO financial-statement envelope is invalid.");

        var typed = new List<NadpcoApiTypedStatement>();
        typed.AddRange(ReadStatements(envelope.IncomeStatement, FinancialStatementType.IncomeStatement));
        typed.AddRange(ReadStatements(envelope.BalanceSheet, FinancialStatementType.BalanceSheet));
        typed.AddRange(ReadStatements(envelope.CashFlow, FinancialStatementType.CashFlow));

        string? canonicalExternalCompanyId = null;

        foreach (var item in typed
                     .OrderBy(entry => entry.StatementType)
                     .ThenBy(entry => entry.Record.ComID)
                     .ThenBy(entry => entry.Record.PeriodEnd)
                     .ThenBy(entry => entry.Record.IsComposing)
                     .ThenBy(entry => entry.Record.IsRepresented)
                     .ThenBy(entry => entry.Record.IsAudited)
                     .ThenBy(entry => entry.Record.StatementID))
        {
            var statement = item.Record;
            var period = MapPeriod(statement);
            var externalStatementId = statement.StatementID.ToString(CultureInfo.InvariantCulture);
            var externalCompanyId = statement.ComID.ToString(CultureInfo.InvariantCulture);
            canonicalExternalCompanyId = externalCompanyId;
            var statementTypeText = item.StatementType.ToString();

            var row = await dbContext.FinancialStatements.SingleOrDefaultAsync(
                candidate => candidate.ProviderName == ProviderName &&
                    candidate.ExternalStatementId == externalStatementId &&
                    candidate.StatementType == statementTypeText &&
                    candidate.IsAudited == statement.IsAudited &&
                    candidate.IsRepresented == statement.IsRepresented &&
                    candidate.IsComposing == statement.IsComposing,
                cancellationToken);

            if (row is null)
            {
                row = new NormalizedFinancialStatementRow
                {
                    Id = Guid.NewGuid(),
                    ProviderName = ProviderName,
                    ExternalStatementId = externalStatementId,
                    StatementType = statementTypeText
                };
                dbContext.FinancialStatements.Add(row);
            }

            row.ExternalCompanyId = externalCompanyId;
            row.StatementType = statementTypeText;
            row.StatementTitle = statement.FullTitle;
            row.PeriodType = period.FiscalPeriodType.ToString();
            row.PeriodStart = period.PeriodStart;
            row.PeriodEnd = period.PeriodEnd;
            row.PublishedAt = statement.AnouncementDate.HasValue
                ? DateOnly.FromDateTime(statement.AnouncementDate.Value.Date)
                : null;
            row.SourcePayloadChecksum = payload.Checksum;
            row.LastSynchronizedAt = payload.ReceivedAt;
            row.IsAudited = statement.IsAudited;
            row.IsRepresented = statement.IsRepresented;
            row.IsComposing = statement.IsComposing;
            row.WarningsJson = BuildEvidenceJson(statement, item.StatementType);

            await UpsertLineItemsAsync(row, statement.Items, payload.ReceivedAt, cancellationToken);
            // Persist the statement and all of its line items in one SaveChanges transaction.
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return new NormalizationOutcome(typed.Count, canonicalExternalCompanyId);
    }

    private static IReadOnlyList<NadpcoApiTypedStatement> ReadStatements(
        string json,
        FinancialStatementType statementType)
    {
        try
        {
            var records = JsonSerializer.Deserialize<IReadOnlyList<NadpcoApiStatementRecord>>(json, JsonOptions) ??
                throw new JsonException("Payload was null.");
            return records
                .Where(record => record.StatementID > 0)
                .Select(record => new NadpcoApiTypedStatement(statementType, record))
                .ToArray();
        }
        catch (JsonException exception)
        {
            throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                $"NADPCO {statementType} payload is invalid.",
                exception);
        }
    }

    private async Task UpsertLineItemsAsync(
        NormalizedFinancialStatementRow statementRow,
        IReadOnlyList<NadpcoApiStatementLineItem> sourceItems,
        DateTimeOffset synchronizedAt,
        CancellationToken cancellationToken)
    {
        if (sourceItems.Count == 0)
        {
            return;
        }

        var statementType = statementRow.StatementType;
        var sourceItemIds = sourceItems.Select(source => source.ItemID).Distinct().ToArray();
        var existingCatalogRows = await dbContext.FinancialStatementSourceItems
            .Where(candidate => candidate.ProviderName == statementRow.ProviderName &&
                candidate.StatementType == statementType && sourceItemIds.Contains(candidate.SourceItemId))
            .ToListAsync(cancellationToken);
        var catalogRows = dbContext.FinancialStatementSourceItems.Local
            .Where(candidate => candidate.ProviderName == statementRow.ProviderName &&
                candidate.StatementType == statementType && sourceItemIds.Contains(candidate.SourceItemId))
            .Concat(existingCatalogRows)
            .DistinctBy(candidate => candidate.SourceItemId)
            .ToDictionary(candidate => candidate.SourceItemId);

        foreach (var source in sourceItems)
        {
            if (!catalogRows.TryGetValue(source.ItemID, out var catalogRow))
            {
                catalogRow = new FinancialStatementSourceItemCatalogRow
                {
                    Id = Guid.NewGuid(),
                    ProviderName = statementRow.ProviderName,
                    StatementType = statementType,
                    SourceItemId = source.ItemID
                };
                dbContext.FinancialStatementSourceItems.Add(catalogRow);
                catalogRows.Add(source.ItemID, catalogRow);
            }

            catalogRow.TitleFa = source.ItemTitle;
            catalogRow.Unit = source.AmountUnit;
            catalogRow.LastSynchronizedAt = synchronizedAt;
        }

        var catalogIds = catalogRows.Values.Select(row => row.Id).ToArray();
        var existingMappings = await dbContext.FinancialStatementSourceItemMetricMappings
            .Where(candidate => catalogIds.Contains(candidate.SourceItemCatalogId))
            .ToListAsync(cancellationToken);
        var mappings = dbContext.FinancialStatementSourceItemMetricMappings.Local
            .Where(candidate => catalogIds.Contains(candidate.SourceItemCatalogId))
            .Concat(existingMappings)
            .DistinctBy(candidate => candidate.SourceItemCatalogId)
            .ToDictionary(candidate => candidate.SourceItemCatalogId);
        var financialStatementType = Enum.Parse<FinancialStatementType>(statementType);

        foreach (var catalogRow in catalogRows.Values)
        {
            var knownMetricCode = NadpcoApiStatementItemMaps.TryGetMetricCode(
                financialStatementType, catalogRow.SourceItemId);
            if (knownMetricCode is null)
            {
                continue;
            }

            if (!mappings.TryGetValue(catalogRow.Id, out var mapping))
            {
                mapping = new FinancialStatementSourceItemMetricMappingRow
                {
                    Id = Guid.NewGuid(),
                    SourceItemCatalogId = catalogRow.Id
                };
                dbContext.FinancialStatementSourceItemMetricMappings.Add(mapping);
                mappings.Add(catalogRow.Id, mapping);
            }

            mapping.MetricCode = knownMetricCode;
        }

        var existingLineItems = await dbContext.FinancialStatementLineItems
            .Where(candidate => candidate.FinancialStatementId == statementRow.Id)
            .ToListAsync(cancellationToken);
        var lineItemsByCatalogId = dbContext.FinancialStatementLineItems.Local
            .Where(candidate => candidate.FinancialStatementId == statementRow.Id &&
                candidate.SourceItemCatalogId.HasValue)
            .Concat(existingLineItems.Where(candidate => candidate.SourceItemCatalogId.HasValue))
            .DistinctBy(candidate => candidate.SourceItemCatalogId!.Value)
            .ToDictionary(candidate => candidate.SourceItemCatalogId!.Value);
        var legacyLineItemsByMetricCode = dbContext.FinancialStatementLineItems.Local
            .Where(candidate => candidate.FinancialStatementId == statementRow.Id &&
                candidate.SourceItemCatalogId == null && !string.IsNullOrWhiteSpace(candidate.MetricCode))
            .Concat(existingLineItems.Where(candidate => candidate.SourceItemCatalogId == null &&
                !string.IsNullOrWhiteSpace(candidate.MetricCode)))
            .DistinctBy(candidate => candidate.MetricCode!, StringComparer.Ordinal)
            .ToDictionary(candidate => candidate.MetricCode!, StringComparer.Ordinal);

        foreach (var source in sourceItems)
        {
            var catalogRow = catalogRows[source.ItemID];
            mappings.TryGetValue(catalogRow.Id, out var mapping);
            var metricCode = mapping?.MetricCode;
            lineItemsByCatalogId.TryGetValue(catalogRow.Id, out var row);

            if (row is null && !string.IsNullOrWhiteSpace(metricCode))
            {
                legacyLineItemsByMetricCode.TryGetValue(metricCode, out row);
                if (row is not null)
                {
                    legacyLineItemsByMetricCode.Remove(metricCode);
                }
            }

            if (row is null)
            {
                row = new NormalizedFinancialStatementLineItemRow
                {
                    Id = Guid.NewGuid(),
                    FinancialStatementId = statementRow.Id,
                    SourceItemCatalogId = catalogRow.Id
                };
                dbContext.FinancialStatementLineItems.Add(row);
            }

            row.SourceItemCatalogId = catalogRow.Id;
            row.MetricCode = metricCode;
            row.Value = source.Amount;
            lineItemsByCatalogId[catalogRow.Id] = row;
        }
    }

    private static NadpcoApiMappedPeriod MapPeriod(NadpcoApiStatementRecord statement)
    {
        var fiscalYearEnd = DateOnly.FromDateTime(statement.FiscalYearEnd.Date);
        var periodEnd = DateOnly.FromDateTime(statement.PeriodEnd.Date);
        var periodStart = fiscalYearEnd.AddYears(-1).AddDays(1);
        var fiscalPeriodType = statement.PeriodType switch
        {
            3 => FiscalPeriodType.ThreeMonths,
            6 => FiscalPeriodType.SixMonths,
            9 => FiscalPeriodType.NineMonths,
            12 => FiscalPeriodType.TwelveMonths,
            _ => FiscalPeriodType.TwelveMonths
        };

        return new NadpcoApiMappedPeriod(fiscalPeriodType, periodStart, periodEnd);
    }

    private static string BuildEvidenceJson(
        NadpcoApiStatementRecord statement,
        FinancialStatementType statementType)
    {
        var evidence = new
        {
            Code = "NadpcoApiStatementSelection",
            StatementID = statement.StatementID,
            SourceStatementType = statementType.ToString(),
            statement.BourseSymbol,
            statement.FullTitle,
            statement.IsAudited,
            statement.IsRepresented,
            statement.IsComposing,
            statement.JalaliFiscalYearEnd,
            statement.JalaliPeriodEnd,
            statement.JalaliAnouncementDate,
            statement.AnouncementDate,
            AssumedScale = "MillionRials",
            SourceAmountUnit = statement.Items.Select(item => item.AmountUnit).FirstOrDefault(unit => !string.IsNullOrWhiteSpace(unit)) ?? "N/A",
            InvalidObservations = statement.Items
                .Where(item => item.Amount is null)
                .Select(item => new
                {
                    SourceItemId = item.ItemID,
                    item.ItemTitle,
                    Reason = "NullAmount"
                })
                .ToArray()
        };
        return JsonSerializer.Serialize(new[] { evidence }, JsonOptions);
    }

    private sealed record NadpcoApiMappedPeriod(
        FiscalPeriodType FiscalPeriodType,
        DateOnly PeriodStart,
        DateOnly PeriodEnd);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
