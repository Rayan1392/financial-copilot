using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

internal sealed class MonthlyProductTrendQueryUseCase(
    ICompanyResolverService companyResolver,
    IMonthlyProductComparisonReadRepository repository) : IMonthlyProductTrendQueryUseCase
{
    public async Task<MonthlyProductTrendResult> ExecuteAsync(
        MonthlyProductTrendQuery query,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query.CompanyText) || string.IsNullOrWhiteSpace(query.ProductText))
            return NotFound(query, "company_or_product_missing");

        var company = await companyResolver.ResolveBySymbolAsync(query.CompanyText, ct);
        if (company is null) return NotFound(query, "company_not_found");

        var available = await repository.GetAvailablePeriodsAsync(company.ExternalCompanyId, ct);
        if (available.Count == 0) return NotFound(query, "no_qualifying_product_sales");

        var periods = new List<MonthlyProductComparisonPeriod>();
        foreach (var period in available.OrderByDescending(x => x.Year).ThenByDescending(x => x.Month))
        {
            var data = await repository.GetPeriodAsync(company.ExternalCompanyId, period, ct);
            if (data is not null) periods.Add(data);
        }

        var candidates = periods
            .SelectMany(period => period.Observations)
            .Select(row => ToCandidate(company.ExternalCompanyId, row))
            .GroupBy(candidate => candidate.ProductKey, StringComparer.Ordinal)
            .Select(group => group
                .OrderBy(candidate => candidate.DisplayTitle, StringComparer.Ordinal)
                .ThenBy(candidate => candidate.Unit, StringComparer.Ordinal)
                .First())
            .OrderBy(candidate => candidate.DisplayTitle, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.ProductKey, StringComparer.Ordinal)
            .ToArray();

        var requested = MonthlyProductTrendCalculator.NormalizeProductText(query.ProductText);
        var matches = ResolveMatches(candidates, requested);
        if (matches.Length == 0)
            return NotFound(query, "product_not_found", company, candidates);
        if (matches.Length > 1)
            return new(
                MonthlyProductTrendResult.Discriminator,
                MonthlyProductTrendResult.CurrentVersion,
                MonthlyProductTrendResolutionState.Ambiguous,
                query.CompanyText,
                company.ExternalCompanyId,
                company.CompanySymbol ?? company.Ticker,
                company.TseSymbol ?? company.Ticker ?? company.CompanySymbol,
                null, null, null, null, null,
                [], matches.Take(8).ToArray(), [],
                "ambiguous_product",
                "بیش از یک محصول با این مشخصات یافت شد.");

        var selected = matches[0];
        var selectedRows = periods
            .Select(period => (period, rows: period.Observations
                .Where(row => string.Equals(ProductKey(company.ExternalCompanyId, row), selected.ProductKey, StringComparison.Ordinal))
                .ToArray()))
            .ToDictionary(item => item.period.Period, item => item.rows);

        var latestValid = selectedRows
            .Where(item => item.Value.Any(row => row.SalesAmount.HasValue))
            .Select(item => item.Key)
            .OrderByDescending(x => x.Year).ThenByDescending(x => x.Month)
            .FirstOrDefault();

        var window = ResolveWindow(query, latestValid);
        if (window.Count == 0 || latestValid == default)
            return NotFound(query, "product_not_reported", company, [selected]);

        var points = new List<MonthlyProductTrendPoint>(window.Count);
        foreach (var period in window)
        {
            selectedRows.TryGetValue(period, out var rows);
            rows ??= Array.Empty<ProductSalesObservation>();
            points.Add(BuildPoint(period, selected, rows));
        }

        var evidence = points.SelectMany(point => point.Evidence).Distinct().ToArray();
        return new(
            MonthlyProductTrendResult.Discriminator,
            MonthlyProductTrendResult.CurrentVersion,
            MonthlyProductTrendResolutionState.Resolved,
            query.CompanyText,
            company.ExternalCompanyId,
            company.CompanySymbol ?? company.Ticker,
            company.TseSymbol ?? company.Ticker ?? company.CompanySymbol,
            selected.ProductKey,
            selected.ProviderProductCode,
            selected.ProviderProductId,
            selected.DisplayTitle,
            selected.Unit,
            points,
            [selected],
            evidence,
            Message: "روند فروش ماهانه محصول آماده است.");
    }

    private static MonthlyProductTrendPoint BuildPoint(
        JalaliPeriod period,
        MonthlyProductTrendCandidate candidate,
        IReadOnlyCollection<ProductSalesObservation> rows)
    {
        if (rows.Count == 0)
            return new(period, period.ToString(), candidate.ProductKey, candidate.DisplayTitle, candidate.Unit,
                null, null, null, null, null, MonthlyProductTrendRateStatus.UnavailableRate, [], true);

        var units = rows
            .Select(row => MonthlyProductComparisonNormalizer.Unit(row.Unit))
            .Where(unit => unit.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (units.Length > 1)
            return new(period, period.ToString(), candidate.ProductKey, candidate.DisplayTitle, candidate.Unit,
                null, null, null, null, null, MonthlyProductTrendRateStatus.UnavailableRate,
                rows.SelectMany(row => new[] { new MonthlyProductComparisonEvidence(
                    row.ReportId, row.RowId, row.ProviderName, row.ExternalReportId, row.Period) }).Distinct().ToArray());

        if (!TrySumNullable(rows.Select(row => row.ProductionQuantity), out var production) ||
            !TrySumNullable(rows.Select(row => row.SalesQuantity), out var quantity) ||
            !TrySumNullable(rows.Select(row => row.SalesAmount), out var value))
        {
            return new(period, period.ToString(), candidate.ProductKey, candidate.DisplayTitle, candidate.Unit,
                null, null, null, null, null, MonthlyProductTrendRateStatus.Overflow,
                rows.SelectMany(row => new[] { new MonthlyProductComparisonEvidence(
                    row.ReportId, row.RowId, row.ProviderName, row.ExternalReportId, row.Period) }).Distinct().ToArray());
        }
        var (rate, status) = MonthlyProductTrendCalculator.CalculateRate(value, quantity);
        decimal? billionToman = null;
        if (value.HasValue)
        {
            try { billionToman = checked(value.Value * 0.0001m); }
            catch (OverflowException) { status = MonthlyProductTrendRateStatus.Overflow; }
        }

        return new(
            period,
            period.ToString(),
            candidate.ProductKey,
            candidate.DisplayTitle,
            candidate.Unit,
            production,
            quantity,
            value,
            billionToman,
            rate,
            status,
            rows.SelectMany(row => new[] { new MonthlyProductComparisonEvidence(
                row.ReportId, row.RowId, row.ProviderName, row.ExternalReportId, row.Period) }).Distinct().ToArray());
    }

    private static bool TrySumNullable(IEnumerable<decimal?> values, out decimal? sum)
    {
        var materialized = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        if (materialized.Length == 0)
        {
            sum = null;
            return true;
        }

        try
        {
            decimal total = 0m;
            foreach (var value in materialized) total = checked(total + value);
            sum = total;
            return true;
        }
        catch (OverflowException)
        {
            sum = null;
            return false;
        }
    }

    private static MonthlyProductTrendCandidate[] ResolveMatches(
        IReadOnlyCollection<MonthlyProductTrendCandidate> candidates,
        string requested)
    {
        if (string.IsNullOrWhiteSpace(requested)) return [];

        // Stable provider identity is the strongest match. If more than one
        // candidate shares it, keep all of them so the caller preserves the
        // existing ambiguity response.
        var providerIdentityMatches = candidates
            .Where(candidate =>
                string.Equals(
                    MonthlyProductTrendCalculator.NormalizeProductText(candidate.ProviderProductCode),
                    requested,
                    StringComparison.OrdinalIgnoreCase) ||
                string.Equals(
                    candidate.ProviderProductId is > 0 ? candidate.ProviderProductId.Value.ToString() : null,
                    requested,
                    StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (providerIdentityMatches.Length > 0)
            return providerIdentityMatches;

        // An exact normalized title wins over every longer title that merely
        // contains it. Distinct ProductKeys with the same exact title remain
        // in this tier and therefore remain ambiguous.
        var exactTitleMatches = candidates
            .Where(candidate => string.Equals(
                MonthlyProductTrendCalculator.NormalizeProductText(candidate.DisplayTitle),
                requested,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (exactTitleMatches.Length > 0)
            return exactTitleMatches;

        // Partial matching is only a fallback when no stronger match exists.
        return candidates
            .Where(candidate => MonthlyProductTrendCalculator
                .NormalizeProductText(candidate.DisplayTitle)
                .Contains(requested, StringComparison.OrdinalIgnoreCase))
            .ToArray();
    }

    private static MonthlyProductTrendCandidate ToCandidate(string companyId, ProductSalesObservation row) =>
        new(row.Title?.Trim() is { Length: > 0 } title ? title : "بدون عنوان", row.Unit,
            ProductKey(companyId, row), row.ProviderProductCode, row.ProviderProductId);

    private static string ProductKey(string companyId, ProductSalesObservation row) =>
        row.ProductKey ?? MonthlyProductTrendCalculator.ProductKey(companyId, row);

    private static IReadOnlyList<JalaliPeriod> ResolveWindow(MonthlyProductTrendQuery query, JalaliPeriod latestValid)
    {
        if (query.FromPeriod is not null || query.ToPeriod is not null)
        {
            var from = query.FromPeriod ?? query.ToPeriod ?? latestValid;
            var to = query.ToPeriod ?? query.FromPeriod ?? latestValid;
            if (from > to) (from, to) = (to, from);
            return Enumerate(from, to);
        }

        var result = new List<JalaliPeriod>(12);
        var current = latestValid;
        for (var index = 0; index < 12; index++)
        {
            result.Add(current);
            current = Previous(current);
        }
        result.Reverse();
        return result;
    }

    private static IReadOnlyList<JalaliPeriod> Enumerate(JalaliPeriod from, JalaliPeriod to)
    {
        var result = new List<JalaliPeriod>();
        for (var current = from; !(current > to); current = Next(current)) result.Add(current);
        return result;
    }

    private static JalaliPeriod Previous(JalaliPeriod period) => period.Month == 1
        ? new(period.Year - 1, 12)
        : new(period.Year, period.Month - 1);

    private static JalaliPeriod Next(JalaliPeriod period) => period.Month == 12
        ? new(period.Year + 1, 1)
        : new(period.Year, period.Month + 1);

    private static MonthlyProductTrendResult NotFound(
        MonthlyProductTrendQuery query,
        string reason,
        ResolvedCompany? company = null,
        IReadOnlyList<MonthlyProductTrendCandidate>? candidates = null) =>
        new(
            MonthlyProductTrendResult.Discriminator,
            MonthlyProductTrendResult.CurrentVersion,
            MonthlyProductTrendResolutionState.NotFound,
            query.CompanyText,
            company?.ExternalCompanyId,
            company?.CompanySymbol,
            company?.TseSymbol,
            null, null, null, null, null, [], candidates ?? [], [], reason,
            "داده واجد شرایطی برای محصول در بازه درخواستی یافت نشد.");
}
