using System.Text.Json;
using FinancialCopilot.Application.AI.Orchestration;
using FinancialCopilot.Application.Conversations;
using FinancialCopilot.Application.FinancialData.Ingestion;
using FinancialCopilot.Application.FinancialData.Providers;
using FinancialCopilot.Application.Memory;
using FinancialCopilot.Application.Scanner;

namespace FinancialCopilot.Infrastructure.AI.OrchestrationV2.Functions;

internal sealed class MessagePersistenceFunction(
    IConversationRepository repository,
    TimeProvider timeProvider,
    ISymbolLookupProseBuilder symbolLookupProseBuilder)
{
    internal async Task<PersistedConversationExchange> PersistAsync(
        Guid conversationId,
        AiQueryRequest request,
        DetectedIntent intent,
        bool clarificationRequired,
        string? clarificationMessage,
        string? textAnswer,
        ScannerQueryPlan? scannerPlan,
        ScannerTableResult? scannerTable,
        SymbolLookupTableResult? symbolLookupTable,
        ExplainableAnswer? explainableAnswer,
        ConfidenceScoreResult? confidenceScore,
        UsageAccountingResult? usage,
        AuthorizedMemoryContext memoryContext,
        string? agentResponseText,
        bool createConversation,
        CancellationToken cancellationToken,
        ComprehensiveAnalysisQueryResponse? comprehensiveAnalysisResult = null,
        FinancialStatementAnalysisResponse? financialStatementAnalysisResult = null,
        FinancialStatementTableResult? financialStatementTableResult = null,
        ProductRevenueMixResponse? productRevenueMixResult = null,
        MonthlyActivityTrendResponse? monthlyActivityTrendResult = null,
        MonthlySalesQualityRankingResponse? monthlySalesQualityRankingResult = null,
        DisclosureListingResult? disclosureListingResult = null)
    {
        var planJson = scannerPlan is not null ? JsonSerializer.Serialize(scannerPlan) : null;
        var assistantContent = agentResponseText is { Length: > 0 }
            ? agentResponseText
            : BuildAssistantContent(
                intent, scannerPlan, scannerTable, symbolLookupTable,
                explainableAnswer, textAnswer, clarificationRequired, clarificationMessage,
                comprehensiveAnalysisResult, financialStatementAnalysisResult, financialStatementTableResult, productRevenueMixResult, monthlyActivityTrendResult,
                monthlySalesQualityRankingResult);

        var disclosures = memoryContext.Disclosures.Count > 0 ? memoryContext.Disclosures : null;

        return await repository.PersistExchangeAsync(
            new ConversationExchange(
                conversationId,
                request.TenantId,
                request.ActorId,
                timeProvider.GetUtcNow(),
                BuildConversationTitle(request.Message),
                request.Message,
                assistantContent,
                planJson,
                new AssistantMessagePayload(
                    Version: 2,
                    intent,
                    clarificationRequired,
                    clarificationMessage,
                    textAnswer,
                    scannerPlan,
                    scannerTable,
                    symbolLookupTable,
                    explainableAnswer,
                    confidenceScore,
                    usage,
                    disclosures,
                    ComprehensiveAnalysisResult: comprehensiveAnalysisResult,
                    FinancialStatementAnalysisResult: financialStatementAnalysisResult,
                    FinancialStatementTableResult: financialStatementTableResult,
                    ProductRevenueMixResult: productRevenueMixResult,
                    MonthlyActivityTrendResult: monthlyActivityTrendResult,
                    MonthlySalesQualityRankingResult: monthlySalesQualityRankingResult,
                    DisclosureListingResult: disclosureListingResult)),
            createConversation,
            cancellationToken);
    }

    private static string BuildConversationTitle(string message)
    {
        const int maxLength = 80;
        var normalized = string.Join(' ', message.Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries));
        return normalized.Length <= maxLength ? normalized : normalized[..maxLength];
    }

    private string BuildAssistantContent(
        DetectedIntent intent,
        ScannerQueryPlan? plan,
        ScannerTableResult? table,
        SymbolLookupTableResult? lookupTable,
        ExplainableAnswer? explainableAnswer,
        string? textAnswer,
        bool clarificationRequired,
        string? clarificationMessage,
        ComprehensiveAnalysisQueryResponse? comprehensiveAnalysisResult = null,
        FinancialStatementAnalysisResponse? financialStatementAnalysisResult = null,
        FinancialStatementTableResult? financialStatementTableResult = null,
        ProductRevenueMixResponse? productRevenueMixResult = null,
        MonthlyActivityTrendResponse? monthlyActivityTrendResult = null,
        MonthlySalesQualityRankingResponse? monthlySalesQualityRankingResult = null)
    {
        if (clarificationRequired && clarificationMessage is not null)
            return clarificationMessage;

        if (monthlyActivityTrendResult is not null)
            return BuildMonthlyActivityTrendContent(monthlyActivityTrendResult);

        if (monthlySalesQualityRankingResult is not null)
            return BuildMonthlySalesQualityRankingContent(monthlySalesQualityRankingResult);

        if (financialStatementAnalysisResult?.RenderedAnswer is { Length: > 0 } rendered)
            return rendered;

        if (financialStatementTableResult?.RenderedAnswer is { Length: > 0 } renderedTable)
            return renderedTable;

        if (lookupTable is not null)
            return symbolLookupProseBuilder.Build(lookupTable);

        if (explainableAnswer?.ExplanationText is not null)
            return explainableAnswer.ExplanationText;

        if (productRevenueMixResult is not null)
            return BuildProductRevenueMixContent(productRevenueMixResult);

        if (table is not null)
            return plan?.Language?.StartsWith("fa", StringComparison.OrdinalIgnoreCase) == true
                ? $"اسکنر برای {plan!.Conditions.Count} شرط، {table.Rows.Count} نماد منطبق پیدا کرد."
                : $"Scanner found {table.Rows.Count} matching symbol(s) for {plan!.Conditions.Count} condition(s).";

        if (plan is not null)
            return plan.Language?.StartsWith("fa", StringComparison.OrdinalIgnoreCase) == true
                ? $"برنامه اسکن با {plan.Conditions.Count} شرط ایجاد شد."
                : $"Scanner plan created with {plan.Conditions.Count} condition(s).";

        if (comprehensiveAnalysisResult is not null)
            return comprehensiveAnalysisResult.HasResults
                ? $"{comprehensiveAnalysisResult.Items.Count} تحلیل جامع یافت شد."
                : "تحلیل جامعی برای معیارهای درخواستی یافت نشد.";

        return textAnswer ?? "I can help you screen stocks. Please describe your criteria.";
    }

    private static string BuildMonthlyActivityTrendContent(MonthlyActivityTrendResponse result)
    {
        var sb = new System.Text.StringBuilder();
        var companyLabel = result.CompanyName is not null
            ? $"{result.CompanyName} ({result.CompanySymbol})"
            : result.CompanySymbol;

        sb.AppendLine($"### روند فروش ماهانه - {companyLabel}");
        sb.AppendLine($"آخرین دوره گزارش: {result.LatestReportYear}/{result.LatestReportMonth:D2} | واحد: {result.UnitLabelFa}");
        sb.AppendLine();

        if (result.LatestMonthlySalesAmount.HasValue)
            sb.AppendLine($"**خلاصه آخرین ماه:** فروش {FormatTrendAmount(result.LatestMonthlySalesAmount.Value)} {result.UnitLabelFa}");

        if (result.SameMonthPreviousYearSalesAmount.HasValue)
        {
            if (result.SalesAmountYoYGrowthPercent.HasValue)
            {
                var sign = result.SalesAmountYoYGrowthPercent.Value >= 0 ? "+" : "";
                sb.AppendLine($"**مقایسه با ماه مشابه سال قبل:** {FormatTrendAmount(result.SameMonthPreviousYearSalesAmount.Value)} {result.UnitLabelFa} ({sign}{result.SalesAmountYoYGrowthPercent.Value:F1}٪)");
            }
            else
            {
                sb.AppendLine($"**مقایسه با ماه مشابه سال قبل:** {FormatTrendAmount(result.SameMonthPreviousYearSalesAmount.Value)} {result.UnitLabelFa}");
            }
        }

        if (result.Average12MonthSalesAmount.HasValue)
        {
            var vsAvgText = result.SalesVsAverage12MonthPercent.HasValue
                ? $" ({(result.SalesVsAverage12MonthPercent.Value >= 0 ? "+" : "")}{result.SalesVsAverage12MonthPercent.Value:F1}٪ نسبت به میانگین)"
                : "";
            sb.AppendLine($"**میانگین ۱۲ ماهه:** {FormatTrendAmount(result.Average12MonthSalesAmount.Value)} {result.UnitLabelFa}{vsAvgText}");
        }

        if (result.Insights.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**نکات تحلیلی:**");
            foreach (var insight in result.Insights)
                sb.AppendLine($"- {insight.TextFa}");
        }

        if (result.ChartPoints.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("**داده نمودار ماهانه:**");
            sb.AppendLine();

            var firstPoint = result.ChartPoints.First(p => p.PreviousFiscalYear.HasValue || p.CurrentFiscalYear.HasValue);
            var prevYearLabel = firstPoint.PreviousFiscalYear.HasValue ? $"فروش {firstPoint.PreviousFiscalYear}" : "فروش سال قبل";
            var currYearLabel = firstPoint.CurrentFiscalYear.HasValue ? $"فروش {firstPoint.CurrentFiscalYear}" : "فروش سال جاری";

            sb.AppendLine($"| ماه | {prevYearLabel} | {currYearLabel} | میانگین ۱۲ ماهه |");
            sb.AppendLine("|-----|------------:|-------------:|----------------:|");

            foreach (var pt in result.ChartPoints)
            {
                var prevVal = pt.PreviousFiscalYearSalesAmount.HasValue
                    ? FormatTrendAmount(pt.PreviousFiscalYearSalesAmount.Value)
                    : "—";
                var currVal = pt.IsCurrentYearReported && pt.CurrentFiscalYearSalesAmount.HasValue
                    ? FormatTrendAmount(pt.CurrentFiscalYearSalesAmount.Value)
                    : "—";
                var avgVal = pt.Average12MonthSalesAmount.HasValue
                    ? FormatTrendAmount(pt.Average12MonthSalesAmount.Value)
                    : "—";
                sb.AppendLine($"| {pt.FiscalMonthNameFa} | {prevVal} | {currVal} | {avgVal} |");
            }
        }

        if (result.MissingDataPoints.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"*داده‌های ناقص: {result.MissingDataPoints.Count} دوره موجود نیست.*");
        }

        sb.AppendLine();
        sb.AppendLine($"*منبع: {ProviderSources.GetDisplayName(result.SourceProviderName)} | محاسبه: {ShamsiMonthCalculator.FormatJalaliDate(result.CalculatedAtUtc)}*");

        return sb.ToString().TrimEnd();
    }

    private static string FormatTrendAmount(decimal value) =>
        value.ToString("#,##0.###");

    private static string BuildMonthlySalesQualityRankingContent(MonthlySalesQualityRankingResponse result)
    {
        var sb = new System.Text.StringBuilder();
        var title = result.Direction == MonthlySalesQualityDirection.Bottom
            ? "ضعیف‌ترین گزارش‌ها از نظر کیفیت تولید و فروش"
            : "برترین گزارش‌ها از نظر کیفیت تولید و فروش";

        sb.AppendLine($"### {title}");
        sb.AppendLine($"دوره: {result.ReportYear}/{result.ReportMonth:D2}");
        sb.AppendLine("این رتبه‌بندی توصیه خرید/فروش نیست و فقط کیفیت داده‌های تولید و فروش را ارزیابی می‌کند.");
        sb.AppendLine();
        sb.AppendLine("| رتبه | نماد | شرکت | صنعت | امتیاز کیفیت | برچسب | دلیل اصلی | اطمینان |");
        sb.AppendLine("|---:|---|---|---|---:|---|---|---:|");
        foreach (var item in result.Items)
        {
            var mainDriver = item.PositiveDrivers.FirstOrDefault()
                ?? item.NegativeDrivers.FirstOrDefault()
                ?? "—";
            sb.AppendLine($"| {item.Rank} | {item.Symbol} | {item.CompanyName ?? "—"} | {item.IndustryTitle ?? "—"} | {item.QualityScore:F1} | {item.QualityLabel} | {mainDriver} | {item.ConfidenceScore:F0} | ");
        }
        return sb.ToString().TrimEnd();
    }

    private static string BuildProductRevenueMixContent(ProductRevenueMixResponse result)
    {
        var sb = new System.Text.StringBuilder();
        var companyLabel = result.CompanyName is not null
            ? $"{result.CompanyName} ({result.CompanySymbol})"
            : result.CompanySymbol;
        sb.AppendLine($"### ترکیب درآمد محصولات — {companyLabel}");
        sb.AppendLine($"دوره: {result.ReportYear}/{result.ReportMonth:D2} | کل فروش: {result.TotalSalesAmount:N0} ریال");
        sb.AppendLine();
        sb.AppendLine("| ردیف | محصول | فروش (ریال) | سهم (٪) | غالب |");
        sb.AppendLine("|------|-------|------------|---------|------|");
        foreach (var p in result.Products)
        {
            var dominant = p.IsDominantProduct ? "✓" : "";
            sb.AppendLine($"| {p.Rank} | {p.ProductName} | {p.SalesAmount:N0} | {p.RevenueSharePercentage:F1}٪ | {dominant} |");
        }
        return sb.ToString().TrimEnd();
    }
}
