namespace FinancialCopilot.Application.FinancialData.Semantics.Navigation;

public sealed record ProducerSalesQueryIntent(bool IsProducerSalesRanking, PopulationSalesScope? Scope,
    string? MeasureCatalogId, bool RequiresClarification);

/// <summary>Recognizes only explicit producer-sales ranking scope. It never supplies a company registry.</summary>
public sealed class ProducerSalesQueryIntentClassifier
{
    public ProducerSalesQueryIntent Classify(string question, string productReference)
    {
        var text = Normalize(question);
        var product = Normalize(productReference);
        var mentionsProduction = text.Contains("تولید", StringComparison.Ordinal)
            || text.Contains("توليد", StringComparison.Ordinal);
        var mentionsSales = text.Contains("فروش", StringComparison.Ordinal);
        var mentionsRanking = text.Contains("بیشترین", StringComparison.Ordinal)
            || text.Contains("بالاترین", StringComparison.Ordinal)
            || text.Contains("رتبه", StringComparison.Ordinal)
            || text.Contains("رتبه‌بندی", StringComparison.Ordinal);
        if (!mentionsProduction || !mentionsSales || !mentionsRanking)
            return new(false, null, null, false);

        var explicitTotalSales = ContainsAny(text, "کل مبلغ فروش شرکت", "مبلغ کل فروش شرکت", "مجموع مبلغ فروش شرکت",
            "کل فروش ماهانه شرکت", "مبلغ کل فروش ماهانه شرکت", "فروش کل ماهانه", "فروش کل شرکت");
        var explicitlyMentionsProduct = product.Length > 0 && text.Contains(product, StringComparison.Ordinal);
        var explicitProductSales = explicitlyMentionsProduct
            && (text.Contains("مقدار فروش", StringComparison.Ordinal)
                || text.Contains("فروش " + product, StringComparison.Ordinal)
                || text.Contains("فروشِ " + product, StringComparison.Ordinal));

        if (explicitTotalSales)
            return new(true, PopulationSalesScope.TotalCompanySales, "metric_monthly_sales", false);
        if (explicitProductSales)
            return new(true, PopulationSalesScope.RequestedProductSales,
                text.Contains("مقدار فروش", StringComparison.Ordinal)
                    ? "metric_monthly_sales_quantity" : "metric_monthly_sales", false);
        return new(true, null, null, true);
    }

    private static bool ContainsAny(string value, params string[] phrases) =>
        phrases.Any(phrase => value.Contains(phrase, StringComparison.Ordinal));

    private static string Normalize(string value) => (value ?? string.Empty).Normalize()
        .Replace('\u064a', '\u06cc').Replace('\u0643', '\u06a9')
        .Replace('\u200c', ' ').Replace('\u200f', ' ').Trim();
}
