using FinancialCopilot.Application.FinancialData.Providers;

namespace FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;

/// <summary>
/// Default intent resolver for NADPCO monthly activity output types (spec 059 Story C).
/// Rules:
/// <list type="bullet">
///   <item>Explicit month in query → SingleMonth (outputTypeId=0)</item>
///   <item>YTD hint in query text (e.g. "از ابتدای سال") → YearToDate (outputTypeId=1)</item>
///   <item>Otherwise → SingleMonth (outputTypeId=0), the most intuitive answer for "آخرین فروش"</item>
/// </list>
/// </summary>
public sealed class DefaultMonthlyActivityOutputTypeResolver : IMonthlyActivityOutputTypeResolver
{
    private static readonly string[] YtdHints =
    [
        "از ابتدای سال",
        "از اول سال",
        "ytd",
        "year to date",
        "انباشته",
        "تجمعی",
        "cumulative"
    ];

    public MonthlyActivityQueryIntent Resolve(string? userQueryHint, bool hasExplicitMonth)
    {
        if (hasExplicitMonth)
            return MonthlyActivityQueryIntent.SingleMonth;

        if (userQueryHint is not null)
        {
            foreach (var hint in YtdHints)
            {
                if (userQueryHint.Contains(hint, StringComparison.OrdinalIgnoreCase))
                    return MonthlyActivityQueryIntent.YearToDate;
            }
        }

        return MonthlyActivityQueryIntent.SingleMonth;
    }
}
