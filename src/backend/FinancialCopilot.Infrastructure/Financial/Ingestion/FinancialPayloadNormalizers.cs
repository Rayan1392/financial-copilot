using System.Text.Json;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Domain.Financial.Periods;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion;

/// <summary>
/// Result returned by <see cref="IFinancialPayloadNormalizer.NormalizeAsync"/>.
/// </summary>
/// <param name="ProcessedRecords">Number of domain records written or updated.</param>
/// <param name="CanonicalExternalCompanyId">
/// The <c>ExternalCompanyId</c> actually stored in <c>FinancialStatements</c> /
/// <c>MonthlyReports</c> for this payload's company, or <c>null</c> when the normalizer
/// does not write company-scoped financial rows (e.g. Symbols-only normalizers).
/// Downstream callers use this to publish recalculation requests with an identifier that
/// is guaranteed to resolve without heuristic fallbacks.
/// </param>
public sealed record NormalizationOutcome(int ProcessedRecords, string? CanonicalExternalCompanyId = null);

public interface IFinancialPayloadNormalizer
{
    string ProviderName { get; }

    ProviderDataset Dataset { get; }

    Task<NormalizationOutcome> NormalizeAsync(ProviderRawPayload payload, CancellationToken cancellationToken);
}

public sealed class SymbolPayloadNormalizer(
    FinancialIngestionDbContext dbContext,
    string providerName = "ConfiguredFinancialProvider") : IFinancialPayloadNormalizer
{
    public string ProviderName => providerName;

    public ProviderDataset Dataset => ProviderDataset.Symbols;

    public async Task<NormalizationOutcome> NormalizeAsync(ProviderRawPayload payload, CancellationToken cancellationToken)
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

            // Spec 068: Symbols table removed. Store the symbol code directly on the company row.
            company.Name = record.Company;
            company.CompanySymbol = record.Symbol.Trim().ToUpperInvariant();
            company.LastSynchronizedAt = payload.ReceivedAt;
            count++;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new NormalizationOutcome(count);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record SymbolDocument(
        string ExternalSymbolId,
        string Symbol,
        string ExternalCompanyId,
        string Company);
}

public sealed class FinancialStatementPayloadNormalizer(
    FinancialIngestionDbContext dbContext,
    string providerName = "ConfiguredFinancialProvider") : IFinancialPayloadNormalizer
{
    public string ProviderName => providerName;

    public ProviderDataset Dataset => ProviderDataset.FinancialStatements;

    public async Task<NormalizationOutcome> NormalizeAsync(ProviderRawPayload payload, CancellationToken cancellationToken)
    {
        var document = JsonSerializer.Deserialize<StatementDocument>(payload.Payload, JsonOptions) ??
            throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                "Financial statement provider payload is invalid.");

        // Spec 029: validate the two enum-shaped string fields up-front so a bad provider
        // contract fails fast at ingestion rather than silently writing garbage that the metric
        // engine cannot parse.
        if (!Enum.TryParse<FiscalPeriodType>(document.Period, ignoreCase: false, out _))
        {
            throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                $"Unknown PeriodType value '{document.Period}'. " +
                $"Expected one of: {string.Join(", ", Enum.GetNames<FiscalPeriodType>())}.");
        }
        if (!Enum.TryParse<FinancialStatementType>(document.StatementType, ignoreCase: false, out _))
        {
            throw new FinancialProviderException(
                FinancialProviderErrorCode.InvalidResponse,
                $"Unknown StatementType value '{document.StatementType}'. " +
                $"Expected one of: {string.Join(", ", Enum.GetNames<FinancialStatementType>())}.");
        }

        var statement = await dbContext.FinancialStatements.SingleOrDefaultAsync(
            row => row.ProviderName == payload.ProviderName &&
                row.ExternalStatementId == document.StatementId &&
                row.StatementType == document.StatementType,
            cancellationToken);

        if (statement is null)
        {
            statement = new NormalizedFinancialStatementRow
            {
                Id = Guid.NewGuid(),
                ProviderName = payload.ProviderName,
                ExternalStatementId = document.StatementId,
                StatementType = document.StatementType
            };
            dbContext.FinancialStatements.Add(statement);
        }

        statement.ExternalCompanyId = document.CompanyId;
        statement.StatementType = document.StatementType;
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
        return new NormalizationOutcome(1, document.CompanyId);
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record StatementDocument(
        string StatementId,
        string CompanyId,
        decimal? NetProfit,
        string Period,
        string StatementType,
        DateOnly PeriodStart,
        DateOnly PeriodEnd);
}

public sealed class MonthlyReportPayloadNormalizer(
    FinancialIngestionDbContext dbContext,
    string providerName = "ConfiguredFinancialProvider") : IFinancialPayloadNormalizer
{
    public string ProviderName => providerName;

    public ProviderDataset Dataset => ProviderDataset.MonthlyProductionSales;

    public async Task<NormalizationOutcome> NormalizeAsync(ProviderRawPayload payload, CancellationToken cancellationToken)
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
        return new NormalizationOutcome(1, document.CompanyId);
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
