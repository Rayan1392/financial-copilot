using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Infrastructure.Financial.Providers.NadpcoApi;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Spec 059 Story C — verifies that <see cref="DefaultMonthlyActivityOutputTypeResolver"/>
/// maps user query hints to the correct NADPCO outputTypeId.
/// </summary>
public sealed class MonthlyActivityOutputTypeResolverTests
{
    private readonly DefaultMonthlyActivityOutputTypeResolver _resolver = new();

    [Fact]
    public void Resolve_ExplicitMonth_ReturnsSingleMonth()
    {
        var result = _resolver.Resolve(userQueryHint: null, hasExplicitMonth: true);
        Assert.Equal(MonthlyActivityQueryIntent.SingleMonth, result);
    }

    [Fact]
    public void Resolve_NoHintNoExplicitMonth_ReturnsSingleMonth()
    {
        var result = _resolver.Resolve(userQueryHint: null, hasExplicitMonth: false);
        Assert.Equal(MonthlyActivityQueryIntent.SingleMonth, result);
    }

    [Fact]
    public void Resolve_YtdPersianHint_ReturnsYearToDate()
    {
        var result = _resolver.Resolve(userQueryHint: "فروش از ابتدای سال کگل", hasExplicitMonth: false);
        Assert.Equal(MonthlyActivityQueryIntent.YearToDate, result);
    }

    [Fact]
    public void Resolve_YtdEnglishHint_ReturnsYearToDate()
    {
        var result = _resolver.Resolve(userQueryHint: "YTD sales for Kegel", hasExplicitMonth: false);
        Assert.Equal(MonthlyActivityQueryIntent.YearToDate, result);
    }

    [Fact]
    public void Resolve_CumulativePersianHint_ReturnsYearToDate()
    {
        var result = _resolver.Resolve(userQueryHint: "فروش انباشته کگل", hasExplicitMonth: false);
        Assert.Equal(MonthlyActivityQueryIntent.YearToDate, result);
    }

    [Fact]
    public void Resolve_ExplicitMonthOverridesYtdHint_ReturnsSingleMonth()
    {
        // When the user specifies an explicit month AND a YTD-like phrase, the explicit month wins.
        var result = _resolver.Resolve(userQueryHint: "فروش از ابتدای سال اردیبهشت", hasExplicitMonth: true);
        Assert.Equal(MonthlyActivityQueryIntent.SingleMonth, result);
    }

    [Fact]
    public void Resolve_ArbitrarySalesQuery_ReturnsSingleMonth()
    {
        var result = _resolver.Resolve(userQueryHint: "آخرین فروش کگل چقدر بوده", hasExplicitMonth: false);
        Assert.Equal(MonthlyActivityQueryIntent.SingleMonth, result);
    }

    [Fact]
    public void SingleMonth_IntValue_IsZero()
    {
        // The enum value must equal NADPCO outputTypeId=0 so it can be passed as an int filter.
        Assert.Equal(0, (int)MonthlyActivityQueryIntent.SingleMonth);
    }

    [Fact]
    public void YearToDate_IntValue_IsOne()
    {
        Assert.Equal(1, (int)MonthlyActivityQueryIntent.YearToDate);
    }
}
