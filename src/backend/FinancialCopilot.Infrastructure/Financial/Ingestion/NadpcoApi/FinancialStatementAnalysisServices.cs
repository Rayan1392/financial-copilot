using System.Globalization;
using System.Text;
using System.Text.Json;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Domain.Financial.Entities;
using FinancialCopilot.Infrastructure.Financial.Ingestion.Persistence;
using Microsoft.EntityFrameworkCore;

namespace FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi;

internal sealed class EfCoreFinancialStatementAnalysisRepository(FinancialIngestionDbContext dbContext)
    : IFinancialStatementAnalysisRepository
{
    private static readonly string[] AllowedProviders = ["NadpcoApi", "NoavaranCurrentApi"];

    public async Task<IReadOnlyList<FinancialStatementAnalysisStatementSnapshot>> ListCompanyStatementsAsync(
        string externalCompanyId,
        CancellationToken ct = default)
    {
        var rows = await dbContext.FinancialStatements
            .AsNoTracking()
            .Where(row => row.ExternalCompanyId == externalCompanyId &&
                          AllowedProviders.Contains(row.ProviderName))
            .OrderByDescending(row => row.PeriodEnd)
            .ToListAsync(ct);

        if (rows.Count == 0)
            return [];

        var company = await dbContext.Companies
            .AsNoTracking()
            .Where(row => row.ExternalCompanyId == externalCompanyId &&
                          AllowedProviders.Contains(row.ProviderName))
            .OrderByDescending(row => row.LastSynchronizedAt)
            .FirstOrDefaultAsync(ct);

        var statementIds = rows.Select(row => row.Id).ToArray();
        var lineItems = await dbContext.FinancialStatementLineItems
            .AsNoTracking()
            .Where(item => statementIds.Contains(item.FinancialStatementId))
            .ToListAsync(ct);

        var lineItemLookup = lineItems
            .GroupBy(item => item.FinancialStatementId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyDictionary<string, decimal?>)group.ToDictionary(
                    item => item.MetricCode,
                    item => item.Value,
                    StringComparer.OrdinalIgnoreCase));

        return rows.Select(row =>
        {
            var metadata = StatementMetadata.Parse(row.WarningsJson);
            return new FinancialStatementAnalysisStatementSnapshot(
                row.Id,
                row.ExternalStatementId,
                row.ProviderName,
                row.ExternalCompanyId,
                company?.CompanySymbol ?? company?.TseSymbol ?? company?.Ticker,
                company?.Name,
                Enum.Parse<FinancialStatementType>(row.StatementType, ignoreCase: false),
                row.PeriodType,
                ResolvePeriodMonths(row.PeriodType),
                row.PeriodStart,
                row.PeriodEnd,
                metadata.AnnouncementDate,
                metadata.JalaliPeriodEnd,
                metadata.JalaliFiscalYearEnd,
                metadata.JalaliAnnouncementDate,
                metadata.IsAudited,
                metadata.IsRepresented,
                metadata.IsComposing,
                lineItemLookup.TryGetValue(row.Id, out var items)
                    ? items
                    : new Dictionary<string, decimal?>(StringComparer.OrdinalIgnoreCase));
        }).ToArray();
    }

    private static int ResolvePeriodMonths(string periodType) => periodType switch
    {
        "ThreeMonths" => 3,
        "SixMonths" => 6,
        "NineMonths" => 9,
        "TwelveMonths" => 12,
        _ => 0
    };

    private sealed record StatementMetadata(
        string? JalaliPeriodEnd,
        string? JalaliFiscalYearEnd,
        string? JalaliAnnouncementDate,
        DateTimeOffset? AnnouncementDate,
        bool IsAudited,
        bool IsRepresented,
        bool IsComposing)
    {
        public static StatementMetadata Parse(string? warningsJson)
        {
            if (string.IsNullOrWhiteSpace(warningsJson))
                return new StatementMetadata(null, null, null, null, false, false, false);

            try
            {
                using var document = JsonDocument.Parse(warningsJson);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Array)
                    return new StatementMetadata(null, null, null, null, false, false, false);

                string? jalaliPeriodEnd = null;
                string? jalaliFiscalYearEnd = null;
                string? jalaliAnnouncementDate = null;
                DateTimeOffset? announcementDate = null;
                var isAudited = false;
                var isRepresented = false;
                var isComposing = false;

                foreach (var item in root.EnumerateArray())
                {
                    if (!item.TryGetProperty("code", out var codeProp))
                        continue;

                    var code = codeProp.GetString();
                    if (!item.TryGetProperty("evidence", out var evidence))
                        continue;

                    switch (code)
                    {
                        case "JalaliPeriodEnd":
                            jalaliPeriodEnd = evidence.GetString();
                            break;
                        case "JalaliFiscalYearEnd":
                            jalaliFiscalYearEnd = evidence.GetString();
                            break;
                        case "JalaliAnouncementDate":
                        case "JalaliAnnouncementDate":
                            jalaliAnnouncementDate = evidence.GetString();
                            break;
                        case "AnouncementDate":
                        case "AnnouncementDate":
                            if (evidence.ValueKind == JsonValueKind.String &&
                                DateTimeOffset.TryParse(evidence.GetString(), out var parsed))
                            {
                                announcementDate = parsed;
                            }
                            break;
                        case "IsAudited":
                            isAudited = evidence.ValueKind == JsonValueKind.True;
                            break;
                        case "IsRepresented":
                            isRepresented = evidence.ValueKind == JsonValueKind.True;
                            break;
                        case "IsComposing":
                            isComposing = evidence.ValueKind == JsonValueKind.True;
                            break;
                    }
                }

                return new StatementMetadata(
                    jalaliPeriodEnd,
                    jalaliFiscalYearEnd,
                    jalaliAnnouncementDate,
                    announcementDate,
                    isAudited,
                    isRepresented,
                    isComposing);
            }
            catch (JsonException)
            {
                return new StatementMetadata(null, null, null, null, false, false, false);
            }
        }
    }
}

