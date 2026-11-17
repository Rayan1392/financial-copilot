using FinancialCopilot.Infrastructure.Authentication;

namespace FinancialCopilot.UnitTests;

/// <summary>Feature 163 change «answer-readability» D4: bold and list items in the Telegram generic text path.</summary>
public sealed class TelegramBoldRenderingTests
{
    [Fact]
    public void BalancedBold_BecomesTelegramBold_WithItsTextEscaped() =>
        Assert.Equal(
            "فروش *رشد ۱۰۰٫۸ درصد* بود\\.",
            TelegramAssistantResponseRenderer.EscapeMarkdownV2WithBold("فروش **رشد ۱۰۰٫۸ درصد** بود."));

    [Fact]
    public void ListItem_BecomesABullet_AndKeepsItsBoldLabel() =>
        Assert.Equal(
            "• *فروش:* ۱٬۶۷۵٬۲۵۵٬۳۸۵ میلیون ریال",
            TelegramAssistantResponseRenderer.EscapeMarkdownV2WithBold("- **فروش:** ۱٬۶۷۵٬۲۵۵٬۳۸۵ میلیون ریال"));

    [Theory]
    [InlineData("سود **خالص بالا رفت", "سود \\*\\*خالص بالا رفت")]
    [InlineData("**الف** و **ب", "\\*\\*الف\\*\\* و \\*\\*ب")]
    public void UnbalancedMarkers_AreEscapedAsBefore(string input, string expected) =>
        Assert.Equal(expected, TelegramAssistantResponseRenderer.EscapeMarkdownV2WithBold(input));

    [Fact]
    public void SpecialCharactersInsideBold_AreEscaped() =>
        Assert.Equal(
            "*P/E \\(TTM\\) \\- ۵\\.۲*",
            TelegramAssistantResponseRenderer.EscapeMarkdownV2WithBold("**P/E (TTM) - ۵.۲**"));

    [Fact]
    public void Lines_AndBlankLines_AreKept() =>
        Assert.Equal(
            "*نتیجه\\.*\n\n• الف\n• ب",
            TelegramAssistantResponseRenderer.EscapeMarkdownV2WithBold("**نتیجه.**\r\n\r\n- الف\n- ب"));

    [Fact]
    public void TextWithoutMarkdown_IsUnchangedFromThePlainEscape() =>
        Assert.Equal(
            "پاسخ ساده با عدد ۱۲\\.۵ و \\(پرانتز\\)",
            TelegramAssistantResponseRenderer.EscapeMarkdownV2WithBold("پاسخ ساده با عدد ۱۲.۵ و (پرانتز)"));
}
