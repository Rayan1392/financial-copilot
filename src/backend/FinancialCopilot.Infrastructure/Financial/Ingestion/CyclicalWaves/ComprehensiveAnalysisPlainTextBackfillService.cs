using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWaves;

internal sealed class ComprehensiveAnalysisPlainTextBackfillService(
    FinancialIngestionDbContext dbContext,
    IHtmlTextStripper htmlStripper) : IComprehensiveAnalysisPlainTextBackfillService
{
    private const int BatchSize = 500;

    public async Task<ComprehensiveAnalysisBackfillResult> ExecuteAsync(CancellationToken cancellationToken)
    {
        var totalUpdated = 0;

        while (true)
        {
            var batch = await dbContext.ComprehensiveAnalyses
                .Where(a => a.PlainTextSummary == null || a.PlainTextSummary == string.Empty)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);

            if (batch.Count == 0)
                break;

            foreach (var row in batch)
                row.PlainTextSummary = htmlStripper.Strip(row.Summary);

            await dbContext.SaveChangesAsync(cancellationToken);
            totalUpdated += batch.Count;

            if (batch.Count < BatchSize)
                break;
        }

        return new ComprehensiveAnalysisBackfillResult(totalUpdated);
    }
}