internal sealed class FinancialStatementSelectionService : IFinancialStatementSelectionService
{
    public FinancialStatementSelectionResult Select(
        IReadOnlyList<FinancialStatementAnalysisStatementSnapshot> statements,
        FinancialStatementSelectionRequest request)
    {
        var warnings = new List<string>();
        var variantPreference = request.VariantPreference;

        var income = SelectCurrent(statements, FinancialStatementType.IncomeStatement, request, warnings, allowNearestBalanceSheet: false);
        var priorIncome = income is null ? null : SelectPrior(statements, income, request);
        var balance = SelectCurrent(statements, FinancialStatementType.BalanceSheet, request, warnings, allowNearestBalanceSheet: true, anchor: income);
        var priorBalance = balance is null ? null : SelectPrior(statements, balance, request);

        return new FinancialStatementSelectionResult(income, priorIncome, balance, priorBalance, warnings);
    }

    private static FinancialStatementAnalysisStatementSnapshot? SelectCurrent(
        IReadOnlyList<FinancialStatementAnalysisStatementSnapshot> statements,
        FinancialStatementType type,
        FinancialStatementSelectionRequest request,
        List<string> warnings,
        bool allowNearestBalanceSheet,
        FinancialStatementAnalysisStatementSnapshot? anchor = null)
    {
        var filtered = statements
            .Where(statement => statement.StatementType == type)
            .Where(statement => request.PeriodMonths is null || statement.PeriodMonths == request.PeriodMonths.Value)
            .Where(statement => MatchesVariant(statement, request.VariantPreference))
            .Where(statement => request.IsAuditedPreference is null || statement.IsAudited == request.IsAuditedPreference.Value)
            .ToList();

        if (allowNearestBalanceSheet && anchor is not null)
        {
            var samePeriod = filtered
                .Where(statement => statement.PeriodMonths == anchor.PeriodMonths && statement.PeriodEnd == anchor.PeriodEnd)
                .ToList();
            if (samePeriod.Count > 0)
                filtered = samePeriod;
            else
                filtered = filtered
                    .Where(statement => statement.PeriodEnd <= anchor.PeriodEnd)
                    .OrderByDescending(statement => statement.PeriodEnd)
                    .ThenByDescending(statement => statement.PeriodMonths)
                    .Take(5)
                    .ToList();
        }

        if (filtered.Count == 0)
        {
            if (request.VariantPreference == FinancialStatementVariantPreference.DefaultNonConsolidated &&
                statements.Any(statement => statement.StatementType == type && statement.IsComposing))
            {
                warnings.Add($"صورت {StatementTypeLabel(type)} غیرتلفیقی برای دوره درخواستی یافت نشد و پاسخ به تلفیقی fallback نمی‌کند.");
            }

            return null;
        }

        return filtered
            .OrderByDescending(statement => statement.AnnouncementDate ?? DateTimeOffset.MinValue)
            .ThenByDescending(statement => statement.PeriodEnd)
            .ThenByDescending(statement => statement.PeriodMonths)
            .First();
    }

