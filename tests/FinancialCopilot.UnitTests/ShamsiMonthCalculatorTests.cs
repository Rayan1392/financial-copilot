using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.UnitTests;

public sealed class ShamsiMonthCalculatorTests
{
    [Fact]
    public void LatestPublishedMonth_MidMonth_ReturnsPreviousShamsiMonth()
    {
        // 2026-06-10 = 20 Khordad 1405 → latest fully published month is Ordibehesht (1405/02).
        var month = ShamsiMonthCalculator.LatestPublishedMonth(DateTimeOffset.Parse("2026-06-10T08:00:00Z"));

        Assert.Equal(new ShamsiMonth(1405, 2), month);
    }

    [Fact]
    public void LatestPublishedMonth_Farvardin_RollsOverToPreviousYearEsfand()
    {
        // 2026-04-01 = 12 Farvardin 1405 → previous month is Esfand 1404 (1404/12).
        var month = ShamsiMonthCalculator.LatestPublishedMonth(DateTimeOffset.Parse("2026-04-01T08:00:00Z"));

        Assert.Equal(new ShamsiMonth(1404, 12), month);
    }

    [Fact]
    public void LatestPublishedMonth_NeverEarlierThanPermittedFloor()
    {
        // 2025-04-01 = Farvardin 1404 → previous month would be Esfand 1403, below the floor.
        var month = ShamsiMonthCalculator.LatestPublishedMonth(DateTimeOffset.Parse("2025-04-01T08:00:00Z"));

        Assert.Equal(ShamsiMonthCalculator.MonthlyActivityFloor, month);
    }

    [Fact]
    public void DescendingMonths_WalksNewestFirstDownToFloorInclusive()
    {
        var months = ShamsiMonthCalculator.DescendingMonths(
            new ShamsiMonth(1405, 2),
            ShamsiMonthCalculator.MonthlyActivityFloor);

        Assert.Equal(14, months.Count);
        Assert.Equal(new ShamsiMonth(1405, 2), months[0]);
        Assert.Equal(new ShamsiMonth(1405, 1), months[1]);
        Assert.Equal(new ShamsiMonth(1404, 12), months[2]);
        Assert.Equal(new ShamsiMonth(1404, 1), months[^1]);
    }

    [Fact]
    public void DescendingMonths_NewestBelowFloor_IsEmpty()
    {
        var months = ShamsiMonthCalculator.DescendingMonths(
            new ShamsiMonth(1403, 12),
            ShamsiMonthCalculator.MonthlyActivityFloor);

        Assert.Empty(months);
    }

    [Theory]
    [InlineData(1405, 2, "1405/02/31")]  // First half: 31 days.
    [InlineData(1405, 8, "1405/08/30")]  // Second half: 30 days.
    [InlineData(1404, 12, "1404/12/29")] // Esfand 1404 (common year): 29 days.
    public void LastDayJalali_RespectsPersianCalendarMonthLengths(int year, int month, string expected) =>
        Assert.Equal(expected, ShamsiMonthCalculator.LastDayJalali(new ShamsiMonth(year, month)));

    [Fact]
    public void FirstDayJalali_FormatsAsPaddedJalaliDate() =>
        Assert.Equal("1405/02/01", new ShamsiMonth(1405, 2).FirstDayJalali);
}
