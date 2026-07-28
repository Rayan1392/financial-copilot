using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.CodalDb;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CodalDb;

/// <summary>
/// Normalizes the CodalDB <c>FinancialStatements</c> payload (a JSON array of
/// <see cref="CodalStatementRow"/> for one company) into the canonical
/// <c>NormalizedFinancialStatementRow</c> / <c>NormalizedFinancialStatementLineItemRow</c> tables.
/// <para>
/// For each selected canonical statement variant (one per cumulative period window), two rows are
/// written: one with <c>StatementType = IncomeStatement</c> for income items and one with
/// <c>StatementType = BalanceSheet</c> for balance-sheet items. They share the same
/// <c>ExternalStatementId</c> (<c>{StmtId}</c>) and the same period window; the
/// <c>(ProviderName, ExternalStatementId, StatementType)</c> unique key disambiguates them
/// (spec 029).
/// </para>
/// <para>
/// <c>PeriodType</c> stores the
/// <see cref="FinancialCopilot.Domain.Financial.Periods.FiscalPeriodType"/> enum name (the period
/// duration — e.g. <c>ThreeMonths</c>) so input sources can round-trip it via <c>Enum.Parse</c>.
/// </para>
/// <para>
/// Amounts are assumed to be in million Iranian Rials (<c>Unit='N/A'</c> in source); this is
/// recorded once in <c>WarningsJson</c> as source evidence rather than per line-item row.
/// </para>
/// </summary>
public sealed class CodalDbFinancialStatementNormalizer(
    FinancialIngestionDbContext dbContext,
    IOptions<CodalDbProviderOptions> options) : IFinancialPayloadNormalizer
{
    private readonly bool _preferConsolidated = options.Value.PreferConsolidatedStatements;

    public string ProviderName => CodalDbSymbolNormalizer.CodalDbProviderName;

    public ProviderDataset Dataset => ProviderDataset.FinancialStatements;

    public async Task<NormalizationOutcome> NormalizeAsync(ProviderRawPayload payload, CancellationToken cancellationToken)
    {
        var rows = JsonSerializer.Deserialize<IReadOnlyList<CodalStatementRow>>(payload.Payload, JsonOptions)
            ?? throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                "CodalDb financial-statement payload is null or invalid.");

        var selected = CodalDbStatementSelectionPolicy.SelectAll(rows, _preferConsolidated);
        string? canonicalExternalCompanyId = null;

        foreach (var stmt in selected)
        {
            var period = CodalDbFiscalPeriodMapper.Map(
                stmt.FiscalYearEnd, stmt.PeriodEnd, stmt.PeriodType,
                stmt.PeriodEndJalali, stmt.FiscalYearEndJalali);

            var warningsJson = BuildSelectionEvidence(stmt, period);
            var externalCompanyId = stmt.CompanyId.ToString(CultureInfo.InvariantCulture);
            canonicalExternalCompanyId = externalCompanyId;

            // Spec 029: ExternalStatementId keeps the source StmtId verbatim on both rows; the
            // StatementType column disambiguates income vs. balance under the new unique key.
            var externalStatementId = stmt.StmtId.ToString(CultureInfo.InvariantCulture);

            await UpsertStatementRowAsync(
                payload, externalCompanyId, stmt, period, warningsJson,
                externalStatementId, FinancialStatementType.IncomeStatement,
                stmt.IncomeItems,
                CodalDbStatementItemMaps.IncomeItemIdToMetricCode,
                cancellationToken);

            await UpsertStatementRowAsync(
                payload, externalCompanyId, stmt, period, warningsJson,
                externalStatementId, FinancialStatementType.BalanceSheet,
                stmt.BalanceItems,
                CodalDbStatementItemMaps.BalanceItemIdToMetricCode,
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new NormalizationOutcome(selected.Count, canonicalExternalCompanyId);
    }

    private async Task UpsertStatementRowAsync(
        ProviderRawPayload payload,
        string externalCompanyId,
        CodalStatementRow stmt,
        CodalDbMappedPeriod period,
        string warningsJson,
        string externalStatementId,
        FinancialStatementType statementType,
        IReadOnlyList<CodalStatementLineItem> sourceItems,
        IReadOnlyDictionary<int, string> itemMap,
        CancellationToken cancellationToken)
    {
        var statementTypeText = statementType.ToString();
        var statement = await dbContext.FinancialStatements.SingleOrDefaultAsync(
            row => row.ProviderName == ProviderName &&
                row.ExternalStatementId == externalStatementId &&
                row.StatementType == statementTypeText,
            cancellationToken);

        if (statement is null)
        {
            statement = new NormalizedFinancialStatementRow
            {
                Id = Guid.NewGuid(),
                ProviderName = ProviderName,
                ExternalStatementId = externalStatementId,
                StatementType = statementTypeText
            };
            dbContext.FinancialStatements.Add(statement);
        }

        // PeriodType stores the FiscalPeriodType enum name (e.g. "ThreeMonths") so that
        // metric input sources can round-trip it via Enum.Parse<FiscalPeriodType>.
        statement.ExternalCompanyId = externalCompanyId;
        statement.StatementType = statementTypeText;
        statement.PeriodType = period.FiscalPeriodType.ToString();
        statement.PeriodStart = period.PeriodStart;
        statement.PeriodEnd = period.PeriodEnd;
        statement.SourcePayloadChecksum = payload.Checksum;
        statement.LastSynchronizedAt = payload.ReceivedAt;
        statement.WarningsJson = warningsJson;

        await dbContext.SaveChangesAsync(cancellationToken);

        foreach (var item in sourceItems)
        {
            if (!itemMap.TryGetValue(item.ItemId, out var metricCode))
            {
                continue; // unmapped item — ignored in Phase 1
            }

            await UpsertLineItemAsync(statement.Id, metricCode, item.Amount, cancellationToken);
        }
    }

    private async Task UpsertLineItemAsync(
        Guid statementId,
        string metricCode,
        decimal amount,
        CancellationToken cancellationToken)
    {
        var item = await dbContext.FinancialStatementLineItems.SingleOrDefaultAsync(
            row => row.FinancialStatementId == statementId && row.MetricCode == metricCode,
            cancellationToken);

        if (item is null)
        {
            item = new NormalizedFinancialStatementLineItemRow
            {
                Id = Guid.NewGuid(),
                FinancialStatementId = statementId,
                MetricCode = metricCode
            };
            dbContext.FinancialStatementLineItems.Add(item);
        }

        item.Value = amount;
    }

    private static string BuildSelectionEvidence(CodalStatementRow stmt, CodalDbMappedPeriod period)
    {
        var evidence = new
        {
            Code = "CodalStatementSelection",
            StmtId = stmt.StmtId,
            IsAudited = stmt.IsAudited,
            IsRepresented = stmt.IsRepresented,
            IsComposing = stmt.IsComposing,
            PeriodEndJalali = period.PeriodEndJalali,
            FiscalYearEndJalali = period.FiscalYearEndJalali,
            // Amounts have no per-row scale; CodalDB Unit='N/A'. Assumed million Rials.
            AssumedScale = "MillionRials"
        };
        return JsonSerializer.Serialize(new[] { evidence }, JsonOptions);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