    private static FinancialStatementAnalysisStatementSnapshot? SelectPrior(
        IReadOnlyList<FinancialStatementAnalysisStatementSnapshot> statements,
        FinancialStatementAnalysisStatementSnapshot current,
        FinancialStatementSelectionRequest request)
    {
        var priorYearEnd = current.PeriodEnd.AddYears(-1);

        return statements
            .Where(statement => statement.StatementType == current.StatementType)
            .Where(statement => statement.PeriodMonths == current.PeriodMonths)
            .Where(statement => statement.PeriodEnd == priorYearEnd)
            .Where(statement => MatchesVariant(statement, request.VariantPreference))
            .Where(statement => request.IsAuditedPreference is null || statement.IsAudited == request.IsAuditedPreference.Value)
            .OrderByDescending(statement => statement.AnnouncementDate ?? DateTimeOffset.MinValue)
            .FirstOrDefault();
    }

    private static bool MatchesVariant(
        FinancialStatementAnalysisStatementSnapshot statement,
        FinancialStatementVariantPreference preference) => preference switch
    {
        FinancialStatementVariantPreference.ConsolidatedOnly => statement.IsComposing,
        FinancialStatementVariantPreference.NonConsolidatedOnly => !statement.IsComposing,
        _ => !statement.IsComposing
    };

    private static string StatementTypeLabel(FinancialStatementType type) => type switch
    {
        FinancialStatementType.IncomeStatement => "سود و زیان",
        FinancialStatementType.BalanceSheet => "ترازنامه",
        FinancialStatementType.CashFlow => "جریان وجوه نقد",
        _ => type.ToString()
    };
}

internal sealed class FinancialStatementAnalysisRenderer : IFinancialStatementAnalysisRenderer
{
    public string Render(FinancialStatementAnalysisResponse response)
    {
        var sb = new StringBuilder();
        foreach (var bullet in response.SummaryBullets)
            sb.AppendLine(bullet);

        foreach (var section in response.Sections.Where(section => section.Metrics.Count > 0))
        {
            sb.AppendLine();
            sb.AppendLine(section.TitleFa + ":");
            foreach (var metric in section.Metrics)
            {
                if (metric.IsUnavailable)
                {
                    sb.AppendLine($"- {metric.LabelFa}: {metric.Warning ?? "داده کافی موجود نیست."}");
                    continue;
                }

                var line = new StringBuilder($"- {metric.LabelFa}: {metric.CurrentFormattedValue}");
                if (!string.IsNullOrWhiteSpace(metric.PreviousFormattedValue))
                    line.Append($" | دوره مقایسه: {metric.PreviousFormattedValue}");
                if (metric.ChangePercent.HasValue)
                    line.Append($" | تغییر: {metric.ChangePercent.Value:+0.##;-0.##;0}%");
                if (!string.IsNullOrWhiteSpace(metric.Indicator))
                    line.Append($" | {metric.Indicator}");
                sb.AppendLine(line.ToString());
            }
        }

        if (response.SourceReferences.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("منبع:");
            foreach (var source in response.SourceReferences)
            {
                var label = source.StatementType switch
                {
                    nameof(FinancialStatementType.IncomeStatement) => "صورت‌های مالی",
                    nameof(FinancialStatementType.BalanceSheet) => "ترازنامه",
                    nameof(FinancialStatementType.CashFlow) => "جریان وجوه نقد",
                    _ => source.StatementType
                };
                var variant = source.IsComposing ? " تلفیقی" : string.Empty;
                var audited = source.IsAudited ? "حسابرسی شده" : "حسابرسی نشده";
                sb.AppendLine($"{label}{variant} {source.PeriodMonths} ماهه منتهی به {source.JalaliPeriodEnd ?? source.PeriodEndLabel()} ({audited})");
                if (!string.IsNullOrWhiteSpace(source.JalaliAnnouncementDate))
                    sb.AppendLine($"زمان انتشار: {source.JalaliAnnouncementDate}");
                sb.AppendLine($"Provider: {source.ProviderName}");
            }
        }

        if (response.Warnings.Count > 0)
        {
            sb.AppendLine();
            foreach (var warning in response.Warnings)
                sb.AppendLine($"هشدار: {warning}");
        }

        return sb.ToString().TrimEnd();
    }
}

