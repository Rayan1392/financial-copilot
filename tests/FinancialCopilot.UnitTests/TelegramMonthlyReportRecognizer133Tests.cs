using FinancialCopilot.Application.Telegram;

namespace FinancialCopilot.UnitTests;

public sealed class TelegramMonthlyReportRecognizer133Tests
{
    private readonly TelegramMonthlyReportRecognizer recognizer = new();

    [Fact]
    public void Sample_report_extracts_symbol_and_numeric_period()
    {
        var result = recognizer.Recognize("#فعالیت_ماهانه #دسینا گزارش تولید و فروش دوره ۱ ماهه منتهی به ۱۴۰۵/۰۵/۳۱");

        Assert.Equal(new TelegramMonthlyReportRecognition("دسینا", 1405, 5), result);
    }

    [Fact]
    public void Month_hashtag_and_arabic_digits_are_supported()
    {
        var result = recognizer.Recognize("#فعالیت ماهانه #دسینا #مرداد_١٤٠٥");

        Assert.Equal(new TelegramMonthlyReportRecognition("دسینا", 1405, 5), result);
    }

    [Fact]
    public void Caption_text_uses_the_same_deterministic_rules()
    {
        Assert.NotNull(recognizer.Recognize("گزارش ماهانه تولید و فروش #فملی دوره 1 ماهه منتهی به 1405-05-31"));
    }

    [Fact]
    public void Fiscal_year_end_does_not_supply_a_month()
    {
        Assert.Null(recognizer.Recognize("#فعالیت_ماهانه #فملی سال مالی منتهی به 1405/12/29"));
    }

    [Fact]
    public void Conflicting_periods_are_rejected()
    {
        Assert.Null(recognizer.Recognize("#فعالیت_ماهانه #فملی 1405/05/31 و 1405/06/31"));
    }

    [Fact]
    public void Multi_month_only_report_is_rejected()
    {
        Assert.Null(recognizer.Recognize("#فعالیت_ماهانه #فملی گزارش سه ماهه 1405/05/31"));
    }
}
