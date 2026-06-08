using FinancialCopilot.Application.FinancialData.Providers;
using Microsoft.Extensions.Options;

namespace FinancialCopilot.Infrastructure.Financial.Providers;

/// <summary>
/// Configuration-driven <see cref="ISourcePriorityResolver"/>. Pure policy: it reads only
/// <see cref="SourcePriorityOptions"/> and never touches a physical source. Unknown dataset keys fall
/// back to the configured default order; the Shamsi boundary decides Noavaran archive-vs-current
/// ownership of a period.
/// </summary>
public sealed class SourcePriorityResolver(IOptions<SourcePriorityOptions> options) : ISourcePriorityResolver
{
    private readonly SourcePriorityOptions _options = options.Value;

    public int CurrentApiBoundaryShamsiYear => _options.CurrentApiBoundaryShamsiYear;

    public IReadOnlyList<string> ResolvePriority(ProviderDataset dataset)
    {
        if (_options.DatasetPriority.TryGetValue(dataset.ToString(), out var configured) &&
            configured.Count > 0)
        {
            return configured;
        }

        return _options.DefaultOrder;
    }

    public SourceMode ResolveNoavaranOwnership(ShamsiPeriod period) =>
        period.Year < _options.CurrentApiBoundaryShamsiYear
            ? SourceMode.ArchiveOneTime
            : SourceMode.CurrentIncremental;
}
