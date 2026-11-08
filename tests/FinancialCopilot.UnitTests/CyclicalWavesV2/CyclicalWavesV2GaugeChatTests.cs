using FinancialCopilot.Domain.Financial.CyclicalWavesV2;
using FinancialCopilot.Domain.Financial.Services;
using FinancialCopilot.Infrastructure.Financial.Ingestion.CyclicalWavesV2;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.UnitTests.CyclicalWavesV2;

public sealed class CyclicalWavesV2GaugeChatTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private FinancialIngestionDbContext _db = null!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        _db = new FinancialIngestionDbContext(new DbContextOptionsBuilder<FinancialIngestionDbContext>().UseSqlite(_connection).Options);
        await _db.Database.EnsureCreatedAsync();
        SeedTicker("فولاد", pe: ("5.9751", 1), ps: ("1.7531", 2));
        SeedTicker("فملی", pe: null, ps: (null, null));
        SeedTicker("شستا", pe: null, ps: ("5", 2));
        SeedTicker("کگل", pe: (null, null), ps: null);
        var folad = await _db.CyclicalWavesV2Tickers.SingleAsync(x => x.Ticker == "فولاد");
        _db.CyclicalWavesV2EquilibriumRanges.Add(new CyclicalWavesV2EquilibriumRangeRow
        {
            TickerId = folad.Id, HasRange = true, Min = ExactDecimal.Parse("-3236.7000000000000000001"),
            Avg = ExactDecimal.Parse("-7232.1700000000000000002"), Max = ExactDecimal.Parse("-11227.6500000000000000003"),
            FetchedAtUtc = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    [Theory]
    [InlineData("وضعیت گیج P/E فولاد چگونه است؟", true)]
    [InlineData("وضعیت گیج P/S کگل چیست؟", true)]
    [InlineData("P/E فولاد نسبت به میانگین تاریخی خودش در چه وضعیتی قرار دارد؟", true)]
    [InlineData("مقدار فعلی P/S فولاد نسبت به محدوده تاریخی آن کجاست؟", true)]
    [InlineData("قیمت تعادلی فولاد چقدر است؟", true)]
    [InlineData("کدام نمادها در وضعیت P/E زیر حداقل تاریخی قرار دارند؟", true)]
    [InlineData("کدام نمادها در وضعیت P/S زیر میانگین تاریخی قرار دارند؟", true)]
    [InlineData("P/E فولاد چقدر است؟", false)]
    public void IntentRules_RecognizeGaugeRequestsWithoutChangingPlainMetricLookups(string query, bool expected) =>
        Assert.Equal(expected, CyclicalWavesV2GaugeIntentRules.LooksLikeQuery(query));

    [Fact]
    public async Task Pe_and_ps_answers_keep_current_historical_and_axis_values_separate()
    {
        var service = new CyclicalWavesV2GaugeQueryService(_db);
        var pe = await service.AnswerAsync("گیج P/E فولاد", CancellationToken.None);
        var ps = await service.AnswerAsync("گیج P/S فولاد", CancellationToken.None);

        Assert.Contains("CyclicalWaves v2", pe, StringComparison.Ordinal);
        Assert.Contains("مقدار فعلی 5.9751", pe, StringComparison.Ordinal);
        Assert.Contains("کمینه 2", pe, StringComparison.Ordinal);
        Assert.Contains("میانگین 4", pe, StringComparison.Ordinal);
        Assert.Contains("محور 1 تا 10", pe, StringComparison.Ordinal);
        Assert.Contains("روزها", pe, StringComparison.Ordinal);
        Assert.Contains("مقدار فعلی 1.7531", ps, StringComparison.Ordinal);
        Assert.Contains("میانگین 4", ps, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Null_close_and_status_are_explicitly_unavailable_not_zero()
    {
        var service = new CyclicalWavesV2GaugeQueryService(_db);

        var text = await service.AnswerAsync("گیج P/S فملی", CancellationToken.None);

        Assert.Contains("مقدار فعلی ناموجود", text, StringComparison.Ordinal);
        Assert.Contains("رتبه‌بندی نشده", text, StringComparison.Ordinal);
        Assert.DoesNotContain("مقدار فعلی 0", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Equilibrium_answer_keeps_negative_values_in_irr_and_disclaims_future_price()
    {
        var service = new CyclicalWavesV2GaugeQueryService(_db);

        var text = await service.AnswerAsync("قیمت تعادلی فولاد", CancellationToken.None);

        Assert.Contains("حداقل -3236.7000000000000000001", text, StringComparison.Ordinal);
        Assert.Contains("میانگین -7232.1700000000000000002", text, StringComparison.Ordinal);
        Assert.Contains("حداکثر -11227.6500000000000000003", text, StringComparison.Ordinal);
        Assert.Contains("ریال", text, StringComparison.Ordinal);
        Assert.Contains("تضمین قیمت آینده نیست", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Screening_excludes_null_ps_closes_and_uses_approved_comparisons()
    {
        var service = new CyclicalWavesV2GaugeQueryService(_db);

        var pe = await service.AnswerAsync("کدام نماد P/E زیر حداقل تاریخی است؟", CancellationToken.None);
        var ps = await service.AnswerAsync("کدام نماد P/S زیر میانگین تاریخی است؟", CancellationToken.None);

        Assert.Contains("فولاد", pe, StringComparison.Ordinal);
        Assert.DoesNotContain("کگل", pe, StringComparison.Ordinal);
        Assert.Contains("فولاد", ps, StringComparison.Ordinal);
        Assert.DoesNotContain("فملی", ps, StringComparison.Ordinal);
        Assert.DoesNotContain("شستا", ps, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Missing_gauge_and_unknown_ticker_have_explicit_unavailable_messages()
    {
        var service = new CyclicalWavesV2GaugeQueryService(_db);

        var noGauge = await service.AnswerAsync("گیج P/E فملی", CancellationToken.None);
        var unknown = await service.AnswerAsync("گیج P/E نمادناشناخته", CancellationToken.None);

        Assert.Contains("در دسترس نیست", noGauge, StringComparison.Ordinal);
        Assert.Contains("در کاتالوگ CyclicalWaves پیدا نشد", unknown, StringComparison.Ordinal);
    }

    private void SeedTicker(string symbol, (string? Close, int? Status)? pe, (string? Close, int? Status)? ps)
    {
        var id = Guid.NewGuid();
        _db.CyclicalWavesV2Tickers.Add(new CyclicalWavesV2TickerRow
        {
            Id = id, Ticker = symbol, NormalizedTicker = PersianSymbolNormalizer.Normalize(symbol), IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow, LastSeenInCatalogAt = DateTimeOffset.UtcNow
        });
        _db.CyclicalWavesV2GaugeAvailability.Add(new CyclicalWavesV2GaugeAvailabilityRow
        {
            TickerId = id, HasPe = pe is not null, HasPs = ps is not null, LastFetchedAtUtc = DateTimeOffset.UtcNow
        });
        if (pe is { } peData) _db.CyclicalWavesV2GaugeDetails.Add(Gauge(id, "PE", peData.Close, peData.Status));
        if (ps is { } psData) _db.CyclicalWavesV2GaugeDetails.Add(Gauge(id, "PS", psData.Close, psData.Status));
        _db.SaveChanges();
    }

    private static CyclicalWavesV2GaugeDetailRow Gauge(Guid tickerId, string type, string? close, int? status) => new()
    {
        TickerId = tickerId, GaugeType = type, A = 1, B = 2, C = 3, D = 4, E = 5, F = 6,
        Close = close is null ? null : ExactDecimal.Parse(close), Start = ExactDecimal.Parse("1"), End = ExactDecimal.Parse("10"),
        Min = ExactDecimal.Parse("2"), Avg = ExactDecimal.Parse("4"), Max = ExactDecimal.Parse("8"), Status = status,
        FetchedAtUtc = DateTimeOffset.UtcNow
    };
}
