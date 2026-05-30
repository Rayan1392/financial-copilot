using System.Globalization;
using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
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
/// written: one for income line items and one for balance-sheet line items. They share the same
/// period window but have distinct <c>ExternalStatementId</c>s (<c>"{StmtId}:INC"</c> /
/// <c>"{StmtId}:BS"</c>) and distinct <c>PeriodType</c> values (the
/// <see cref="FinancialCopilot.Domain.Financial.Periods.FiscalPeriodType"/> enum name, which is
/// what <c>NetProfitMetricInputSource</c> and similar sources parse back via <c>Enum.Parse</c>).
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

    public async Task<int> NormalizeAsync(ProviderRawPayload payload, CancellationToken cancellationToken)
    {
        var rows = JsonSerializer.Deserialize<IReadOnlyList<CodalStatementRow>>(payload.Payload, JsonOptions)
            ?? throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                "CodalDb financial-statement payload is null or invalid.");

        var selected = CodalDbStatementSelectionPolicy.SelectAll(rows, _preferConsolidated);

        foreach (var stmt in selected)
        {
            var period = CodalDbFiscalPeriodMapper.Map(
                stmt.FiscalYearEnd, stmt.PeriodEnd, stmt.PeriodType,
                stmt.PeriodEndJalali, stmt.FiscalYearEndJalali);

            var warningsJson = BuildSelectionEvidence(stmt, period);
            var externalCompanyId = stmt.CompanyId.ToString(CultureInfo.InvariantCulture);

            await UpsertStatementRowAsync(
                payload, externalCompanyId, stmt, period, warningsJson,
                $"{stmt.StmtId}:INC", stmt.IncomeItems,
                CodalDbStatementItemMaps.IncomeItemIdToMetricCode,
                cancellationToken);

            await UpsertStatementRowAsync(
                payload, externalCompanyId, stmt, period, warningsJson,
                $"{stmt.StmtId}:BS", stmt.BalanceItems,
                CodalDbStatementItemMaps.BalanceItemIdToMetricCode,
                cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return selected.Count;
    }

    private async Task UpsertStatementRowAsync(
        ProviderRawPayload payload,
        string externalCompanyId,
        CodalStatementRow stmt,
        CodalDbMappedPeriod period,
        string warningsJson,
        string externalStatementId,
        IReadOnlyList<CodalStatementLineItem> sourceItems,
        IReadOnlyDictionary<int, string> itemMap,
        CancellationToken cancellationToken)
    {
        var statement = await dbContext.FinancialStatements.SingleOrDefaultAsync(
            row => row.ProviderName == ProviderName && row.ExternalStatementId == externalStatementId,
            cancellationToken);

        if (statement is null)
        {
            statement = new NormalizedFinancialStatementRow
            {
                Id = Guid.NewGuid(),
                ProviderName = ProviderName,
                ExternalStatementId = externalStatementId
            };
            dbContext.FinancialStatements.Add(statement);
        }

        // PeriodType stores the FiscalPeriodType enum name (e.g. "ThreeMonths") so that
        // metric input sources can round-trip it via Enum.Parse<FiscalPeriodType>.
        statement.ExternalCompanyId = externalCompanyId;
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
