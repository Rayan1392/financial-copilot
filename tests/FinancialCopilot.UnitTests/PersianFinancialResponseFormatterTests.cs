using FinancialCopilot.Infrastructure.AI.OrchestrationV2.Workflow;

namespace FinancialCopilot.UnitTests;

public sealed class PersianFinancialResponseFormatterTests
{
    [Fact]
    public void FormatBillionToman_IntegerAmount_OmitsUnnecessaryDecimals()
    {
        Assert.Equal("۱٬۰۰۰", PersianFinancialResponseFormatter.FormatBillionToman(10_000_000m));
    }

    [Fact]
    public void FormatBillionToman_DecimalAmount_UsesOneDecimalForLargeValues()
    {
        Assert.Equal("۱۲٬۲۹۵٫۳", PersianFinancialResponseFormatter.FormatBillionToman(122_953_413m));
    }

    [Fact]
    public void FormatBillionToman_UsesPersianDigitsAndSeparators()
    {
        Assert.Equal("۱۲٬۲۹۵٫۳", PersianFinancialResponseFormatter.FormatBillionToman(122_953_413m));
    }

    [Theory]
    [InlineData(null)]
    public void FormatBillionToman_MissingAmount_ReturnsPlaceholder(decimal? amount)
    {
        Assert.Equal("—", PersianFinancialResponseFormatter.FormatBillionToman(amount));
    }

    [Fact]
    public void ProductSalesValue_RendersPersianMarketFriendlyResponse()
    {
        var response = PersianFinancialResponseFormatter.ProductSalesValue(
            "فولاد", "محصولات گرم", 1405, 4, 122_953_413m);

        Assert.Equal("فروش «محصولات گرم» شرکت فولاد در دوره ۱۴۰۵/۰۴ حدود ۱۲٬۲۹۵٫۳ میلیارد تومان بوده است.", response);
        Assert.DoesNotContain("Product sales", response, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("million rial", response, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("122,953,413", response, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatBillionToman_PreservesSmallNonZeroAmount()
    {
        Assert.Equal("۰٫۰۲۵", PersianFinancialResponseFormatter.FormatBillionToman(250m));
    }
}
