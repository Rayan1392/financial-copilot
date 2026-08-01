using FinancialCopilot.Application.FinancialData.FundPortfolio;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.Financial.FundPortfolio;

public sealed class LoggingFundPortfolioIngestionTelemetry(ILogger<LoggingFundPortfolioIngestionTelemetry> logger) : IFundPortfolioIngestionTelemetrySink
{
    public void Record(FundPortfolioIngestionTelemetry telemetry)
    {
        logger.LogInformation("Fund portfolio workbook ingestion completed. CorrelationId={CorrelationId} FundResolution={FundResolution} FileHashPrefix={FileHashPrefix} ParserVersion={ParserVersion} Sheets={SheetCount} UnclassifiedSheets={UnclassifiedSheetCount} Issues={IssueCount} Errors={ErrorCount} Status={Status} DurationMs={DurationMs}",
            telemetry.CorrelationId, telemetry.FundResolutionStatus, telemetry.FileSha256Prefix, telemetry.ParserProfileVersion,
            telemetry.SheetCount, telemetry.UnclassifiedSheetCount, telemetry.IssueCount, telemetry.ErrorCount, telemetry.FinalStatus,
            telemetry.Duration.TotalMilliseconds);
    }
}
