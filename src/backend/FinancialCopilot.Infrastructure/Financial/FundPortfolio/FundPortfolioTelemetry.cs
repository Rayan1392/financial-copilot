using FinancialCopilot.Application.FinancialData.FundPortfolio;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed class LoggingFundPortfolioIngestionTelemetry(ILogger<LoggingFundPortfolioIngestionTelemetry> logger) : IFundPortfolioIngestionTelemetrySink
{
    private static readonly Meter Meter = new("FinancialCopilot.FundPortfolio.Ingestion", "1.0");
    private readonly Counter<long> unclassifiedSheets = Meter.CreateCounter<long>("fund_portfolio.ingestion.unclassified_sheets");
    private readonly Counter<long> formulaErrors = Meter.CreateCounter<long>("fund_portfolio.ingestion.formula_errors");
    private readonly Counter<long> dateFailures = Meter.CreateCounter<long>("fund_portfolio.ingestion.date_failures");
    private readonly Counter<long> partialParses = Meter.CreateCounter<long>("fund_portfolio.ingestion.partial_parses");

    public void Record(FundPortfolioIngestionTelemetry telemetry)
    {
        unclassifiedSheets.Add(telemetry.UnclassifiedSheetCount);
        formulaErrors.Add(telemetry.FormulaErrorCount);
        dateFailures.Add(telemetry.DateFailureCount);
        partialParses.Add(telemetry.PartialParseCount);
        logger.LogInformation("Fund portfolio workbook ingestion completed. CorrelationId={CorrelationId} FundResolution={FundResolution} FileHashPrefix={FileHashPrefix} ParserVersion={ParserVersion} Sheets={SheetCount} UnclassifiedSheets={UnclassifiedSheetCount} Issues={IssueCount} Errors={ErrorCount} Status={Status} DurationMs={DurationMs}",
            telemetry.CorrelationId, telemetry.FundResolutionStatus, telemetry.FileSha256Prefix, telemetry.ParserProfileVersion,
            telemetry.SheetCount, telemetry.UnclassifiedSheetCount, telemetry.IssueCount, telemetry.ErrorCount, telemetry.FinalStatus,
            telemetry.Duration.TotalMilliseconds);
    }
}
