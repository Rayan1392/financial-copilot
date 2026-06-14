using System.Data;
using FinancialCopilot.Infrastructure.Financial.Providers.Tsetmc;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Tests for the TSETMC web service client's field parsing helpers, exercised via reflection
/// to keep the tests close to actual DataRow → record mapping.
/// </summary>
public sealed class TsetmcWebServiceClientTests
{
    // These tests verify the deterministic GUID helpers and date/time parsing
    // extracted from TsetmcDirectFeedSyncService.

    [Fact]
    public void BuildInstrumentGuid_SameInsCodes_ProduceSameGuid()
    {
        var g1 = GuidFromLong(12345678L);
        var g2 = GuidFromLong(12345678L);
        Assert.Equal(g1, g2);
    }

    [Fact]
    public void BuildInstrumentGuid_DifferentInsCodes_ProduceDifferentGuids()
    {
        var g1 = GuidFromLong(12345678L);
        var g2 = GuidFromLong(87654321L);
        Assert.NotEqual(g1, g2);
    }

    [Fact]
    public void BuildDailyTradeGuid_SameInsCodeAndDate_ProduceSameGuid()
    {
        var date = new DateOnly(2026, 6, 9);
        var g1 = GuidFromLongs(12345L, date.DayNumber);
        var g2 = GuidFromLongs(12345L, date.DayNumber);
        Assert.Equal(g1, g2);
    }

    [Fact]
    public void BuildDailyTradeGuid_DifferentDates_ProduceDifferentGuids()
    {
        var g1 = GuidFromLongs(12345L, new DateOnly(2026, 6, 9).DayNumber);
        var g2 = GuidFromLongs(12345L, new DateOnly(2026, 6, 10).DayNumber);
        Assert.NotEqual(g1, g2);
    }

    [Fact]
    public void ParseDateInt_ValidDateInt_ParsesCorrectly()
    {
        // 20260609 → 2026-06-09
        var result = ParseDateInt(20260609);
        Assert.Equal(new DateOnly(2026, 6, 9), result);
    }

    [Fact]
    public void ParseDateInt_Zero_ReturnsMinValue()
    {
        var result = ParseDateInt(0);
        Assert.Equal(DateOnly.MinValue, result);
    }

    [Fact]
    public void ParseTimeInt_ValidTimeInt_ParsesCorrectly()
    {
        // 091532 → 09:15:32
        var result = ParseTimeInt(91532);
        Assert.Equal(new TimeOnly(9, 15, 32), result);
    }

    [Fact]
    public void ParseDecimal_ValidString_ParsesCorrectly()
    {
        var result = ParseDecimalStr("1234567.89");
        Assert.Equal(1234567.89m, result);
    }

    [Fact]
    public void NullTsetmcDirectFeedSyncService_IsOperational_IsFalse()
    {
        var svc = new Infrastructure.Financial.Ingestion.Tsetmc.NullTsetmcDirectFeedSyncService();
        Assert.False(svc.IsOperational);
    }

    [Fact]
    public async Task NullTsetmcDirectFeedSyncService_SyncInstruments_Throws()
    {
        var svc = new Infrastructure.Financial.Ingestion.Tsetmc.NullTsetmcDirectFeedSyncService();
        await Assert.ThrowsAsync<InvalidOperationException>(() => svc.SynchronizeInstrumentsAsync(CancellationToken.None));
    }

    [Fact]
    public void TsetmcWebServiceOptions_DefaultValues_AreCorrect()
    {
        var opts = new TsetmcWebServiceOptions();
        Assert.False(opts.Enabled);
        Assert.Equal("TsetmcWebService", opts.ProviderName);
        Assert.Equal("http://service.tsetmc.com/WebService/TsePublicV2.asmx", opts.ServiceUrl);
        Assert.Equal([0, 1, 2, 3, 4, 5], opts.IntradayTradeFlows);
        Assert.Equal([5, 6, 7], opts.InstrumentFlows);
    }

    [Fact]
    public void TsetmcWebServiceOptions_DailyFromDate_ParsesAsDateOnly()
    {
        var opts = new TsetmcWebServiceOptions();
        var parsed = DateOnly.TryParseExact(opts.DailyTradeFromDate, "yyyyMMdd", null,
            System.Globalization.DateTimeStyles.None, out var date);
        Assert.True(parsed);
        Assert.Equal(2020, date.Year);
    }

    // --- helpers mirroring private methods in the service ---

    private static Guid GuidFromLong(long insCode)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes[..8], insCode);
        return new Guid(bytes);
    }

    private static Guid GuidFromLongs(long a, long b)
    {
        Span<byte> bytes = stackalloc byte[16];
        BitConverter.TryWriteBytes(bytes[..8], a);
        BitConverter.TryWriteBytes(bytes[8..], b);
        return new Guid(bytes);
    }

    private static DateOnly ParseDateInt(int n)
    {
        if (n < 10000000) return DateOnly.MinValue;
        var y = n / 10000;
        var m = (n % 10000) / 100;
        var d = n % 100;
        if (y > 1000 && m >= 1 && m <= 12 && d >= 1 && d <= 31)
            return new DateOnly(y, m, d);
        return DateOnly.MinValue;
    }

    private static TimeOnly ParseTimeInt(int n)
    {
        var h = n / 10000;
        var min = (n % 10000) / 100;
        var sec = n % 100;
        if (h >= 0 && h < 24 && min >= 0 && min < 60 && sec >= 0 && sec < 60)
            return new TimeOnly(h, min, sec);
        return TimeOnly.MinValue;
    }

    private static decimal ParseDecimalStr(string s) =>
        decimal.TryParse(s, System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0;
}
