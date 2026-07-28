using FinancialCopilot.Application.Scanner;
using Microsoft.Extensions.Logging;

namespace FinancialCopilot.Infrastructure.AI.Observability;

public sealed class LoggingConfidenceScoringAuditSink(
    ILogger<LoggingConfidenceScoringAuditSink> logger) : IConfidenceScoringAuditSink
{
    public void Record(ConfidenceScoringAudit audit)
    {
        logger.LogInformation(
            "Calculated confidence score {Score} using policy {PolicyVersion}. CorrelationId={CorrelationId}, SourceType={SourceType}, SupportedCells={SupportedCells}, ExpectedCells={ExpectedCells}, Consistency={Consistency}, WarningPenalty={WarningPenalty}",
            audit.Result.Score,
            audit.Result.PolicyVersion,
            audit.CorrelationId,
            audit.SourceType,
            audit.SupportedCells,
            audit.ExpectedCells,
            audit.NarrativeConsistency,
            audit.WarningPenalty);
    }
}
