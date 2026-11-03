namespace FinancialCopilot.Application.FinancialData.Ingestion;

public static class MonthlyProductComparisonNormalizer
{
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        var s = value.Trim().Normalize();
        s = s.Replace('ي', 'ی').Replace('ى', 'ی').Replace('ك', 'ک').Replace('\u200c', ' ');
        s = s.Replace('٠','0').Replace('١','1').Replace('٢','2').Replace('٣','3').Replace('٤','4').Replace('٥','5').Replace('٦','6').Replace('٧','7').Replace('٨','8').Replace('٩','9');
        var chars = s.Where(c => !char.IsPunctuation(c) && !char.IsWhiteSpace(c) || c == ' ').ToArray();
        return string.Join(' ', new string(chars).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }
    public static bool IsValidCode(string? code) { var x = Normalize(code); return x.Length > 0 && x.Trim('0').Length > 0; }
    public static string Unit(string? unit) => Normalize(unit).Replace("کیلوگرم", "کیلو").Replace("کیلو گرم", "کیلو").Replace("تن", "تن");
}

public static class MonthlyProductComparisonCalculator
{
    public static MonthlyProductComparisonResponse Calculate(string companyText, MonthlyProductComparisonPeriod current, MonthlyProductComparisonPeriod comparison)
    {
        var all = current.Observations.Concat(comparison.Observations).ToArray();
        var warnings = new HashSet<MonthlyProductComparisonWarning>();
        decimal Sum(IEnumerable<ProductSalesObservation> rows) => rows.Where(x => x.SalesAmount.HasValue).Sum(x => x.SalesAmount!.Value);
        var curTotal = Sum(current.Observations); var baseTotal = Sum(comparison.Observations); var change = curTotal - baseTotal;
        if (baseTotal == 0) warnings.Add(MonthlyProductComparisonWarning.ZeroCompanyRevenueChange);
        var totals = new CompanySalesTotals(curTotal, baseTotal, change, baseTotal == 0 ? null : change / baseTotal * 100m);
        var curGroups = Group(current.Observations, warnings); var baseGroups = Group(comparison.Observations, warnings);
        ReconcileUnmatchedCodeRowsByTitleAndUnit(curGroups, baseGroups);
        MarkUnitConflicts(curGroups, baseGroups);
        var keys = curGroups.Keys.Union(baseGroups.Keys).OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var products = new List<ProductComparisonItem>();
        foreach (var key in keys)
        {
            curGroups.TryGetValue(key, out var c); baseGroups.TryGetValue(key, out var b);
            var identity = c?.Identity ?? b!.Identity; var lifecycle = c is null ? ProductLifecycle.Discontinued : b is null ? ProductLifecycle.New : ProductLifecycle.Continuing;
            var salesChange = c?.Sales.SalesAmount - b?.Sales.SalesAmount;
            if (c is null) salesChange = -(b?.Sales.SalesAmount);
            var itemWarnings = (c?.Warnings ?? []).Concat(b?.Warnings ?? []).Distinct().ToArray(); foreach (var w in itemWarnings) warnings.Add(w);
            itemWarnings = itemWarnings.Concat(Validate(c?.Sales)).Concat(Validate(b?.Sales)).Distinct().ToArray();
            foreach (var w in itemWarnings) warnings.Add(w);
            decimal? q = null, p = null, residual = null; var driver = ProductDriver.Unclassified; var signal = ProductionSalesSignal.Unavailable; decimal? diff = null;
            if (lifecycle == ProductLifecycle.Continuing && itemWarnings.Length == 0 && CanDecompose(c!.Sales, b!.Sales))
            {
                q = (c.Sales.SalesQuantity!.Value - b.Sales.SalesQuantity!.Value) * b.Sales.SalesRate!.Value;
                p = (c.Sales.SalesRate!.Value - b.Sales.SalesRate!.Value) * c.Sales.SalesQuantity!.Value;
                residual = salesChange - q - p;
                var denom = Math.Abs(q.Value) + Math.Abs(p.Value); driver = denom == 0 ? ProductDriver.Unclassified : Math.Abs(q.Value) / denom >= .60m ? ProductDriver.QuantityDriven : Math.Abs(p.Value) / denom >= .60m ? ProductDriver.PriceDriven : ProductDriver.Mixed;
                if (CompatibleUnit(c.Sales.Unit, b.Sales.Unit) && c.Sales.ProductionQuantity.HasValue && b.Sales.ProductionQuantity.HasValue)
                { diff = (c.Sales.ProductionQuantity - b.Sales.ProductionQuantity) - (c.Sales.SalesQuantity - b.Sales.SalesQuantity); signal = Math.Abs(diff.Value) == 0 ? ProductionSalesSignal.NoMaterialDifference : diff > 0 ? ProductionSalesSignal.ProductionAboveSales : ProductionSalesSignal.SalesAboveProduction; }
            }
            else if (lifecycle == ProductLifecycle.Continuing) warnings.Add(MonthlyProductComparisonWarning.PartialDecomposition);
            products.Add(new ProductComparisonItem(c?.DisplayTitle ?? b!.DisplayTitle, c?.Sales.Unit ?? b?.Sales.Unit, key, identity, lifecycle, c?.Sales, b?.Sales, salesChange, change == 0 ? null : salesChange / change * 100m, q, p, residual, driver, signal, diff, itemWarnings, (c?.Evidence ?? []).Concat(b?.Evidence ?? []).ToArray()));
        }
        var ordered = products.OrderByDescending(x => x.SalesChange ?? decimal.MinValue).ThenBy(x => x.NormalizedKey, StringComparer.Ordinal).ToArray();
        var totalQ = products.Sum(x => x.QuantityEffect.HasValue ? Math.Abs(x.QuantityEffect.Value) : 0m);
        var totalP = products.Sum(x => x.PriceEffect.HasValue ? Math.Abs(x.PriceEffect.Value) : 0m);
        var primary = totalQ + totalP == 0 ? ProductDriver.Unclassified : totalQ / (totalQ + totalP) >= .60m ? ProductDriver.QuantityDriven : totalP / (totalQ + totalP) >= .60m ? ProductDriver.PriceDriven : ProductDriver.Mixed;
        if (!string.IsNullOrWhiteSpace(companyText)) { /* companyText is retained as evidence; product filtering belongs to the use-case contract */ }
        var state = warnings.Count == 0 ? MonthlyProductComparisonState.Available : MonthlyProductComparisonState.Partial;
        return new(state, companyText, current.Observations.FirstOrDefault()?.ExternalCompanyId, current.Period, comparison.Period, totals, primary, ordered.FirstOrDefault(x => x.SalesChange > 0), ordered.LastOrDefault(x => x.SalesChange < 0), products, warnings.ToArray(), current.Evidence.Concat(comparison.Evidence).ToArray());
    }

