namespace FinancialCopilot.Application.FinancialData.Ingestion;

public sealed class MonthlySalesQualityScoreCalculator : IMonthlySalesQualityScoreCalculator
{
    private const decimal SalesGrowthWeight = 25m;
    private const decimal QuantityGrowthWeight = 20m;
    private const decimal RateGrowthWeight = 15m;
    private const decimal ProductMixWeight = 15m;
    private const decimal PersistenceWeight = 15m;
    private const decimal IndustryWeight = 10m;

    public MonthlySalesQualityScoreResult Calculate(MonthlySalesQualityScoreInput input)
    {
        var positiveDrivers = new List<string>();
        var negativeDrivers = new List<string>();
        var dimensionScores = new List<(decimal? Score, decimal Weight)>();

        var salesVsAvg = PercentChange(input.MonthlySalesAmount, input.Avg12MonthSalesAmount);
        var mom = PercentChange(input.MonthlySalesAmount, input.PreviousMonthSalesAmount);
        var yoy = PercentChange(input.MonthlySalesAmount, input.SameMonthPreviousYearSalesAmount);

        decimal? salesGrowthScore = salesVsAvg.HasValue
            ? MapSalesGrowthVsAverage(salesVsAvg.Value)
            : null;
        dimensionScores.Add((salesGrowthScore, SalesGrowthWeight));
        AddSalesGrowthDrivers(salesVsAvg, positiveDrivers, negativeDrivers);

        var quantityScore = CalculateQuantityGrowthQuality(input, positiveDrivers, negativeDrivers);
        dimensionScores.Add((quantityScore, QuantityGrowthWeight));

        var (rateScore, suspiciousRateSpike) = CalculateRateGrowthQuality(input, positiveDrivers, negativeDrivers);
        dimensionScores.Add((rateScore, RateGrowthWeight));

        var productMixScore = CalculateProductMixStrength(input, positiveDrivers, negativeDrivers);
        dimensionScores.Add((productMixScore, ProductMixWeight));

        var trendScore = CalculatePersistenceTrend(input, positiveDrivers, negativeDrivers);
        dimensionScores.Add((trendScore, PersistenceWeight));

        var industryScore = CalculateIndustryRelativeStrength(input, positiveDrivers, negativeDrivers);
        dimensionScores.Add((industryScore, IndustryWeight));

        var qualityScore = WeightedAverageReweighted(dimensionScores);
        var coverage = new MonthlySalesQualityDataCoverage(
            input.HistoryMonths,
            input.HasProductLineItems,
            input.ProductMixRows.Count > 0,
            input.IndustryPeerCount);

        var confidence = CalculateConfidence(input, suspiciousRateSpike, salesVsAvg.HasValue, mom.HasValue, yoy.HasValue);

        if (!input.HasProductLineItems)
            negativeDrivers.Add("داده محصول برای تحلیل مقدار/نرخ کامل نیست");
        if (input.ProductMixRows.Count == 0)
            negativeDrivers.Add("داده ترکیب فروش محصولات موجود نیست و وزن آن بازتوزیع شد");
        if (input.IndustryPeerCount is > 0 and < 5)
            negativeDrivers.Add("تعداد هم‌گروه‌های صنعت برای مقایسه کافی نیست");

        return new MonthlySalesQualityScoreResult(
            QualityScore: qualityScore,
            QualityLabel: GetQualityLabel(qualityScore),
            ConfidenceScore: confidence,
            DimensionScores: new MonthlySalesQualityDimensionScores(
                salesGrowthScore,
                quantityScore,
                rateScore,
                productMixScore,
                trendScore,
                industryScore),
            PositiveDrivers: positiveDrivers.Distinct().Take(5).ToList(),
            NegativeDrivers: negativeDrivers.Distinct().Take(5).ToList(),
            DataCoverage: coverage,
            SalesVsAvg12MPercent: salesVsAvg,
            SalesMonthOverMonthPercent: mom,
            SalesYearOverYearPercent: yoy);
    }

    private static decimal? PercentChange(decimal current, decimal? baseline)
    {
        if (!baseline.HasValue || baseline.Value <= 0m) return null;
        return (current - baseline.Value) / baseline.Value * 100m;
    }

    private static decimal MapSalesGrowthVsAverage(decimal percent)
    {
        var capped = Math.Clamp(percent, -50m, 150m);
        return capped switch
        {
            <= -50m => 0m,
            <= -25m => Interpolate(capped, -50m, 0m, -25m, 25m),
            <= 0m => Interpolate(capped, -25m, 25m, 0m, 50m),
            <= 50m => Interpolate(capped, 0m, 50m, 50m, 80m),
            <= 100m => Interpolate(capped, 50m, 80m, 100m, 100m),
            _ => 100m
        };
    }

