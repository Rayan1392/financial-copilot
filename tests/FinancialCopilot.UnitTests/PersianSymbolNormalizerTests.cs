using FinancialCopilot.Domain.Financial.Services;

namespace FinancialCopilot.UnitTests;

public sealed class PersianSymbolNormalizerTests
{
    [Fact]
    public void Normalize_CleanAsciiTicker_ReturnsUnchanged()
    {
        var result = PersianSymbolNormalizer.Normalize("AAPL");
        Assert.Equal("AAPL", result);
    }

    [Fact]
    public void Normalize_CleanPersianTicker_ReturnsUnchanged()
    {
        var result = PersianSymbolNormalizer.Normalize("شغدیر");
        Assert.Equal("شغدیر", result);
    }

    [Fact]
    public void Normalize_ZwnjPolluted_StripsZwnj()
    {
        // U+200C ZWNJ embedded inside a Persian ticker
        var polluted = "شغ‌دیر";
        var result = PersianSymbolNormalizer.Normalize(polluted);
        Assert.Equal("شغدیر", result);
    }

    [Fact]
    public void Normalize_RlmPresent_StripsRlm()
    {
        var polluted = "‏شغدیر";
        var result = PersianSymbolNormalizer.Normalize(polluted);
        Assert.Equal("شغدیر", result);
    }

    [Fact]
    public void Normalize_RlePresent_StripsRle()
    {
        var polluted = "‫شغدیر";
        var result = PersianSymbolNormalizer.Normalize(polluted);
        Assert.Equal("شغدیر", result);
    }

    [Fact]
    public void Normalize_ArabicYe_MapsToPersianyYe()
    {
        // U+064A Arabic Ye → U+06CC Persian Ye
        var arabic = "فوليد";
        var result = PersianSymbolNormalizer.Normalize(arabic);
        Assert.Equal("فولید", result);
    }

    [Fact]
    public void Normalize_ArabicKaf_MapsToPersianyKaf()
    {
        // U+0643 Arabic Kaf → U+06A9 Persian Kaf
        var arabic = "كمکو";
        var result = PersianSymbolNormalizer.Normalize(arabic);
        Assert.Equal("کمکو", result);
    }

    [Fact]
    public void Normalize_NullInput_ReturnsEmptyString()
    {
        var result = PersianSymbolNormalizer.Normalize(null);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Normalize_WhitespaceOnly_ReturnsEmptyString()
    {
        var result = PersianSymbolNormalizer.Normalize("   ");
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Normalize_LeadingTrailingWhitespace_Trimmed()
    {
        var result = PersianSymbolNormalizer.Normalize("  شغدیر  ");
        Assert.Equal("شغدیر", result);
    }

    [Fact]
    public void Normalize_MultipleInvisibleChars_AllStripped()
    {
        var polluted = "‌‍شغ‏دیر﻿";
        var result = PersianSymbolNormalizer.Normalize(polluted);
        Assert.Equal("شغدیر", result);
    }

    [Fact]
    public void Normalize_SpacingVariants_CollapseToSameKey()
    {
        var separated = PersianSymbolNormalizer.Normalize("گل گهر");
        var joined = PersianSymbolNormalizer.Normalize("گلگهر");

        Assert.Equal("گلگهر", separated);
        Assert.Equal(joined, separated);
    }

    [Fact]
    public void Normalize_PunctuationAndExtraSpaces_AreIgnored()
    {
        var result = PersianSymbolNormalizer.Normalize("  فولاد-مبارکه،   اصفهان؟ ");
        Assert.Equal("فولادمبارکهاصفهان", result);
    }
}
