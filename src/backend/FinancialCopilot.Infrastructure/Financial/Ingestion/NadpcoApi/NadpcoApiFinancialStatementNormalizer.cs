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

            await dbContext.SaveChangesAsync(cancellationToken);
            await UpsertLineItemsAsync(row, statement.Items, payload.ReceivedAt, cancellationToken);
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
        foreach (var source in sourceItems)
        {
            var catalogRow = await UpsertSourceItemCatalogAsync(
                statementRow.ProviderName,
                statementRow.StatementType,
                source,
                synchronizedAt,
                cancellationToken);
            await EnsureKnownMappingAsync(catalogRow, cancellationToken);
            var metricCode = await ResolveMetricCodeAsync(catalogRow.Id, cancellationToken);

            var row = !string.IsNullOrWhiteSpace(metricCode)
                ? await dbContext.FinancialStatementLineItems.SingleOrDefaultAsync(
                    candidate => candidate.FinancialStatementId == statementRow.Id &&
                        candidate.MetricCode == metricCode,
                    cancellationToken)
                : await dbContext.FinancialStatementLineItems.SingleOrDefaultAsync(
                    candidate => candidate.FinancialStatementId == statementRow.Id &&
                        candidate.SourceItemCatalogId == catalogRow.Id,
                    cancellationToken);

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
            SourceAmountUnit = statement.Items.Select(item => item.AmountUnit).FirstOrDefault(unit => !string.IsNullOrWhiteSpace(unit)) ?? "N/A"
        };
        return JsonSerializer.Serialize(new[] { evidence }, JsonOptions);
    }

    private async Task<FinancialStatementSourceItemCatalogRow> UpsertSourceItemCatalogAsync(
        string providerName,
        string statementType,
        NadpcoApiStatementLineItem sourceItem,
        DateTimeOffset synchronizedAt,
        CancellationToken cancellationToken)
    {
        var row = dbContext.FinancialStatementSourceItems.Local.FirstOrDefault(
                      candidate => candidate.ProviderName == providerName &&
                          candidate.StatementType == statementType &&
                          candidate.SourceItemId == sourceItem.ItemID)
                  ?? await dbContext.FinancialStatementSourceItems.SingleOrDefaultAsync(
            candidate => candidate.ProviderName == providerName &&
                candidate.StatementType == statementType &&
                candidate.SourceItemId == sourceItem.ItemID,
            cancellationToken);

        if (row is null)
        {
            row = new FinancialStatementSourceItemCatalogRow
            {
                Id = Guid.NewGuid(),
                ProviderName = providerName,
                StatementType = statementType,
                SourceItemId = sourceItem.ItemID
            };
            dbContext.FinancialStatementSourceItems.Add(row);
        }

        row.TitleFa = sourceItem.ItemTitle;
        row.Unit = sourceItem.AmountUnit;
        row.LastSynchronizedAt = synchronizedAt;
        return row;
    }

    private async Task EnsureKnownMappingAsync(
        FinancialStatementSourceItemCatalogRow catalogRow,
        CancellationToken cancellationToken)
    {
        var statementType = Enum.Parse<FinancialStatementType>(catalogRow.StatementType);
        var metricCode = NadpcoApiStatementItemMaps.TryGetMetricCode(statementType, catalogRow.SourceItemId);
        if (metricCode is null)
        {
            return;
        }

        var existing = dbContext.FinancialStatementSourceItemMetricMappings.Local.FirstOrDefault(
                           candidate => candidate.SourceItemCatalogId == catalogRow.Id)
                       ?? await dbContext.FinancialStatementSourceItemMetricMappings.SingleOrDefaultAsync(
            candidate => candidate.SourceItemCatalogId == catalogRow.Id,
            cancellationToken);

        if (existing is null)
        {
            dbContext.FinancialStatementSourceItemMetricMappings.Add(
                new FinancialStatementSourceItemMetricMappingRow
                {
                    Id = Guid.NewGuid(),
                    SourceItemCatalogId = catalogRow.Id,
                    MetricCode = metricCode
                });
            return;
        }

        existing.MetricCode = metricCode;
    }

    private async Task<string?> ResolveMetricCodeAsync(Guid sourceItemCatalogId, CancellationToken cancellationToken) =>
        dbContext.FinancialStatementSourceItemMetricMappings.Local
            .Where(row => row.SourceItemCatalogId == sourceItemCatalogId)
            .Select(row => row.MetricCode)
            .SingleOrDefault()
        ?? await dbContext.FinancialStatementSourceItemMetricMappings
            .Where(row => row.SourceItemCatalogId == sourceItemCatalogId)
            .Select(row => row.MetricCode)
            .SingleOrDefaultAsync(cancellationToken);

    private sealed record NadpcoApiMappedPeriod(
        FiscalPeriodType FiscalPeriodType,
        DateOnly PeriodStart,
        DateOnly PeriodEnd);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