    private static decimal Interpolate(decimal value, decimal x1, decimal y1, decimal x2, decimal y2) =>
        y1 + (value - x1) * (y2 - y1) / (x2 - x1);

    private static decimal? CalculateQuantityGrowthQuality(
        MonthlySalesQualityScoreInput input,
        List<string> positiveDrivers,
        List<string> negativeDrivers)
    {
        var salesQtyGrowth = PercentChange(input.MonthlySalesQuantity ?? 0m, input.PreviousMonthSalesQuantity);
        var productionQtyGrowth = PercentChange(input.MonthlyProductionQuantity ?? 0m, input.PreviousMonthProductionQuantity);
        var momSales = PercentChange(input.MonthlySalesAmount, input.PreviousMonthSalesAmount);

        var quantitySignal = salesQtyGrowth ?? productionQtyGrowth;
        if (!quantitySignal.HasValue) return null;

        if (momSales >= 0m && quantitySignal >= 10m)
        {
            positiveDrivers.Add("رشد فروش با رشد مقدار همراه بوده است");
            return 85m;
        }

        if (momSales >= 0m && quantitySignal >= -5m)
        {
            positiveDrivers.Add("مقدار فروش/تولید همزمان با فروش پایدار بوده است");
            return 70m;
        }

        if (momSales >= 10m && quantitySignal <= -20m)
        {
            negativeDrivers.Add("رشد فروش عمدتاً با افت مقدار همراه بوده است");
            return 25m;
        }

        if (quantitySignal <= -20m)
        {
            negativeDrivers.Add("مقدار فروش/تولید کاهش معنادار داشته است");
            return 35m;
        }

        return 55m;
    }

    private static (decimal? Score, bool SuspiciousRateSpike) CalculateRateGrowthQuality(
        MonthlySalesQualityScoreInput input,
        List<string> positiveDrivers,
        List<string> negativeDrivers)
    {
        var rateGrowth = PercentChange(input.MonthlyAverageSalesRate ?? 0m, input.PreviousMonthAverageSalesRate);
        if (!rateGrowth.HasValue) return (null, false);

        var quantityGrowth = PercentChange(input.MonthlySalesQuantity ?? 0m, input.PreviousMonthSalesQuantity);
        var suspicious = rateGrowth.Value > 150m;

        if (suspicious)
        {
            negativeDrivers.Add("جهش غیرعادی نرخ فروش مشاهده شده و اطمینان کاهش یافته است");
            return (45m, true);
        }

        if (rateGrowth.Value is >= 5m and <= 50m && (!quantityGrowth.HasValue || quantityGrowth.Value >= -10m))
        {
            positiveDrivers.Add("افزایش نرخ فروش با افت شدید مقدار همراه نیست");
            return (75m, false);
        }

        if (rateGrowth.Value > 50m && quantityGrowth <= -20m)
        {
            negativeDrivers.Add("بخشی از رشد فروش ناشی از افزایش نرخ و افت مقدار است");
            return (35m, false);
        }

        if (rateGrowth.Value < -10m)
        {
            negativeDrivers.Add("نرخ فروش نسبت به دوره قبل افت کرده است");
            return (40m, false);
        }

        return (55m, false);
    }

    private static decimal? CalculateProductMixStrength(
        MonthlySalesQualityScoreInput input,
        List<string> positiveDrivers,
        List<string> negativeDrivers)
    {
        if (input.ProductMixRows.Count == 0) return null;

        var top = input.ProductMixRows.OrderBy(r => r.ProductRank).First();
        var momSales = PercentChange(input.MonthlySalesAmount, input.PreviousMonthSalesAmount);

        if (top.RevenueSharePercentage >= 85m && momSales < 0m)
        {
            negativeDrivers.Add("تمرکز درآمد روی محصول اصلی بالا است و فروش کل کاهش داشته است");
            return 35m;
        }

        if (top.IsDominantProduct && top.RevenueSharePercentage is >= 35m and <= 80m && momSales >= 0m)
        {
            positiveDrivers.Add("محصول اصلی سهم معنادار دارد و فروش کل بهبود یافته است");
            return 80m;
        }

        if (input.ProductMixRows.Count >= 3 && top.RevenueSharePercentage <= 70m)
        {
            positiveDrivers.Add("ترکیب درآمد محصولات متنوع‌تر از تمرکز تک‌محصولی است");
            return 70m;
        }

        return 55m;
    }

