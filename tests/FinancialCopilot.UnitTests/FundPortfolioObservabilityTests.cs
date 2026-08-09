using System.Diagnostics.Metrics;
using FinancialCopilot.Application.FinancialData.FundPortfolio;
using FinancialCopilot.Infrastructure.Financial.FundPortfolio;
using Microsoft.Extensions.Logging.Abstractions;

namespace FinancialCopilot.UnitTests;

public sealed class FundPortfolioObservabilityTests
{
    [Fact]
    public void AuditPolicy_CoversAllRequiredLifecycleOutcomes()
    {
        var correlationId = Guid.NewGuid();
        var reportId = Guid.NewGuid();
        var supersededReportId = Guid.NewGuid();

        var events = new[]
        {
            FundPortfolioAuditPolicy.Ingested(reportId, correlationId, 1, FinancialCopilot.Domain.Financial.FundPortfolio.FundPortfolioParseStatus.Parsed),
            FundPortfolioAuditPolicy.Duplicate(reportId, correlationId, 1),
            FundPortfolioAuditPolicy.CorrectedRevision(reportId, correlationId, 2, supersededReportId),
            FundPortfolioAuditPolicy.Failure(reportId, correlationId, "InvalidDataException"),
            FundPortfolioAuditPolicy.Superseded(reportId, correlationId, 2, supersededReportId)
        };

        Assert.Equal(
            new[]
            {
                FundPortfolioAuditEventTypes.Ingest,
                FundPortfolioAuditEventTypes.Duplicate,
                FundPortfolioAuditEventTypes.CorrectedRevision,
                FundPortfolioAuditEventTypes.Failure,
                FundPortfolioAuditEventTypes.Supersession
            },
            events.Select(item => item.EventType));
        Assert.All(events, item => Assert.Equal(correlationId.ToString("N"), item.CorrelationId));
    }

    [Fact]
    public void IngestionTelemetry_ExposesParserQualityMetrics()
    {
        var measurements = new Dictionary<string, long>(StringComparer.Ordinal);
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == "FinancialCopilot.FundPortfolio.Ingestion")
                    meterListener.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((instrument, measurement, _, _) => measurements[instrument.Name] = measurement);
        listener.Start();

        var sink = new LoggingFundPortfolioIngestionTelemetry(NullLogger<LoggingFundPortfolioIngestionTelemetry>.Instance);
        sink.Record(new(
            Guid.NewGuid(),
            FinancialCopilot.Domain.Financial.FundPortfolio.FundResolutionStatus.Resolved,
            "ABC123",
            "iran-fund-portfolio-workbook-v1",
            4,
            2,
            5,
            3,
            FinancialCopilot.Domain.Financial.FundPortfolio.FundPortfolioParseStatus.PartiallyParsed,
            TimeSpan.FromMilliseconds(12),
            FormulaErrorCount: 3,
            DateFailureCount: 1,
            PartialParseCount: 1));

        Assert.Equal(2, measurements["fund_portfolio.ingestion.unclassified_sheets"]);
        Assert.Equal(3, measurements["fund_portfolio.ingestion.formula_errors"]);
        Assert.Equal(1, measurements["fund_portfolio.ingestion.date_failures"]);
        Assert.Equal(1, measurements["fund_portfolio.ingestion.partial_parses"]);
    }
}
