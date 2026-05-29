using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Domain.Financial.DataQuality;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Providers.CyclicalWaves;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;

public sealed class CyclicalWavesFinancialStatementNormalizer(
    FinancialIngestionDbContext dbContext) : IFinancialPayloadNormalizer
{
    public string ProviderName => "CyclicalWaves";

    public ProviderDataset Dataset => ProviderDataset.FinancialStatements;

    public async Task<int> NormalizeAsync(ProviderRawPayload payload, CancellationToken cancellationToken)
    {
        var response = JsonSerializer.Deserialize<CyclicalWavesTickerDetailResponse>(payload.Payload, JsonOptions) ??
            throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                "CyclicalWaves ticker detail payload is invalid.");

        if (!response.Success)
        {
            throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                "CyclicalWaves ticker detail response indicates failure.");
        }

        var data = response.Data;
        await EnrichSymbolAsync(data, payload.ReceivedAt, cancellationToken);

        var asOf = payload.ReceivedAt;
        var staleWarnings = StaleDataWarnings();

        var periods = new[]
        {
            (
                StatementId: $"{data.Id}:Q0",
                Period: CyclicalWavesRelativePeriodResolver.ResolveQuarter(asOf, CyclicalWavesRelativePeriodResolver.QuarterOffset.Q0),
                Revenue: data.LastQuarterSale,
                NetProfit: data.LastQuarterNetProfit,
                GrossProfit: data.LastQuarterGrossProfit,
                OperatingProfit: data.LastQuarterOperatingProfit,
                NetProfitMargin: data.LastQuarterNetProfitMargin,
                GrossProfitMargin: data.LastQuarterGrossProfitMargin,
                OperatingProfitMargin: data.LastQuarterOperatingProfitMargin,
                IsQ0: true
            ),
            (
                StatementId: $"{data.Id}:Q1",
                Period: CyclicalWavesRelativePeriodResolver.ResolveQuarter(asOf, CyclicalWavesRelativePeriodResolver.QuarterOffset.Q1),
                Revenue: data.PenultimateQuarterSale,
                NetProfit: data.PenultimateQuarterNetProfit,
                GrossProfit: data.PenultimateQuarterGrossProfit,
                OperatingProfit: data.PenultimateQuarterOperatingProfit,
                NetProfitMargin: data.PenultimateQuarterNetProfitMargin,
                GrossProfitMargin: data.PenultimateQuarterGrossProfitMargin,
                OperatingProfitMargin: data.PenultimateQuarterOperatingProfitMargin,
                IsQ0: false
            ),
            (
                StatementId: $"{data.Id}:Q4",
                Period: CyclicalWavesRelativePeriodResolver.ResolveQuarter(asOf, CyclicalWavesRelativePeriodResolver.QuarterOffset.Q4),
                Revenue: data.LastYearSameQuarterSale,
                NetProfit: data.LastYearSameQuarterNetProfit,
                GrossProfit: data.LastYearSameQuarterGrossProfit,
                OperatingProfit: data.LastYearSameQuarterOperatingProfit,
                NetProfitMargin: data.LastYearSameQuarterNetProfitMargin,
                GrossProfitMargin: data.LastYearSameQuarterGrossProfitMargin,
                OperatingProfitMargin: data.LastYearSameQuarterOperatingProfitMargin,
                IsQ0: false
            )
        };

        foreach (var p in periods)
        {
            var statement = await dbContext.FinancialStatements.SingleOrDefaultAsync(
                row => row.ProviderName == ProviderName && row.ExternalStatementId == p.StatementId,
                cancellationToken);

            if (statement is null)
            {
                statement = new NormalizedFinancialStatementRow
                {
                    Id = Guid.NewGuid(),
                    ProviderName = ProviderName,
                    ExternalStatementId = p.StatementId
                };
                dbContext.FinancialStatements.Add(statement);
            }

            statement.ExternalCompanyId = data.Id;
            statement.PeriodType = "IncomeStatement";
            statement.PeriodStart = p.Period.Start;
            statement.PeriodEnd = p.Period.End;
            statement.SourcePayloadChecksum = payload.Checksum;
            statement.LastSynchronizedAt = payload.ReceivedAt;
            statement.WarningsJson = staleWarnings;

            await dbContext.SaveChangesAsync(cancellationToken);

            await UpsertLineItemAsync(statement.Id, "REVENUE", p.Revenue, cancellationToken);
            await UpsertLineItemAsync(statement.Id, "NET_PROFIT", p.NetProfit, cancellationToken);
            await UpsertLineItemAsync(statement.Id, "GROSS_PROFIT", p.GrossProfit, cancellationToken);
            await UpsertLineItemAsync(statement.Id, "OPERATING_PROFIT", p.OperatingProfit, cancellationToken);
            await UpsertLineItemAsync(statement.Id, "NET_PROFIT_MARGIN", p.NetProfitMargin, cancellationToken);
            await UpsertLineItemAsync(statement.Id, "GROSS_PROFIT_MARGIN", p.GrossProfitMargin, cancellationToken);
            await UpsertLineItemAsync(statement.Id, "OPERATING_PROFIT_MARGIN", p.OperatingProfitMargin, cancellationToken);

            if (p.IsQ0)
            {
                await UpsertLineItemAsync(statement.Id, "PE_RATIO", data.Pe, cancellationToken);
                await UpsertLineItemAsync(statement.Id, "PS_RATIO", data.Ps, cancellationToken);
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return periods.Length;
    }

    private async Task EnrichSymbolAsync(
        CyclicalWavesTickerData data,
        DateTimeOffset receivedAt,
        CancellationToken cancellationToken)
    {
        var symbol = await dbContext.Symbols.SingleOrDefaultAsync(
            row => row.ProviderName == ProviderName && row.ExternalSymbolId == data.Ticker,
            cancellationToken);

        if (symbol is null)
        {
            return;
        }

        symbol.SymbolCode = data.Enticker;
        symbol.LastSynchronizedAt = receivedAt;

        var company = await dbContext.Companies.SingleOrDefaultAsync(
            row => row.Id == symbol.CompanyId,
            cancellationToken);

        if (company is not null)
        {
            company.Name = data.Ticker;
            company.LastSynchronizedAt = receivedAt;
        }
    }

    private async Task UpsertLineItemAsync(
        Guid statementId,
        string metricCode,
        decimal? value,
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

        item.Value = value;
    }

    private static string StaleDataWarnings() =>
        JsonSerializer.Serialize(
            new[] { new { Code = nameof(FinancialDataWarningCode.StaleData), Message = "Fiscal period dates are estimated from the request timestamp using Iranian fiscal-year calendar approximations." } },
            JsonOptions);

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
}