    private static decimal? CalculatePersistenceTrend(
        MonthlySalesQualityScoreInput input,
        List<string> positiveDrivers,
        List<string> negativeDrivers)
    {
        if (input.LastThreeMonthlySalesAmounts.Count < 2) return null;

        var ordered = input.LastThreeMonthlySalesAmounts.TakeLast(3).ToList();
        var current = ordered[^1];
        var previous = ordered.Count >= 2 ? ordered[^2] : (decimal?)null;
        var upward = ordered.Count == 3 && ordered[0] <= ordered[1] && ordered[1] <= ordered[2];
        var consistentlyAboveAverage = input.Avg12MonthSalesAmount.HasValue &&
            ordered.All(v => v >= input.Avg12MonthSalesAmount.Value);

        if (upward)
        {
            positiveDrivers.Add("روند ۳ ماهه فروش صعودی است");
            return 85m;
        }

        if (consistentlyAboveAverage)
        {
            positiveDrivers.Add("فروش چند ماه اخیر بالاتر از میانگین ۱۲ ماهه است");
            return 75m;
        }

        if (previous.HasValue && input.Avg12MonthSalesAmount.HasValue && current < previous.Value && current < input.Avg12MonthSalesAmount.Value)
        {
            negativeDrivers.Add("فروش ماه جاری نسبت به ماه قبل و میانگین ۱۲ ماهه پایین‌تر است");
            return 30m;
        }

        if (ordered.Count == 3 && ordered[2] > ordered[1] * 1.75m && ordered[0] < input.Avg12MonthSalesAmount)
        {
            negativeDrivers.Add("بخشی از بهبود فروش شبیه جهش یک‌ماهه است");
            return 55m;
        }

        return 55m;
    }

    private static decimal? CalculateIndustryRelativeStrength(
        MonthlySalesQualityScoreInput input,
        List<string> positiveDrivers,
        List<string> negativeDrivers)
    {
        if (!input.IndustryPercentile.HasValue || input.IndustryPeerCount < 5) return null;

        var score = Clamp(input.IndustryPercentile.Value * 100m);
        if (input.IndustryPercentile.Value >= 0.75m)
            positiveDrivers.Add("شرکت در چارک بالای صنعت قرار دارد");
        else if (input.IndustryPercentile.Value <= 0.25m)
            negativeDrivers.Add("شرکت در چارک پایین صنعت قرار دارد");

        return score;
    }

    private static decimal WeightedAverageReweighted(IReadOnlyList<(decimal? Score, decimal Weight)> dimensions)
    {
        var available = dimensions.Where(d => d.Score.HasValue).ToList();
        if (available.Count == 0) return 0m;

        var weightedSum = available.Sum(d => d.Score!.Value * d.Weight);
        var totalWeight = available.Sum(d => d.Weight);
        return Clamp(decimal.Round(weightedSum / totalWeight, 2));
    }

    private static decimal CalculateConfidence(
        MonthlySalesQualityScoreInput input,
        bool suspiciousRateSpike,
        bool hasAvg12M,
        bool hasMom,
        bool hasYoy)
    {
        var score = 0m;
        score += input.HistoryMonths >= 12 ? 20m : input.HistoryMonths >= 6 ? 10m : 0m;
        if (input.HasProductLineItems) score += 20m;
        if (input.ProductMixRows.Count > 0) score += 15m;
        if (hasAvg12M) score += 10m;
        if (hasMom) score += 10m;
        if (hasYoy) score += 10m;
        if (input.IndustryPeerCount >= 5) score += 10m;
        if (!suspiciousRateSpike) score += 5m;
        return Clamp(score);
    }

    private static void AddSalesGrowthDrivers(
        decimal? salesVsAvg,
        List<string> positiveDrivers,
        List<string> negativeDrivers)
    {
        if (!salesVsAvg.HasValue) return;

        if (salesVsAvg.Value >= 20m)
            positiveDrivers.Add("فروش ماهانه بالاتر از میانگین ۱۲ ماهه است");
        else if (salesVsAvg.Value <= -20m)
            negativeDrivers.Add("فروش ماهانه پایین‌تر از میانگین ۱۲ ماهه است");
    }

    private static string GetQualityLabel(decimal score) => score switch
    {
        >= 85m => "گزارش بسیار قوی",
        >= 70m => "گزارش قوی",
        >= 55m => "گزارش متوسط رو به خوب",
        >= 40m => "گزارش متوسط/خنثی",
        >= 25m => "گزارش ضعیف",
        _ => "گزارش بسیار ضعیف یا دیتای ناکافی"
    };

    private static decimal Clamp(decimal value) => Math.Clamp(value, 0m, 100m);
}