    private sealed record Grouped(string DisplayTitle, ProductPeriodValues Sales, ProductIdentityState Identity, IReadOnlyCollection<MonthlyProductComparisonWarning> Warnings, IReadOnlyCollection<MonthlyProductComparisonEvidence> Evidence);
    private static Dictionary<string, Grouped> Group(IEnumerable<ProductSalesObservation> source, HashSet<MonthlyProductComparisonWarning> global)
    {
        var result = new Dictionary<string, Grouped>(StringComparer.Ordinal);
        foreach (var row in source)
        {
            if (!row.SalesAmount.HasValue) { global.Add(MonthlyProductComparisonWarning.InvalidSalesAmount); continue; }
            var code = MonthlyProductComparisonNormalizer.IsValidCode(row.ProductCode) ? "C:" + MonthlyProductComparisonNormalizer.Normalize(row.ProductCode) + "|U:" + MonthlyProductComparisonNormalizer.Unit(row.Unit) : "T:" + MonthlyProductComparisonNormalizer.Normalize(row.Title) + "|U:" + MonthlyProductComparisonNormalizer.Unit(row.Unit);
            if (code == "T:|U:") code = "ROW:" + row.RowId;
            var item = new Grouped(row.Title?.Trim() ?? "بدون عنوان", new(row.SalesAmount, row.ProductionQuantity, row.SalesQuantity, row.SalesRate, row.Unit), code.StartsWith("C:") ? ProductIdentityState.Code : ProductIdentityState.TitleAndUnit, Array.Empty<MonthlyProductComparisonWarning>(), [new(row.ReportId,row.RowId,row.ProviderName,row.ExternalReportId,row.Period)]);
            if (result.TryGetValue(code, out var old))
            {
                global.Add(MonthlyProductComparisonWarning.PossibleDuplicateRows);
                result[code] = old with
                {
                    Warnings = old.Warnings.Append(MonthlyProductComparisonWarning.PossibleDuplicateRows).ToArray(),
                    Sales = new((old.Sales.SalesAmount ?? 0) + (item.Sales.SalesAmount ?? 0), old.Sales.ProductionQuantity + item.Sales.ProductionQuantity, old.Sales.SalesQuantity + item.Sales.SalesQuantity, old.Sales.SalesRate ?? item.Sales.SalesRate, old.Sales.Unit ?? item.Sales.Unit),
                    Evidence = old.Evidence.Concat(item.Evidence).ToArray()
                };
            }
            else result[code] = item;
        } return result;
    }

