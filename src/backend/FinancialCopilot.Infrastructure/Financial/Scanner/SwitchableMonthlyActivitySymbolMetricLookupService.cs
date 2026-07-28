using FinancialCopilot.Application.Scanner;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Scanner;

public sealed class SwitchableMonthlyActivitySymbolMetricLookupService(
    ILegacySymbolMetricLookupService legacyLookupService,
    ISnapshotMonthlyActivitySymbolMetricLookupService snapshotLookupService,
    IOptions<MonthlyActivityLookupOptions> options)
    : ISymbolMetricLookupService
{
    public Task<SymbolLookupTableResult> LookupAsync(
        SymbolLookupRequest request,
        CancellationToken cancellationToken)
    {
        if (options.Value.DirectLookupSourceMode == MonthlyActivityDirectLookupSourceMode.TrendSnapshot &&
            SnapshotMonthlyActivitySymbolMetricLookupService.Supports(request))
        {
            return snapshotLookupService.LookupAsync(request, cancellationToken);
        }

        return legacyLookupService.LookupAsync(request, cancellationToken);
    }
}
