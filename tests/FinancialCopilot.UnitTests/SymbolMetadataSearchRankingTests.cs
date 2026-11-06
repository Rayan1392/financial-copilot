using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using FinancialCopilot.Infrastructure.Financial.Metadata;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.UnitTests;

/// <summary>
/// Watchlist «افزودن نماد» search: matches on symbol or company name, ranked by relevance so the
/// exact / prefix symbol match is never truncated by alphabetically earlier name-only matches.
/// Regression: searching «خودرو» returned «خاور…», «بموتو3», … but not ایران خودرو itself.
/// </summary>
public sealed class SymbolMetadataSearchRankingTests
{
    private static FinancialIngestionDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<FinancialIngestionDbContext>()
            .UseInMemoryDatabase($"symbol-search-{Guid.NewGuid():N}")
            .Options;
        return new FinancialIngestionDbContext(options);
    }

    private static void AddCompany(FinancialIngestionDbContext db, string symbol, string name) =>
        db.Companies.Add(new NormalizedCompanyRow
        {
            Id = Guid.NewGuid(),
            ProviderName = "test",
            ExternalCompanyId = $"EXT-{symbol}",
            TseSymbol = symbol,
            Name = name,
            LastSynchronizedAt = DateTimeOffset.UtcNow
        });

    private static async Task<FinancialIngestionDbContext> SeedAutomotiveAsync()
    {
        var db = CreateDb();
        // Alphabetically earlier symbols whose company name merely contains «خودرو» — more than
        // limit * 3 of them, so the old alphabetical ordering truncated «خودرو» away.
        var nameOnly = new List<string> { "خاذین", "خاذینح", "بموتو3", "تاپرا6", "تپکو12", "بهین رو", "18719101" };
        nameOnly.AddRange(Enumerable.Range(1, 40).Select(index => $"خاور{index}"));
        foreach (var symbol in nameOnly)
        {
            AddCompany(db, symbol, $"شرکت خودرو {symbol}");
        }

        AddCompany(db, "خودرو", "ایران خودرو");
        AddCompany(db, "خودروح", "ح. ایران خودرو");
        AddCompany(db, "خساپا", "سایپا");
        await db.SaveChangesAsync();
        return db;
    }

    [Fact]
    public async Task ExactSymbolMatch_IsFirst_EvenWhenManyNameMatchesSortEarlier()
    {
        await using var db = await SeedAutomotiveAsync();
        var service = new EfCoreAssistedQueryMetadataService(db);

        var results = (await service.SearchSymbolsAsync("خودرو", 12, CancellationToken.None)).ToArray();

        Assert.Equal(12, results.Length);
        Assert.Equal("خودرو", results[0].SymbolCode);
        Assert.Equal("ایران خودرو", results[0].CompanyName);
        Assert.Equal("خودروح", results[1].SymbolCode); // symbol prefix ranks next
    }

    [Fact]
    public async Task CompanyNameSearch_StillFindsCompany_AndRanksExactNameFirst()
    {
        await using var db = await SeedAutomotiveAsync();
        var service = new EfCoreAssistedQueryMetadataService(db);

        var results = (await service.SearchSymbolsAsync("ایران خودرو", 12, CancellationToken.None)).ToArray();

        Assert.Equal("خودرو", results[0].SymbolCode); // exact company name
        Assert.Contains(results, item => item.SymbolCode == "خودروح"); // company name contains
        Assert.DoesNotContain(results, item => item.SymbolCode == "خساپا");
    }

    [Fact]
    public async Task SymbolPrefix_OutranksNameOnlyMatch()
    {
        await using var db = CreateDb();
        AddCompany(db, "الف", "شرکت فولاد الف");
        AddCompany(db, "فولاد", "فولاد مبارکه اصفهان");
        AddCompany(db, "فولادح", "ح. فولاد مبارکه اصفهان");
        await db.SaveChangesAsync();
        var service = new EfCoreAssistedQueryMetadataService(db);

        var symbols = (await service.SearchSymbolsAsync("فولاد", 10, CancellationToken.None))
            .Select(item => item.SymbolCode)
            .ToArray();

        Assert.Equal(["فولاد", "فولادح", "الف"], symbols);
    }

    [Theory]
    // stored with Arabic letters (some vendor feeds), typed with Persian keyboard
    [InlineData("كچاد", "كاني چادرملو", "کچاد")]
    [InlineData("كچاد", "كاني چادرملو", "کانی چادرملو")]
    // stored with Persian letters, typed with Arabic letters (Arabic keyboard / copy-paste)
    [InlineData("کچاد", "کانی چادرملو", "كچاد")]
    [InlineData("کچاد", "کانی چادرملو", "كانى چادرملو")]
    public async Task ArabicAndPersianYehKaf_MatchEachOther_OnSymbolAndCompanyName(
        string storedSymbol, string storedName, string search)
    {
        await using var db = CreateDb();
        AddCompany(db, storedSymbol, storedName);
        AddCompany(db, "فولاد", "فولاد مبارکه اصفهان");
        await db.SaveChangesAsync();
        var service = new EfCoreAssistedQueryMetadataService(db);

        var results = (await service.SearchSymbolsAsync(search, 10, CancellationToken.None)).ToArray();

        var match = Assert.Single(results);
        Assert.Equal(storedSymbol, match.SymbolCode); // stored value is returned unchanged
    }

    [Fact]
    public async Task ArabicKeyboardSymbol_StillRanksAsExactSymbolMatch()
    {
        await using var db = CreateDb();
        AddCompany(db, "الف", "شرکت تاپیکو الف");
        AddCompany(db, "تاپیکو", "سرمایه گذاری نفت و گاز و پتروشیمی تامین");
        await db.SaveChangesAsync();
        var service = new EfCoreAssistedQueryMetadataService(db);

        var symbols = (await service.SearchSymbolsAsync("تاپيكو", 10, CancellationToken.None))
            .Select(item => item.SymbolCode)
            .ToArray();

        Assert.Equal(["تاپیکو", "الف"], symbols);
    }

    [Fact]
    public async Task EmptySearch_KeepsAlphabeticalBrowseOrder()
    {
        await using var db = CreateDb();
        AddCompany(db, "ب", "بی");
        AddCompany(db, "الف", "الفی");
        await db.SaveChangesAsync();
        var service = new EfCoreAssistedQueryMetadataService(db);

        var symbols = (await service.SearchSymbolsAsync(null, 10, CancellationToken.None))
            .Select(item => item.SymbolCode)
            .ToArray();

        Assert.Equal(["الف", "ب"], symbols);
    }
}
