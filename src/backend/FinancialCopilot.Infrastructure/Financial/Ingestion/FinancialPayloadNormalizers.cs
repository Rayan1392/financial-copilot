using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

public interface IFinancialPayloadNormalizer
{
    string ProviderName { get; }

    ProviderDataset Dataset { get; }

    Task<int> NormalizeAsync(ProviderRawPayload payload, CancellationToken cancellationToken);
}

public sealed class SymbolPayloadNormalizer(
    FinancialIngestionDbContext dbContext) : IFinancialPayloadNormalizer
{
    public string ProviderName => "ConfiguredFinancialProvider";

    public ProviderDataset Dataset => ProviderDataset.Symbols;

    public async Task<int> NormalizeAsync(ProviderRawPayload payload, CancellationToken cancellationToken)
    {
        var records = JsonSerializer.Deserialize<SymbolDocument[]>(payload.Payload, JsonOptions) ??
            throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                "Symbol provider payload is invalid.");
        var count = 0;

        foreach (var record in records)
        {
            var company = await dbContext.Companies.SingleOrDefaultAsync(
                row => row.ProviderName == payload.ProviderName &&
                    row.ExternalCompanyId == record.ExternalCompanyId,
                cancellationToken);

            if (company is null)
            {
                company = new NormalizedCompanyRow
                {
                    Id = Guid.NewGuid(),
                    ProviderName = payload.ProviderName,
                    ExternalCompanyId = record.ExternalCompanyId
                };
                dbContext.Companies.Add(company);
            }

            company.Name = record.Company;
            company.LastSynchronizedAt = payload.ReceivedAt;

            var symbol = await dbContext.Symbols.SingleOrDefaultAsync(
                row => row.ProviderName == payload.ProviderName &&
                    row.ExternalSymbolId == record.ExternalSymbolId,
                cancellationToken);

            if (symbol is null)
            {
                symbol = new NormalizedSymbolRow
                {
                    Id = Guid.NewGuid(),
                    CompanyId = company.Id,
                    ProviderName = payload.ProviderName,
                    ExternalSymbolId = record.ExternalSymbolId
                };
                dbContext.Symbols.Add(symbol);
            }

            symbol.SymbolCode = record.Symbol.Trim().ToUpperInvariant();
            symbol.LastSynchronizedAt = payload.ReceivedAt;
            count++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return count;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record SymbolDocument(
        string ExternalSymbolId,
        string Symbol,
        string ExternalCompanyId,
        string Company);
}

public sealed class FinancialStatementPayloadNormalizer(
    FinancialIngestionDbContext dbContext) : IFinancialPayloadNormalizer
{
    public string ProviderName => "ConfiguredFinancialProvider";

    public ProviderDataset Dataset => ProviderDataset.FinancialStatements;

    public async Task<int> NormalizeAsync(ProviderRawPayload payload, CancellationToken cancellationToken)
    {
        var document = JsonSerializer.Deserialize<StatementDocument>(payload.Payload, JsonOptions) ??
            throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                "Financial statement provider payload is invalid.");
        var statement = await dbContext.FinancialStatements.SingleOrDefaultAsync(
            row => row.ProviderName == payload.ProviderName &&
                row.ExternalStatementId == document.StatementId,
            cancellationToken);

        if (statement is null)
        {
            statement = new NormalizedFinancialStatementRow
            {
                Id = Guid.NewGuid(),
                ProviderName = payload.ProviderName,
                ExternalStatementId = document.StatementId
            };
            dbContext.FinancialStatements.Add(statement);
        }

        statement.ExternalCompanyId = document.CompanyId;
        statement.PeriodType = document.Period;
        statement.PeriodStart = document.PeriodStart;
        statement.PeriodEnd = document.PeriodEnd;
        statement.SourcePayloadChecksum = payload.Checksum;
        statement.LastSynchronizedAt = payload.ReceivedAt;
        var item = await dbContext.FinancialStatementLineItems.SingleOrDefaultAsync(
            row => row.FinancialStatementId == statement.Id && row.MetricCode == "NET_PROFIT",
            cancellationToken);

        if (item is null)
        {
            item = new NormalizedFinancialStatementLineItemRow
            {
                Id = Guid.NewGuid(),
                FinancialStatementId = statement.Id,
                MetricCode = "NET_PROFIT"
            };
            dbContext.FinancialStatementLineItems.Add(item);
        }

        item.Value = document.NetProfit;
        await dbContext.SaveChangesAsync(cancellationToken);
        return 1;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record StatementDocument(
        string StatementId,
        string CompanyId,
        decimal? NetProfit,
        string Period,
        DateOnly PeriodStart,
        DateOnly PeriodEnd);
}

public sealed class MonthlyReportPayloadNormalizer(
    FinancialIngestionDbContext dbContext) : IFinancialPayloadNormalizer
{
    public string ProviderName => "ConfiguredFinancialProvider";

    public ProviderDataset Dataset => ProviderDataset.MonthlyProductionSales;

    public async Task<int> NormalizeAsync(ProviderRawPayload payload, CancellationToken cancellationToken)
    {
        var document = JsonSerializer.Deserialize<MonthlyDocument>(payload.Payload, JsonOptions) ??
            throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                "Monthly report provider payload is invalid.");
        var report = await dbContext.MonthlyReports.SingleOrDefaultAsync(
            row => row.ProviderName == payload.ProviderName && row.ExternalReportId == document.ReportId,
            cancellationToken);

        if (report is null)
        {
            report = new NormalizedMonthlyReportRow
            {
                Id = Guid.NewGuid(),
                ProviderName = payload.ProviderName,
                ExternalReportId = document.ReportId
            };
            dbContext.MonthlyReports.Add(report);
        }

        report.ExternalCompanyId = document.CompanyId;
        report.PeriodStart = document.PeriodStart;
        report.PeriodEnd = document.PeriodEnd;
        report.SourcePayloadChecksum = payload.Checksum;
        report.LastSynchronizedAt = payload.ReceivedAt;
        var item = await dbContext.MonthlyReportLineItems.SingleOrDefaultAsync(
            row => row.MonthlyReportId == report.Id && row.ProductCode == document.ProductCode,
            cancellationToken);

        if (item is null)
        {
            item = new NormalizedMonthlyReportLineItemRow
            {
                Id = Guid.NewGuid(),
                MonthlyReportId = report.Id,
                ProductCode = document.ProductCode
            };
            dbContext.MonthlyReportLineItems.Add(item);
        }

        item.ProductionQuantity = document.ProductionQuantity;
        item.SalesQuantity = document.SalesQuantity;
        item.SalesAmount = document.SalesAmount;
        await dbContext.SaveChangesAsync(cancellationToken);
        return 1;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record MonthlyDocument(
        string ReportId,
        string CompanyId,
        DateOnly PeriodStart,
        DateOnly PeriodEnd,
        string ProductCode,
        decimal? ProductionQuantity,
        decimal? SalesQuantity,
        decimal? SalesAmount);
}