internal sealed class FinancialStatementAnalysisUseCase(
    ICompanyResolverService companyResolver,
    IFinancialStatementAnalysisRepository repository,
    IFinancialStatementSelectionService selectionService,
    IFinancialStatementAnalysisRenderer renderer,
    TimeProvider timeProvider)
    : IFinancialStatementAnalysisUseCase
{
    public async Task<FinancialStatementAnalysisResponse?> ExecuteAsync(
        FinancialStatementAnalysisQuery query,
        CancellationToken ct = default)
    {
        var companyHint = query.SymbolOrCompanyName;
        if (string.IsNullOrWhiteSpace(companyHint))
            return null;

        var company = await companyResolver.ResolveBySymbolAsync(companyHint, ct);
        if (company is null)
            return null;

        var statements = await repository.ListCompanyStatementsAsync(company.ExternalCompanyId, ct);
        if (statements.Count == 0)
            return null;

        var selection = selectionService.Select(
            statements,
            new FinancialStatementSelectionRequest(
                query.PeriodMonths,
                query.StatementTypeFocus,
                query.VariantPreference,
                query.IsAuditedPreference));

        var warnings = selection.Warnings.ToList();
        if (selection.IncomeStatement is null && selection.BalanceSheet is null)
            return null;

        var summaryBullets = new List<string>();
        var sections = new List<FinancialStatementAnalysisSection>();
        var metrics = query.MetricFocusCodes ?? [];

        if (selection.IncomeStatement is not null)
        {
            var incomeSection = BuildIncomeSection(selection.IncomeStatement, selection.PriorIncomeStatement, metrics, summaryBullets);
            if (incomeSection.Metrics.Count > 0 || incomeSection.SummaryBullets.Count > 0)
                sections.Add(incomeSection);
        }

        if (selection.BalanceSheet is not null || query.IncludeBalanceSheetSummary || query.IncludeReturnMetrics)
        {
            var balanceSection = BuildBalanceSection(
                selection.IncomeStatement,
                selection.PriorIncomeStatement,
                selection.BalanceSheet,
                selection.PriorBalanceSheet,
                metrics,
                query.IncludeReturnMetrics,
                summaryBullets,
                warnings);
            if (balanceSection.Metrics.Count > 0 || balanceSection.SummaryBullets.Count > 0)
                sections.Add(balanceSection);
        }

        var sourceReferences = new List<FinancialStatementSourceReference>();
        if (selection.IncomeStatement is not null)
            sourceReferences.Add(ToSourceReference(selection.IncomeStatement));
        if (selection.BalanceSheet is not null &&
            sourceReferences.All(source => source.StatementId != selection.BalanceSheet.StatementId))
            sourceReferences.Add(ToSourceReference(selection.BalanceSheet));

        var anchor = selection.IncomeStatement ?? selection.BalanceSheet!;
        var response = new FinancialStatementAnalysisResponse(
            CompanySymbol: anchor.CompanySymbol ?? companyHint,
            CompanyName: anchor.CompanyName,
            SelectedPeriodMonths: anchor.PeriodMonths,
            SelectedPeriodType: anchor.PeriodType,
            JalaliPeriodEnd: anchor.JalaliPeriodEnd,
            JalaliFiscalYearEnd: anchor.JalaliFiscalYearEnd,
            SelectedVariant: anchor.IsComposing ? "Consolidated" : "NonConsolidated",
            SelectedAuditedStatus: anchor.IsAudited,
            SummaryBullets: summaryBullets,
            Sections: sections,
            SourceReferences: sourceReferences,
            Warnings: warnings,
            ConfidenceScore: warnings.Count == 0 ? 0.92 : 0.78,
            RenderedAnswer: null,
            GeneratedAtUtc: timeProvider.GetUtcNow());

        return response with { RenderedAnswer = renderer.Render(response) };
    }

    private static FinancialStatementAnalysisSection BuildIncomeSection(
        FinancialStatementAnalysisStatementSnapshot current,
        FinancialStatementAnalysisStatementSnapshot? previous,
        IReadOnlyList<string> metricFocusCodes,
        List<string> summaryBullets)
    {
        var metrics = new List<FinancialStatementMetricComparison>();
        var focusSet = metricFocusCodes.Count == 0
            ? null
            : new HashSet<string>(metricFocusCodes, StringComparer.OrdinalIgnoreCase);

        AddAmountMetric(metrics, current, previous, "REVENUE", "درآمد عملیاتی", "میلیون ریال", focusSet, summaryBullets);
        AddAmountMetric(metrics, current, previous, "GROSS_PROFIT", "سود/زیان ناخالص", "میلیون ریال", focusSet, summaryBullets);
        AddAmountMetric(metrics, current, previous, "OPERATING_PROFIT", "سود/زیان عملیاتی", "میلیون ریال", focusSet, summaryBullets);
        AddAmountMetric(metrics, current, previous, "NET_PROFIT", "سود/زیان خالص", "میلیون ریال", focusSet, summaryBullets);
        AddAmountMetric(metrics, current, previous, "EPS", "EPS", "ریال", focusSet, summaryBullets);

        AddMarginMetric(metrics, current, previous, "GROSS_PROFIT_MARGIN", "حاشیه سود ناخالص", "GROSS_PROFIT", focusSet);
        AddMarginMetric(metrics, current, previous, "OPERATING_PROFIT_MARGIN", "حاشیه سود عملیاتی", "OPERATING_PROFIT", focusSet);
        AddMarginMetric(metrics, current, previous, "NET_PROFIT_MARGIN", "حاشیه سود خالص", "NET_PROFIT", focusSet);

        return new FinancialStatementAnalysisSection("خلاصه سود و زیان", [], metrics);
    }

    private static FinancialStatementAnalysisSection BuildBalanceSection(
        FinancialStatementAnalysisStatementSnapshot? income,
        FinancialStatementAnalysisStatementSnapshot? priorIncome,
        FinancialStatementAnalysisStatementSnapshot? balance,
        FinancialStatementAnalysisStatementSnapshot? priorBalance,
        IReadOnlyList<string> metricFocusCodes,
        bool includeReturnMetrics,
        List<string> summaryBullets,
        List<string> warnings)
    {
        var metrics = new List<FinancialStatementMetricComparison>();
        var focusSet = metricFocusCodes.Count == 0
            ? null
            : new HashSet<string>(metricFocusCodes, StringComparer.OrdinalIgnoreCase);

        if (balance is null)
        {
            warnings.Add("ترازنامه متناظر برای محاسبه وضعیت مالی و نسبت‌ها یافت نشد.");
            return new FinancialStatementAnalysisSection("خلاصه ترازنامه", [], metrics);
        }

        AddAmountMetric(metrics, balance, priorBalance, "TOTAL_ASSETS", "جمع دارایی‌ها", "میلیون ریال", focusSet, summaryBullets);
        AddAmountMetric(metrics, balance, priorBalance, "TOTAL_LIABILITIES", "جمع بدهی‌ها", "میلیون ریال", focusSet, summaryBullets);
        AddAmountMetric(metrics, balance, priorBalance, "TOTAL_EQUITY", "حقوق صاحبان سهام", "میلیون ریال", focusSet, summaryBullets);
        AddAmountMetric(metrics, balance, priorBalance, "CURRENT_ASSETS", "دارایی‌های جاری", "میلیون ریال", focusSet, summaryBullets, addBullet: false);
        AddAmountMetric(metrics, balance, priorBalance, "CURRENT_LIABILITIES", "بدهی‌های جاری", "میلیون ریال", focusSet, summaryBullets, addBullet: false);

        AddComputedRatio(metrics, balance, priorBalance, "DEBT_RATIO", "نسبت بدهی",
            ComputeRatio(GetValue(balance, "TOTAL_LIABILITIES"), GetValue(balance, "TOTAL_ASSETS")),
            ComputeRatio(GetValue(priorBalance, "TOTAL_LIABILITIES"), GetValue(priorBalance, "TOTAL_ASSETS")),
            focusSet);
        AddComputedRatio(metrics, balance, priorBalance, "CURRENT_RATIO", "نسبت جاری",
            ComputeRatio(GetValue(balance, "CURRENT_ASSETS"), GetValue(balance, "CURRENT_LIABILITIES")),
            ComputeRatio(GetValue(priorBalance, "CURRENT_ASSETS"), GetValue(priorBalance, "CURRENT_LIABILITIES")),
            focusSet);

        if (includeReturnMetrics || (focusSet?.Overlaps(["ROA", "ROE"]) ?? false))
        {
            AddComputedRatio(metrics, balance, priorBalance, "ROA", "بازده دارایی",
                ComputeRatio(GetValue(income, "NET_PROFIT"), GetValue(balance, "TOTAL_ASSETS"), multiplyBy100: true),
                ComputeRatio(GetValue(priorIncome, "NET_PROFIT"), GetValue(priorBalance, "TOTAL_ASSETS"), multiplyBy100: true),
                focusSet,
                suffix: "%");
            AddComputedRatio(metrics, balance, priorBalance, "ROE", "بازده حقوق صاحبان سهام",
                ComputeRatio(GetValue(income, "NET_PROFIT"), GetValue(balance, "TOTAL_EQUITY"), multiplyBy100: true),
                ComputeRatio(GetValue(priorIncome, "NET_PROFIT"), GetValue(priorBalance, "TOTAL_EQUITY"), multiplyBy100: true),
                focusSet,
                suffix: "%");
        }

        return new FinancialStatementAnalysisSection("خلاصه ترازنامه و نسبت‌ها", [], metrics);
    }

    private static void AddAmountMetric(
        List<FinancialStatementMetricComparison> metrics,
        FinancialStatementAnalysisStatementSnapshot current,
        FinancialStatementAnalysisStatementSnapshot? previous,
        string metricCode,
        string labelFa,
        string unit,
        HashSet<string>? focusSet,
        List<string> summaryBullets,
        bool addBullet = true)
    {
        if (focusSet is not null && !focusSet.Contains(metricCode))
            return;

        var currentValue = GetValue(current, metricCode);
        var previousValue = GetValue(previous, metricCode);
        var changePercent = ComputeChangePercent(currentValue, previousValue);
        var indicator = ResolveIndicator(currentValue, previousValue, metricCode);

        metrics.Add(new FinancialStatementMetricComparison(
            metricCode,
            labelFa,
            currentValue,
            FormatAmount(currentValue, unit),
            previousValue,
            FormatAmount(previousValue, unit),
            changePercent,
            changePercent is null ? null : changePercent >= 0 ? "افزایش" : "کاهش",
            indicator,
            IsUnavailable: currentValue is null,
            Warning: currentValue is null ? "داده این قلم در صورت انتخاب‌شده موجود نیست." : null));

        if (addBullet && currentValue.HasValue)
        {
            var bullet = changePercent.HasValue && previousValue.HasValue
                ? $"{indicator} {labelFa} {current.PeriodMonths} ماهه نسبت به دوره مشابه {changePercent.Value:+0.##;-0.##;0}% تغییر کرده و به {FormatAmount(currentValue, unit)} رسیده است."
                : $"⏹️ {labelFa} {current.PeriodMonths} ماهه برابر {FormatAmount(currentValue, unit)} است.";
            summaryBullets.Add(bullet);
        }
    }

    private static void AddMarginMetric(
        List<FinancialStatementMetricComparison> metrics,
        FinancialStatementAnalysisStatementSnapshot current,
        FinancialStatementAnalysisStatementSnapshot? previous,
        string metricCode,
        string labelFa,
        string numeratorMetricCode,
        HashSet<string>? focusSet)
    {
        if (focusSet is not null && !focusSet.Contains(metricCode))
            return;

        var currentValue = ComputeRatio(GetValue(current, numeratorMetricCode), GetValue(current, "REVENUE"), multiplyBy100: true);
        var previousValue = ComputeRatio(GetValue(previous, numeratorMetricCode), GetValue(previous, "REVENUE"), multiplyBy100: true);

        metrics.Add(new FinancialStatementMetricComparison(
            metricCode,
            labelFa,
            currentValue,
            FormatPercent(currentValue),
            previousValue,
            FormatPercent(previousValue),
            ComputeChangePercent(currentValue, previousValue),
            null,
            ResolveIndicator(currentValue, previousValue, metricCode),
            IsUnavailable: currentValue is null,
            Warning: currentValue is null ? "به دلیل نبود درآمد یا قلم سود، این حاشیه قابل محاسبه نیست." : null));
    }

    private static void AddComputedRatio(
        List<FinancialStatementMetricComparison> metrics,
        FinancialStatementAnalysisStatementSnapshot balance,
        FinancialStatementAnalysisStatementSnapshot? priorBalance,
        string metricCode,
        string labelFa,
        decimal? currentValue,
        decimal? previousValue,
        HashSet<string>? focusSet,
        string suffix = "")
    {
        if (focusSet is not null && !focusSet.Contains(metricCode))
            return;

        metrics.Add(new FinancialStatementMetricComparison(
            metricCode,
            labelFa,
            currentValue,
            FormatRatio(currentValue, suffix),
            previousValue,
            FormatRatio(previousValue, suffix),
            ComputeChangePercent(currentValue, previousValue),
            null,
            ResolveIndicator(currentValue, previousValue, metricCode),
            IsUnavailable: currentValue is null,
            Warning: currentValue is null ? $"برای محاسبه {labelFa} داده کافی وجود ندارد." : null));
    }

    private static FinancialStatementSourceReference ToSourceReference(
        FinancialStatementAnalysisStatementSnapshot statement) =>
        new(
            statement.StatementType.ToString(),
            statement.StatementId,
            statement.ExternalStatementId,
            statement.ProviderName,
            statement.PeriodType,
            statement.PeriodMonths,
            statement.JalaliPeriodEnd,
            statement.JalaliFiscalYearEnd,
            statement.JalaliAnnouncementDate,
            statement.IsAudited,
            statement.IsRepresented,
            statement.IsComposing);

    private static decimal? GetValue(FinancialStatementAnalysisStatementSnapshot? statement, string metricCode)
    {
        if (statement is null)
            return null;

        return statement.LineItems.TryGetValue(metricCode, out var value) ? value : null;
    }

    private static decimal? ComputeRatio(decimal? numerator, decimal? denominator, bool multiplyBy100 = false)
    {
        if (!numerator.HasValue || !denominator.HasValue || denominator.Value == 0m)
            return null;

        var value = numerator.Value / denominator.Value;
        return multiplyBy100 ? value * 100m : value;
    }

    private static decimal? ComputeChangePercent(decimal? current, decimal? previous)
    {
        if (!current.HasValue || !previous.HasValue || previous.Value == 0m)
            return null;

        return ((current.Value - previous.Value) / Math.Abs(previous.Value)) * 100m;
    }

    private static string ResolveIndicator(decimal? current, decimal? previous, string metricCode)
    {
        if (!current.HasValue || !previous.HasValue)
            return "⏹️";

        var delta = current.Value - previous.Value;
        if (delta == 0m)
            return "⏹️";

        var positiveMeansGood = metricCode switch
        {
            "TOTAL_LIABILITIES" or "DEBT_RATIO" => false,
            _ => true
        };

        var improved = positiveMeansGood ? delta > 0m : delta < 0m;
        return improved ? "✅" : "⚪️";
    }

    private static string? FormatAmount(decimal? value, string unit) =>
        value.HasValue ? $"{value.Value:N0} {unit}" : null;

    private static string? FormatPercent(decimal? value) =>
        value.HasValue ? $"{value.Value:0.##}%" : null;

    private static string? FormatRatio(decimal? value, string suffix) =>
        value.HasValue
            ? string.IsNullOrWhiteSpace(suffix)
                ? value.Value.ToString("0.##", CultureInfo.InvariantCulture)
                : $"{value.Value:0.##}{suffix}"
            : null;
}

internal static class FinancialStatementSourceReferenceExtensions
{
    internal static string PeriodEndLabel(this FinancialStatementSourceReference source) =>
        source.JalaliPeriodEnd ?? source.ExternalStatementId;
}