    private static void ReconcileUnmatchedCodeRowsByTitleAndUnit(
        Dictionary<string, Grouped> current,
        Dictionary<string, Grouped> comparison)
    {
        // Provider product codes are preferred when they are stable. When a code
        // changes between reports, an unmatched row may still be the same product;
        // use normalized title + unit as the cross-period fallback identity.
        foreach (var currentKey in current.Keys.Where(key => !comparison.ContainsKey(key)).ToArray())
        {
            var currentItem = current[currentKey];
            var normalizedTitle = MonthlyProductComparisonNormalizer.Normalize(currentItem.DisplayTitle);
            if (string.IsNullOrWhiteSpace(normalizedTitle) ||
                string.Equals(normalizedTitle, "Product", StringComparison.OrdinalIgnoreCase))
                continue;
            var candidates = comparison
                .Where(pair => !current.ContainsKey(pair.Key) &&
                               TitleAndUnitKey(pair.Value) == TitleAndUnitKey(currentItem))
                .Select(pair => pair.Key)
                .ToArray();
            if (candidates.Length == 1)
            {
                var comparisonKey = candidates[0];
                current.Remove(currentKey);
                current[comparisonKey] = currentItem;
            }
        }
    }

    private static string TitleAndUnitKey(Grouped item) =>
        "T:" + MonthlyProductComparisonNormalizer.Normalize(item.DisplayTitle) +
        "|U:" + MonthlyProductComparisonNormalizer.Unit(item.Sales.Unit);

    private static IEnumerable<MonthlyProductComparisonWarning> Validate(ProductPeriodValues? value)
    {
        if (value is null) yield break;
        if (!value.SalesRate.HasValue) yield return MonthlyProductComparisonWarning.MissingRate;
        if (value.SalesQuantity is < 0) yield return MonthlyProductComparisonWarning.InvalidQuantity;
        if (value.SalesRate == 0 && value.SalesQuantity is not null and not 0) yield return MonthlyProductComparisonWarning.PartialDecomposition;
    }
    private static void MarkUnitConflicts(Dictionary<string, Grouped> current, Dictionary<string, Grouped> comparison)
    {
        foreach (var left in current.Keys.Where(x => x.StartsWith("C:", StringComparison.Ordinal)))
            foreach (var right in comparison.Keys.Where(x => x.StartsWith(left[..left.IndexOf("|U:", StringComparison.Ordinal)], StringComparison.Ordinal)))
                if (MonthlyProductComparisonNormalizer.Unit(current[left].Sales.Unit) != MonthlyProductComparisonNormalizer.Unit(comparison[right].Sales.Unit))
                {
                    current[left] = current[left] with { Warnings = current[left].Warnings.Append(MonthlyProductComparisonWarning.UnitChanged).ToArray() };
                    comparison[right] = comparison[right] with { Warnings = comparison[right].Warnings.Append(MonthlyProductComparisonWarning.UnitChanged).ToArray() };
                }
    }
    private static bool CompatibleUnit(string? a, string? b) => MonthlyProductComparisonNormalizer.Unit(a) == MonthlyProductComparisonNormalizer.Unit(b) && !string.IsNullOrEmpty(MonthlyProductComparisonNormalizer.Unit(a));
    private static bool CanDecompose(ProductPeriodValues a, ProductPeriodValues b) => a.SalesQuantity.HasValue && b.SalesQuantity.HasValue && a.SalesRate.HasValue && b.SalesRate.HasValue && a.SalesRate != 0 && b.SalesRate != 0 && a.SalesQuantity >= 0 && b.SalesQuantity >= 0 && CompatibleUnit(a.Unit, b.Unit);
}
