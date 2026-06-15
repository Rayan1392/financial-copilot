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

        var selected = NadpcoApiStatementSelectionPolicy.SelectAll(typed);
        string? canonicalExternalCompanyId = null;

        foreach (var item in selected)
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
                    candidate.StatementType == statementTypeText,
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
            row.PeriodType = period.FiscalPeriodType.ToString();
            row.PeriodStart = period.PeriodStart;
            row.PeriodEnd = period.PeriodEnd;
            row.SourcePayloadChecksum = payload.Checksum;
            row.LastSynchronizedAt = payload.ReceivedAt;
            row.WarningsJson = BuildEvidenceJson(statement, item.StatementType);

            await dbContext.SaveChangesAsync(cancellationToken);
            await UpsertLineItemsAsync(row.Id, statement.Items, MapFor(item.StatementType), cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new NormalizationOutcome(selected.Count, canonicalExternalCompanyId);
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
        Guid statementId,
        IReadOnlyList<NadpcoApiStatementLineItem> sourceItems,
        IReadOnlyDictionary<int, string> itemMap,
        CancellationToken cancellationToken)
    {
        foreach (var source in sourceItems)
        {
            if (!itemMap.TryGetValue(source.ItemID, out var metricCode))
            {
                continue;
            }

            var row = await dbContext.FinancialStatementLineItems.SingleOrDefaultAsync(
                candidate => candidate.FinancialStatementId == statementId &&
                    candidate.MetricCode == metricCode,
                cancellationToken);

            if (row is null)
            {
                row = new NormalizedFinancialStatementLineItemRow
                {
                    Id = Guid.NewGuid(),
                    FinancialStatementId = statementId,
                    MetricCode = metricCode
                };
                dbContext.FinancialStatementLineItems.Add(row);
            }

            row.Value = source.Amount;
        }
    }

    private static IReadOnlyDictionary<int, string> MapFor(FinancialStatementType statementType) =>
        statementType switch
        {
            FinancialStatementType.IncomeStatement => NadpcoApiStatementItemMaps.IncomeItemIdToMetricCode,
            FinancialStatementType.BalanceSheet => NadpcoApiStatementItemMaps.BalanceSheetItemIdToMetricCode,
            FinancialStatementType.CashFlow => NadpcoApiStatementItemMaps.CashFlowItemIdToMetricCode,
            _ => throw new ArgumentOutOfRangeException(nameof(statementType), statementType, null)
        };

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

    private sealed record NadpcoApiMappedPeriod(
        FiscalPeriodType FiscalPeriodType,
        DateOnly PeriodStart,
        DateOnly PeriodEnd);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
