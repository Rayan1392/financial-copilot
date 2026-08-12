using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.UnitTests;

public sealed class IndustryRelativeValuationOptionsTests
{
    [Fact]
    public void Defaults_are_valid_and_match_design()
    {
        var options = new IndustryRelativeValuationOptions();
        Assert.True(options.IsValid(out var error), error);
        Assert.Equal(1440, options.DailyCadenceMinutes);
        Assert.Equal(26, options.SourceFreshnessHours);
        Assert.Equal(1.5m, options.IqrMultiplier);
        Assert.Equal(3, options.DefaultResultLimit);
        Assert.Equal(100, options.MaximumResultLimit);
        Assert.Equal(3, options.EntryConsecutiveSnapshots);
        Assert.Equal(3, options.ExitConsecutiveSnapshots);
    }

    [Theory]
    [InlineData(nameof(IndustryRelativeValuationOptions.DailyCadenceMinutes), 1439)]
    [InlineData(nameof(IndustryRelativeValuationOptions.SourceFreshnessHours), 0)]
    [InlineData(nameof(IndustryRelativeValuationOptions.DefaultResultLimit), 0)]
    [InlineData(nameof(IndustryRelativeValuationOptions.MaximumResultLimit), 1001)]
    [InlineData(nameof(IndustryRelativeValuationOptions.EntryConsecutiveSnapshots), 31)]
    public void Out_of_range_values_are_rejected(string property, int value)
    {
        var options = new IndustryRelativeValuationOptions();
        typeof(IndustryRelativeValuationOptions).GetProperty(property)!.SetValue(options, value);
        Assert.False(options.IsValid(out var error));
        Assert.NotEmpty(error);
    }

    [Fact]
    public void Default_limit_cannot_exceed_maximum()
    {
        var options = new IndustryRelativeValuationOptions { DefaultResultLimit = 10, MaximumResultLimit = 5 };
        Assert.False(options.IsValid(out var error));
        Assert.Contains("cannot exceed", error);
    }
}
