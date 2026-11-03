using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

internal sealed class MonthlyProductComparisonUseCase(ICompanyResolverService resolver, IMonthlyProductComparisonReadRepository repository) : IMonthlyProductComparisonUseCase
{
    public async Task<MonthlyProductComparisonResponse> ExecuteAsync(MonthlyProductComparisonQuery query, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(query.CompanyText)) return Block(query, MonthlyProductComparisonBlockingReason.CompanyNotFound, "لطفاً نام یا نماد معتبر شرکت را وارد کنید.");
        if (query.Focus is not MonthlyProductComparisonFocus.All and not MonthlyProductComparisonFocus.Sales and not MonthlyProductComparisonFocus.Production and not MonthlyProductComparisonFocus.Quantity and not MonthlyProductComparisonFocus.Rate)
            return new(MonthlyProductComparisonState.Unavailable, query.CompanyText, null, query.CurrentPeriod, query.ComparisonPeriod, null, null, null, null, [], [], [], MonthlyProductComparisonBlockingReason.InvalidPeriod, "این نوع تمرکز پشتیبانی نمی‌شود.");
        var company = await resolver.ResolveBySymbolAsync(query.CompanyText, ct);
        if (company is null) return Block(query, MonthlyProductComparisonBlockingReason.CompanyNotFound, "شرکت یا نماد موردنظر یافت نشد.");
        var available = await repository.GetAvailablePeriodsAsync(company.ExternalCompanyId, ct);
        if (available.Count == 0) return Block(query, MonthlyProductComparisonBlockingReason.NoMonthlyProductData, "داده ماهانه تولید و فروش برای این شرکت موجود نیست.", company.ExternalCompanyId);
        var current = query.CurrentPeriod ?? available[0];
        if (query.CurrentPeriod is not null && !available.Contains(current)) return Block(query, MonthlyProductComparisonBlockingReason.CurrentPeriodNotFound, $"دوره {current} در داده‌های ذخیره‌شده موجود نیست.", company.ExternalCompanyId);
        var comparison = query.ComparisonPeriod ?? available.Where(x => x < current).OrderByDescending(x => x.Year).ThenByDescending(x => x.Month).FirstOrDefault();
        if (comparison == default) return Block(query, MonthlyProductComparisonBlockingReason.ComparisonPeriodNotFound, "دوره مقایسه‌ای قبلی موجود نیست.", company.ExternalCompanyId);
        if (query.ComparisonPeriod is not null && !available.Contains(comparison)) return Block(query, MonthlyProductComparisonBlockingReason.ComparisonPeriodNotFound, $"دوره {comparison} در داده‌های ذخیره‌شده موجود نیست.", company.ExternalCompanyId);
        if (current == comparison) return Block(query, MonthlyProductComparisonBlockingReason.EqualPeriods, "دو دوره برای مقایسه باید متفاوت باشند.", company.ExternalCompanyId);
        var currentData = await repository.GetPeriodAsync(company.ExternalCompanyId, current, ct);
        var comparisonData = await repository.GetPeriodAsync(company.ExternalCompanyId, comparison, ct);
        if (currentData is null) return Block(query, MonthlyProductComparisonBlockingReason.CurrentPeriodNotFound, $"دوره {current} قابل دسترس نیست.", company.ExternalCompanyId);
        if (comparisonData is null) return Block(query, MonthlyProductComparisonBlockingReason.ComparisonPeriodNotFound, $"دوره {comparison} قابل دسترس نیست.", company.ExternalCompanyId);
        var result = MonthlyProductComparisonCalculator.Calculate(query.CompanyText, currentData, comparisonData);
        return result with { ExternalCompanyId = company.ExternalCompanyId };
    }

    private static MonthlyProductComparisonResponse Block(MonthlyProductComparisonQuery query, MonthlyProductComparisonBlockingReason reason, string message, string? companyId = null) =>
        new(MonthlyProductComparisonState.Unavailable, query.CompanyText, companyId, query.CurrentPeriod, query.ComparisonPeriod, null, null, null, null, [], [], [], reason, message);
}
