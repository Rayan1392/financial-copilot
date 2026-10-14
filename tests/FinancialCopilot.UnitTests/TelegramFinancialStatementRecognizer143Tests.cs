using FinancialCopilot.Application.Telegram;

namespace FinancialCopilot.UnitTests;

public sealed class TelegramFinancialStatementRecognizer143Tests
{
    private readonly TelegramFinancialStatementRecognizer recognizer = new();

    [Fact]
    public void Actual_thamid_nine_month_announcement_is_recognized()
    {
        const string announcement = """
            توسعه وعمران امید(#ثامید)
            اطلاعات و صورت‌های مالی میاندوره‌ای دوره 9 ماهه منتهی به 1405/06/31 (حسابرسی نشده)

            @NoavaranAmin

            ✅ افزایش 18 درصدی درآمدهای عملیاتی 9 ماهه 1405 در مقایسه با دوره مشابه

            تحلیل بیشتر در:
            @NoavaranCodal
            """;

        var result = recognizer.Recognize(announcement);

        Assert.Equal(new TelegramFinancialStatementRecognition(
            "ثامید", "Interim", 1405, 6, 31, 9, false, false, false, null), result);
    }

    [Theory]
    [InlineData("پردیس صنعت زنده رود(#زنده رود)\nصورت‌های مالی سال مالی منتهی به 1403/12/30 (حسابرسی نشده)", "زنده رود", 1403, 12, 30, 12, false, false)]
    [InlineData("قند لرستان(#قلرست)\nصورت های مالی سال مالی منتهی به 1405/04/31 (حسابرسی نشده)(اصلاحیه)", "قلرست", 1405, 4, 31, 12, false, true)]
    [InlineData("صنایع چوب خزر کاسپین(#چخزر)\nاطلاعات و صورت‌های مالی میاندوره‌ای دوره 6 ماهه منتهی به 1405/06/31 (حسابرسی نشده)", "چخزر", 1405, 6, 31, 6, false, false)]
    public void Supplied_examples_parse_header_fields(string text, string symbol, int year, int month, int day,
        int months, bool audited, bool amended)
    {
        var result = recognizer.Recognize(text);
        Assert.NotNull(result);
        Assert.Equal(symbol, result.Symbol);
        Assert.Equal((year, month, day, months),
            (result.PeriodYear, result.PeriodMonth, result.PeriodDay, result.PeriodMonths));
        Assert.Equal((bool?)audited, result.IsAudited);
        Assert.Equal(amended, result.IsAmendment);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(6)]
    [InlineData(9)]
    [InlineData(12)]
    public void Supports_period_months(int months) =>
        Assert.Equal(months, recognizer.Recognize($"#نماد\nصورت های مالی دوره {months} ماهه منتهی به 1405/06/31")?.PeriodMonths);

    [Fact]
    public void Persian_digits_and_explicit_variants_are_supported()
    {
        var result = recognizer.Recognize("#نماد\nصورتهای مالی دوره ۶ ماهه منتهی به ۱۴۰۵/۰۶/۳۱ (حسابرسی شده) (تلفیقی) (اصلاحیه)");
        Assert.NotNull(result);
        Assert.True(result.IsAudited);
        Assert.True(result.IsAmendment);
        Assert.True(result.IsConsolidated);
    }

    [Fact]
    public void Arabic_indic_digits_and_standalone_marker_are_supported()
    {
        var result = recognizer.Recognize("#نماد\nصورت های مالی دوره ٦ ماهه منتهی به ١٤٠٥/٠٦/٣١ (شرکت اصلی)");
        Assert.NotNull(result);
        Assert.Equal((1405, 6, 31, 6), (result.PeriodYear, result.PeriodMonth, result.PeriodDay, result.PeriodMonths));
        Assert.False(result.IsConsolidated);
    }

    [Fact]
    public void Invalid_or_conflicting_header_periods_fail_closed()
    {
        Assert.Null(recognizer.Recognize("#نماد\nصورت مالی دوره 6 ماهه منتهی به 1405/12/30"));
        Assert.Null(recognizer.Recognize("#نماد\nصورت مالی دوره 6 ماهه منتهی به 1405/06/31 و 1405/09/30"));
        Assert.Null(recognizer.Recognize("#نماد\nصورت مالی دوره منتهی به 1405/06/31"));
    }

    [Fact]
    public void Commentary_dates_are_ignored_and_monthly_reports_are_not_claimed()
    {
        var text = "#نماد\nصورت های مالی سال مالی منتهی به 1403/12/30\nرشد 214 درصد، EPS 24968 و 12 ماهه 1404";
        var result = recognizer.Recognize(text);
        Assert.NotNull(result);
        Assert.Equal((1403, 12, 30), (result.PeriodYear, result.PeriodMonth, result.PeriodDay));
        Assert.Null(recognizer.Recognize("#نماد\nگزارش تولید و فروش ماهانه فعالیت ماهانه 1405/06/31"));
    }
}
