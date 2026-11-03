using System.Globalization;
using System.Text;
using System.Collections;

namespace FinancialCopilot.Application.FinancialData.Semantics.Navigation;

public enum StructuredAnswerStatus { Answered, NoData, Ambiguous, Unsupported, Incomplete, Failed }

public sealed record StructuredQueryAnswerRequest(
    StructuredQueryPlan? Plan,
    StructuredQueryExecutionResult? Execution,
    string? FailureStage = null,
    IReadOnlyList<string>? Diagnostics = null);

public sealed record StructuredAnswerEvidence(
    string? Company,
    string? Product,
    string? PeriodStart,
    string? PeriodEnd,
    string? Provider,
    IReadOnlyList<string> ReportIdentities,
    IReadOnlyList<string> SourceReferences,
    IReadOnlyDictionary<string, string> Facts,
    string EvidenceStatus = "verified");

/// <summary>Response contracts for the Feature 141 typed-query path. Every displayed fact is copied from query evidence.</summary>
public sealed record PersianStructuredQueryResponse(
    StructuredAnswerStatus Status,
    string TextAnswer,
    string? Unit,
    string? FailureStage,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<StructuredAnswerEvidence> Evidence,
    IReadOnlyList<IReadOnlyDictionary<string, string>> TableRows,
    IReadOnlyList<string> Diagnostics);

public interface IPersianStructuredQueryResponseGenerator
{
    PersianStructuredQueryResponse Generate(StructuredQueryAnswerRequest request);
}

/// <summary>
/// Deterministically renders verified Feature 141 rows. It never computes measures, growth, or aggregate values;
/// it only formats values already returned by the authorized executor.
/// </summary>
public sealed class PersianStructuredQueryResponseGenerator : IPersianStructuredQueryResponseGenerator
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;
    private readonly QuantityUnitDisplayCatalog _quantityUnits;

    public PersianStructuredQueryResponseGenerator() : this(new QuantityUnitDisplayCatalog()) { }

    public PersianStructuredQueryResponseGenerator(QuantityUnitDisplayCatalog quantityUnits) =>
        _quantityUnits = quantityUnits ?? throw new ArgumentNullException(nameof(quantityUnits));

    public PersianStructuredQueryResponse Generate(StructuredQueryAnswerRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        var execution = request.Execution;
        if ((request.Diagnostics ?? []).Contains("producer_sales_scope_ambiguous", StringComparer.Ordinal))
            return new(StructuredAnswerStatus.Ambiguous,
                "منظورتان رتبه‌بندی بر اساس فروش کل شرکت (مبلغ کل فروش ماهانه) است یا فروش خود گندله (مبلغ یا مقدار)؟",
                null, request.FailureStage ?? "planning_validation", [], [], [], request.Diagnostics ?? []);
        if (request.Plan is null || execution is null)
            return SafeFailure(StructuredAnswerStatus.Unsupported, request,
                "این درخواست با تعریف‌های تأییدشدهٔ پرس‌وجو قابل اجرا نیست.");
        if (PersianStatementComparisonRenderer.Applies(request.Plan))
            return PersianStatementComparisonRenderer.Render(request, request.Plan, execution);

        var status = execution.Status switch
        {
            StructuredQueryExecutionStatus.Succeeded => StructuredAnswerStatus.Answered,
            StructuredQueryExecutionStatus.NoData => StructuredAnswerStatus.NoData,
            StructuredQueryExecutionStatus.Ambiguous => StructuredAnswerStatus.Ambiguous,
            StructuredQueryExecutionStatus.Incomplete or StructuredQueryExecutionStatus.Truncated => StructuredAnswerStatus.Incomplete,
            _ => StructuredAnswerStatus.Failed
        };
        if (status != StructuredAnswerStatus.Answered)
        {
            if (status == StructuredAnswerStatus.Incomplete && request.Plan.PopulationCoveragePolicyId is not null)
            {
                // Owner decision 2026-10-30: a product mapping limited to approved aliases still ranks the companies that
                // have a valid observation, labelled as partial. Incomplete company-scope coverage is still refused.
                if (request.Plan.PopulationSalesScope is not null
                    && execution.DiagnosticCode == "population_discovery_product_alias_coverage_incomplete")
                    return RenderPartialProducerSalesRanking(request, request.Plan, execution);
                if (request.Plan.PopulationSalesScope is not null)
                    return new(status,
                        "پوشش جمعیت تولیدکنندگان یا نگاشت محصول برای رتبه‌بندی کامل نیست؛ رتبهٔ مالی گزارش نمی‌شود.",
                        execution.EffectiveUnit, request.FailureStage ?? "coverage_validation", execution.Warnings,
                        [], [], (request.Diagnostics ?? []).Concat(execution.DiagnosticCode is null ? [] : [execution.DiagnosticCode])
                            .Distinct(StringComparer.Ordinal).ToArray());
                var partialEvidence = execution.Rows.Where(row => row.GetValueOrDefault("CompanyId") is Guid)
                    .Select(row => ToEvidence(row, CompanyLabels(request.Plan), ProductLabel(request.Plan))).ToArray();
                var partialText = partialEvidence.Length == 0
                    ? PopulationNoMatchText(request.Plan)
                    : RenderPopulationDiscovery(request.Plan, partialEvidence, execution.Rows, partial: true);
                return new(status, AppendWarnings(partialText, execution.Warnings), execution.EffectiveUnit,
                    request.FailureStage ?? "coverage_validation", execution.Warnings, partialEvidence,
                    execution.Rows.Where(row => row.GetValueOrDefault("CompanyId") is Guid).Select(ToDisplayFacts).ToArray(),
                    (request.Diagnostics ?? []).Concat(execution.DiagnosticCode is null ? [] : [execution.DiagnosticCode])
                        .Distinct(StringComparer.Ordinal).ToArray());
            }
            var text = status switch
            {
                StructuredAnswerStatus.NoData => NoDataText(request.Plan),
                StructuredAnswerStatus.Ambiguous => "داده‌های موجود برای ارائهٔ یک پاسخ مالی یکتا کافی نیستند؛ نتیجه مبهم است.",
                StructuredAnswerStatus.Incomplete => request.Plan.PopulationCoveragePolicyId is not null
                    ? PopulationNoMatchText(request.Plan)
                    : "داده یا پوشش لازم برای پاسخ کامل نیست؛ عددی گزارش نمی‌کنم.",
                _ => "اجرای امن پرس‌وجو کامل نشد؛ نتیجهٔ مالی ارائه نمی‌شود."
            };
            var failureEvidence = status == StructuredAnswerStatus.Ambiguous
                ? execution.Rows.Select(row => ToEvidence(row, CompanyLabels(request.Plan), ProductLabel(request.Plan), "ambiguous-not-answerable")).ToArray()
                : [];
            var diagnostics = (request.Diagnostics ?? []).Concat(execution.DiagnosticCode is null ? [] : [execution.DiagnosticCode])
                .Distinct(StringComparer.Ordinal).ToArray();
            return new(status, AppendWarnings(text, execution.Warnings), execution.EffectiveUnit,
                request.FailureStage ?? "postgresql_execution", execution.Warnings, failureEvidence, [], diagnostics);
        }
        if (execution.Rows.Count == 0)
            return new(StructuredAnswerStatus.NoData, request.Plan.PopulationSalesScope is not null
                    ? "برای جمعیت تولیدکنندگان واجد شرایط، ماه گزارش مشترکی با دادهٔ فروش معتبر پیدا نشد."
                    : "برای این پرس‌وجو داده‌ای پیدا نشد.", execution.EffectiveUnit,
                request.FailureStage, execution.Warnings, [], [], request.Diagnostics ?? []);

        var companyLabels = CompanyLabels(request.Plan);
        var productLabel = ProductLabel(request.Plan);
        var evidence = execution.Rows.Select(row => ToEvidence(row, companyLabels, productLabel)).ToArray();
        var tableRows = execution.Rows.Select(ToDisplayFacts).ToArray();
        var metric = request.Plan.Sources.FirstOrDefault()?.MetricCode ?? request.Plan.Sources.FirstOrDefault()?.CatalogId;
        var measure = MeasureLabel(metric, request.Plan.Sources.FirstOrDefault()?.TitleFa);
        if (IsQuantityMetric(metric) && HasIncompatibleQuantityUnits(execution.Rows))
            return new(StructuredAnswerStatus.Ambiguous,
                "واحدهای اندازه‌گیری گزارش‌ها با هم سازگار نیستند؛ نتیجهٔ عددی ارائه نمی‌شود.", null,
                "unit_validation", [], [], [], ["monthly_quantity_units_incompatible"]);
        var unit = ResolveUnit(execution, metric);
        var operation = request.Plan.FinancialOperation;
        var textAnswer = operation.Kind switch
        {
            _ when request.Plan.PopulationCoveragePolicyId is not null && request.Plan.PopulationSalesScope is null =>
                RenderPopulationDiscovery(request.Plan, evidence, execution.Rows, partial: false),
            _ when request.Plan.PopulationSalesScope is not null =>
                RenderProducerSalesRanking(request.Plan, measure, unit, execution.Rows, evidence),
            _ when request.Plan.Presentation == PlanPresentation.RankedTable => RenderRanking(measure, unit, execution.Rows, evidence),
            PlanFinancialOperationKind.TimeSeries => RenderTimeSeries(measure, unit, evidence),
            PlanFinancialOperationKind.GrowthPercentage or PlanFinancialOperationKind.AbsoluteChange =>
                RenderComparisons(measure, unit, operation.Kind, execution.Rows, evidence),
            _ when request.Plan.Entities.Count(x => x.EntityKind.Equals("Company", StringComparison.OrdinalIgnoreCase)) > 1
                => RenderCompanyComparison(measure, unit, execution.Rows, evidence),
            _ when MonthlyMeasureLabel(metric) is not null && execution.Rows.Count == 1
                => RenderMonthlyValue(metric, unit, execution.Rows[0], evidence[0]),
            _ => RenderValues(measure, unit, execution.Rows, evidence)
        };
        textAnswer = AppendWarnings(textAnswer, execution.Warnings);
        return new(StructuredAnswerStatus.Answered, textAnswer, unit, request.FailureStage,
            execution.Warnings, evidence, tableRows, request.Diagnostics ?? []);
    }

    private static PersianStructuredQueryResponse SafeFailure(StructuredAnswerStatus status,
        StructuredQueryAnswerRequest request, string message) => new(status, message, null,
        request.FailureStage, [], [], [], request.Diagnostics ?? []);

    private static string RenderValues(string measure, string unit, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<StructuredAnswerEvidence> evidence)
    {
        var row = rows[0];
        var subjectAndPeriod = string.Join(" ", new[] { Subject(evidence[0]), Period(evidence[0]).TrimStart() }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        var provenance = Provenance(evidence[0]);
        // A statement value states its reporting basis, so a standalone default is never mistaken for the consolidated figure.
        var basis = row.GetValueOrDefault("IsComposing") switch
        {
            true => " بر اساس صورت مالی تلفیقی",
            false => " بر اساس صورت مالی جداگانه",
            _ => string.Empty
        };
        return $"{subjectAndPeriod}، {measure}{basis}: {Amount(row.GetValueOrDefault("Value"), unit)}{(provenance.Length == 0 ? "." : "." + provenance + ".")}";
    }

    private static string RenderComparisons(string measure, string unit, PlanFinancialOperationKind kind,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, IReadOnlyList<StructuredAnswerEvidence> evidence)
    {
        var row = rows[0];
        if (row.GetValueOrDefault("ComparisonStatus") as string is not (null or "comparable"))
            return "دوره‌های گزارش برای این مقایسه قابل تأیید نیستند؛ نتیجهٔ عددی ارائه نمی‌شود.";
        var current = Amount(row.GetValueOrDefault("Value"), unit);
        var previous = Amount(row.GetValueOrDefault("PreviousValue"), unit);
        var changeKey = kind == PlanFinancialOperationKind.GrowthPercentage ? "GrowthPercentage" : "ComparisonValue";
        var changeUnit = kind == PlanFinancialOperationKind.GrowthPercentage ? "درصد" : unit;
        var subject = Subject(evidence[0]);
        var provenance = Provenance(evidence[0]);
        return $"{subject}، {measure} در دورهٔ جاری {current} و در دورهٔ مبنا {previous} بوده است؛ " +
               $"مقدار ثبت‌شده برای تغییر {(kind == PlanFinancialOperationKind.GrowthPercentage ? "درصدی" : "مطلق")}: {Amount(row.GetValueOrDefault(changeKey), changeUnit)}." +
               (provenance.Length == 0 ? string.Empty : provenance + ".");
    }

    /// <summary>One monthly production or sales figure: «تولید گندله کگل در شهریور ۱۴۰۵ برابر با ۱٬۰۸۵٬۱۵۷ تن بوده است.»</summary>
    private static string RenderMonthlyValue(string? metric, string unit, IReadOnlyDictionary<string, object?> row,
        StructuredAnswerEvidence evidence)
    {
        var company = Text(row.GetValueOrDefault("CompanySymbol")) ?? evidence.Company;
        var subject = string.Join(" ", new[] { MonthlyMeasureLabel(metric), evidence.Product, company }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        var period = Period(evidence);
        return $"{subject}{(period.Length == 0 ? "" : " " + period)} برابر با {Amount(row.GetValueOrDefault("Value"), unit)} بوده است."
            + Provenance(evidence);
    }

    /// <summary>A trend as one line per reported period, without repeating the source after every period.</summary>
    private static string RenderTimeSeries(string measure, string unit, IReadOnlyList<StructuredAnswerEvidence> evidence) =>
        "روند " + string.Join(" ", new[] { measure, evidence.Count == 0 ? null : Subject(evidence[0]) }
            .Where(value => !string.IsNullOrWhiteSpace(value))) + ":\n"
        + string.Join("\n", evidence.Select(item =>
            $"{PeriodPhrase(item)}: {Amount(item.Facts.GetValueOrDefault("Value"), unit, "ناموجود")}"))
        + (evidence.Count == 0 ? string.Empty : Provenance(evidence[0]));

    private static string RenderRanking(string measure, string unit, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        IReadOnlyList<StructuredAnswerEvidence> evidence)
    {
        var growthRanking = rows.Count > 0 && rows[0].ContainsKey("GrowthPercentage");
        var resultKey = growthRanking ? "GrowthPercentage" : "Value";
        var resultUnit = growthRanking ? "درصد" : unit;
        var rankingLabel = growthRanking ? "رشد " + measure : measure;
        return "رتبه‌بندی بر اساس " + rankingLabel + ": " +
            string.Join("؛ ", rows.Select((row, index) =>
                $"رتبهٔ {Digits((index + 1).ToString(Invariant))}: {Subject(evidence[index])}، {Amount(row.GetValueOrDefault(resultKey), resultUnit)}{Provenance(evidence[index])}")) + ".";
    }

    private static string RenderProducerSalesRanking(StructuredQueryPlan plan, string measure, string unit,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, IReadOnlyList<StructuredAnswerEvidence> evidence)
    {
        var product = plan.ProductConcept?.CanonicalLabel ?? ProductLabel(plan) ?? "محصول تأییدشده";
        var metric = plan.PopulationSalesScope == PopulationSalesScope.TotalCompanySales
            ? $"رتبه‌بندی تولیدکنندگان {product} بر اساس {measure} کل شرکت"
            : $"رتبه‌بندی تولیدکنندگان {product} بر اساس {measure} همان محصول";
        var period = evidence.Count == 0 ? string.Empty : $" در دورهٔ مشترک {PeriodPhrase(evidence[0])}";
        return metric + period + ": " + string.Join("؛ ", rows.Select((row, index) =>
        {
            var company = evidence[index].Company ?? "شرکت با هویت نامشخص";
            var productName = plan.PopulationSalesScope == PopulationSalesScope.RequestedProductSales
                ? "، " + (evidence[index].Product ?? product) : string.Empty;
            return $"رتبهٔ {Digits((index + 1).ToString(Invariant))}: {company}{productName}، {Amount(row.GetValueOrDefault("Value"), unit)}{Provenance(evidence[index])}";
        })) + ".";
    }

    /// <summary>
    /// A producer ranking over the companies that have a valid observation in the requested period, in the executor's
    /// deterministic order (value descending, then company). A company without a value is left out, never ranked as
    /// zero; quantities in incompatible units are not ranked; the answer never claims complete market coverage.
    /// </summary>
    private PersianStructuredQueryResponse RenderPartialProducerSalesRanking(StructuredQueryAnswerRequest request,
        StructuredQueryPlan plan, StructuredQueryExecutionResult execution)
    {
        var diagnostics = (request.Diagnostics ?? []).Concat(execution.DiagnosticCode is null ? [] : [execution.DiagnosticCode])
            .Append("population_ranking_partial_product_coverage").Distinct(StringComparer.Ordinal).ToArray();
        var rows = execution.Rows.Where(row => row.GetValueOrDefault("CompanyId") is Guid
            && row.GetValueOrDefault("Value") is not (null or DBNull)).ToArray();
        if (rows.Length == 0)
            return new(StructuredAnswerStatus.Incomplete, PopulationNoMatchText(plan), execution.EffectiveUnit,
                request.FailureStage ?? "coverage_validation", execution.Warnings, [], [], diagnostics);
        var metric = plan.Sources.FirstOrDefault()?.MetricCode ?? plan.Sources.FirstOrDefault()?.CatalogId;
        if (IsQuantityMetric(metric) && HasIncompatibleQuantityUnits(rows))
            return new(StructuredAnswerStatus.Ambiguous,
                "واحدهای اندازه‌گیری گزارش‌ها با هم سازگار نیستند؛ نتیجهٔ عددی ارائه نمی‌شود.", null,
                "unit_validation", [], [], [], diagnostics.Append("monthly_quantity_units_incompatible").ToArray());

        var evidence = rows.Select(row => ToEvidence(row, CompanyLabels(plan), ProductLabel(plan))).ToArray();
        var unit = IsQuantityMetric(metric)
            ? ResolveQuantityUnit(rows.SelectMany(QuantityUnitsInRow).FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)))
            : ResolveUnit(execution, metric);
        var product = plan.ProductConcept?.CanonicalLabel ?? ProductLabel(plan) ?? "محصول تأییدشده";
        var basis = plan.PopulationSalesScope == PopulationSalesScope.TotalCompanySales
            ? $"{MeasureLabel(metric)} کل شرکت"
            : $"{MeasureLabel(metric)} {product}";
        var period = plan.Period.ReportingPeriodStart is { } start && plan.Period.ReportingPeriodEnd is { } end
            ? PersianReportingPeriodText.Window(start, end)
            : PeriodPhrase(evidence[0]);
        var items = rows.Select((row, index) =>
            $"رتبهٔ {Digits((index + 1).ToString(Invariant))}: {evidence[index].Company ?? "شرکت با هویت نامشخص"}، " +
            Amount(row.GetValueOrDefault("Value"), unit));
        // Report identities stay in the evidence; the text names a provider only when it is not the default source.
        var providers = evidence.Select(item => item.Provider).OfType<string>().Where(provider => !IsDefaultProvider(provider))
            .Distinct(StringComparer.Ordinal).ToArray();
        var source = providers.Length == 0 ? string.Empty : $"\nمنبع: {string.Join("، ", providers)}";
        var text = $"در {period}، در میان شرکت‌های تولیدکنندهٔ {product} که اطلاعات معتبر دارند، رتبه‌بندی بر اساس {basis} به شرح زیر است:\n" +
            string.Join("\n", items) +
            "\nاین رتبه‌بندی فقط شرکت‌های دارای مشاهدهٔ معتبر در داده‌های موجود را در بر می‌گیرد و پوشش کامل بازار را نشان نمی‌دهد؛ " +
            "شرکت‌های بدون مشاهدهٔ معتبر کنار گذاشته شده‌اند." + source;
        return new(StructuredAnswerStatus.Incomplete, AppendWarnings(text, execution.Warnings), unit,
            request.FailureStage ?? "coverage_validation", execution.Warnings, evidence, rows.Select(ToDisplayFacts).ToArray(),
            diagnostics);
    }

    private static string RenderCompanyComparison(string measure, string unit,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, IReadOnlyList<StructuredAnswerEvidence> evidence) =>
        "مقایسهٔ " + measure + ": " + string.Join("؛ ", rows.Select((row, index) =>
            $"{Subject(evidence[index])} {Amount(row.GetValueOrDefault("Value"), unit)} ({PeriodPhrase(evidence[index])}){Provenance(evidence[index])}")) + ".";

    private string RenderPopulationDiscovery(StructuredQueryPlan plan,
        IReadOnlyList<StructuredAnswerEvidence> evidence,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, bool partial)
    {
        var window = plan.Period.ReportingPeriodStart.HasValue && plan.Period.ReportingPeriodEnd.HasValue
            ? PersianReportingPeriodText.Window(plan.Period.ReportingPeriodStart.Value, plan.Period.ReportingPeriodEnd.Value)
            : "دورهٔ گزارش نامشخص";
        var label = ProductLabel(plan) ?? "محصول تأییدشده";
        var items = evidence.Select((item, index) =>
        {
            var row = rows[index];
            var company = item.Company ?? "شرکت با هویت نامشخص";
            var unit = ResolveRowUnit(row);
            return $"{company}: تولید ثبت‌شدهٔ {label} {Amount(row.GetValueOrDefault("Value"), unit)} {Period(item).TrimStart()}{Provenance(item)}";
        });
        var prefix = $"در گزارش‌های ماهانهٔ واجد شرایط {window}، تولید مثبت {label} برای شرکت‌های زیر مشاهده شد: ";
        var coverage = partial
            ? " این فهرست جزئی است: پوشش دسترسی به شرکت‌ها یا نگاشت محصول کامل نیست و از آن نمی‌توان نتیجه گرفت که شرکت‌های فهرست‌نشده تولید نداشته‌اند."
            : " این فهرست همهٔ شرکت‌های داخل دامنهٔ مجاز را پوشش می‌دهد.";
        return prefix + string.Join("؛ ", items) + "." + coverage;
    }

    private static string PopulationNoMatchText(StructuredQueryPlan plan)
    {
        var window = plan.Period.ReportingPeriodStart.HasValue && plan.Period.ReportingPeriodEnd.HasValue
            ? $" در {PersianReportingPeriodText.Window(plan.Period.ReportingPeriodStart.Value, plan.Period.ReportingPeriodEnd.Value)}"
            : string.Empty;
        return $"در داده‌های قابل دسترسی{window}، مشاهده‌ای با برچسب‌های محصول تأییدشده پیدا نشد. به دلیل کامل نبودن پوشش نگاشت یا دسترسی، نبود تولید برای سایر شرکت‌ها قابل نتیجه‌گیری نیست.";
    }

    private static string NoDataText(StructuredQueryPlan plan)
    {
        var subjects = plan.Entities.Where(entity => entity.EntityKind.Equals("Company", StringComparison.OrdinalIgnoreCase)
                || entity.EntityKind.Equals("Product", StringComparison.OrdinalIgnoreCase))
            .Select(entity => entity.UserReference).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal)
            .ToArray();
        var subject = subjects.Length == 0 ? "این پرس‌وجو" : string.Join(" و ", subjects);
        var period = plan.Period.ReportingPeriodStart.HasValue && plan.Period.ReportingPeriodEnd.HasValue
            ? $" در {PersianReportingPeriodText.Window(plan.Period.ReportingPeriodStart.Value, plan.Period.ReportingPeriodEnd.Value)}"
            : plan.Period.SelectLatestByReportingPeriod ? " در آخرین دورهٔ گزارش واجد شرایط" : "";
        return $"برای {subject}{period} دادهٔ واجد شرایطی پیدا نشد.";
    }

    private static StructuredAnswerEvidence ToEvidence(IReadOnlyDictionary<string, object?> row,
        IReadOnlyDictionary<Guid, string> companyLabels, string? productLabel, string evidenceStatus = "verified")
    {
        var companyId = row.GetValueOrDefault("CompanyId") is Guid id ? id : (Guid?)null;
        var provider = Text(row.GetValueOrDefault("ProviderName"));
        var reportIds = Strings(row.GetValueOrDefault("ReportIdentity"))
            .Concat(Strings(row.GetValueOrDefault("ReportId")))
            .Concat(Strings(row.GetValueOrDefault("ExternalReportId"))).Distinct(StringComparer.Ordinal).ToArray();
        var references = Strings(row.GetValueOrDefault("SourceRowFingerprint"));
        var facts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in new[] { "Value", "PreviousValue", "GrowthPercentage", "ComparisonValue", "SourceUnit", "PreviousSourceUnit", "ComparisonStatus", "PeriodType" })
            if (row.TryGetValue(key, out var fact) && fact is not null) facts[key] = Convert.ToString(fact, Invariant) ?? string.Empty;
        var companyName = Text(row.GetValueOrDefault("CompanyName")) ?? Text(row.GetValueOrDefault("Company"));
        var symbol = Text(row.GetValueOrDefault("CompanySymbol"));
        if (!string.IsNullOrWhiteSpace(symbol)) companyName = $"{companyName ?? (companyId.HasValue && companyLabels.TryGetValue(companyId.Value, out var resolved) ? resolved : "شرکت ناشناس")} ({symbol})";
        else if (companyId.HasValue && companyLabels.TryGetValue(companyId.Value, out var resolvedCompany)) companyName ??= resolvedCompany;
        return new(companyName,
            Text(row.GetValueOrDefault("ProductTitle")) ?? productLabel,
            Text(row.GetValueOrDefault("PeriodStart")), Text(row.GetValueOrDefault("PeriodEnd")), provider,
            reportIds, references, facts, evidenceStatus);
    }

    private static IReadOnlyDictionary<Guid, string> CompanyLabels(StructuredQueryPlan plan) => plan.Entities
        .Where(entity => entity.EntityKind.Equals("Company", StringComparison.OrdinalIgnoreCase))
        .GroupBy(entity => entity.ResolvedEntityId).Where(group => group.Key.HasValue)
        .ToDictionary(group => group.Key!.Value, group => group.First().UserReference);

    private static string? ProductLabel(StructuredQueryPlan plan) => plan.Entities
        .FirstOrDefault(entity => entity.EntityKind.Equals("Product", StringComparison.OrdinalIgnoreCase))?.UserReference;

    private static IReadOnlyDictionary<string, string> ToDisplayFacts(IReadOnlyDictionary<string, object?> row)
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in new[] { "CompanyId", "CompanyName", "CompanySymbol", "ProductTitle", "PeriodStart", "PeriodEnd", "Value", "PreviousValue", "GrowthPercentage", "ComparisonValue", "SourceUnit", "PreviousSourceUnit", "ProviderName", "ReportId", "ExternalReportId", "SourceRowFingerprint", "ComparisonStatus" })
            if (row.TryGetValue(key, out var value) && value is not null) facts[key] = Convert.ToString(value, Invariant) ?? string.Empty;
        return facts;
    }

    private string ResolveUnit(StructuredQueryExecutionResult execution, string? metric)
    {
        if (IsQuantityMetric(metric))
        {
            var sourceUnit = execution.Rows.SelectMany(QuantityUnitsInRow)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
            return ResolveQuantityUnit(sourceUnit);
        }
        var unit = execution.EffectiveUnit ?? execution.Rows.Select(row => Text(row.GetValueOrDefault("SourceUnit")))
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return unit switch
        {
            "MillionIRR" => MillionIrrLabel,
            "IRRPerShare" => "ریال به‌ازای هر سهم",
            "percent" or "Percent" => "درصد",
            "source-quantity-unit" or "row-source-unit" or null or "" => "واحد اندازه‌گیری نامشخص",
            _ => unit
        };
    }

    private string ResolveRowUnit(IReadOnlyDictionary<string, object?> row) =>
        ResolveQuantityUnit(Text(row.GetValueOrDefault("SourceUnit")));

    private string ResolveQuantityUnit(string? sourceUnit) =>
        _quantityUnits.Resolve(sourceUnit)?.PersianLabel ?? "واحد اندازه‌گیری نامشخص";

    private bool HasIncompatibleQuantityUnits(IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        if (rows.Count > 1 && rows.Any(row => string.IsNullOrWhiteSpace(Text(row.GetValueOrDefault("SourceUnit")))))
            return rows.Any(row => !string.IsNullOrWhiteSpace(Text(row.GetValueOrDefault("SourceUnit"))));
        if (rows.Any(row => row.GetValueOrDefault("PreviousValue") is not null
                && string.IsNullOrWhiteSpace(Text(row.GetValueOrDefault("PreviousSourceUnit")))
                && !string.IsNullOrWhiteSpace(Text(row.GetValueOrDefault("SourceUnit")))))
            return true;
        var units = rows.SelectMany(QuantityUnitsInRow).Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => _quantityUnits.Resolve(value)?.CanonicalCode ?? "unrecognized:" + value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return units.Length > 1;
    }

    private static IEnumerable<string?> QuantityUnitsInRow(IReadOnlyDictionary<string, object?> row)
    {
        yield return Text(row.GetValueOrDefault("SourceUnit"));
        yield return Text(row.GetValueOrDefault("PreviousSourceUnit"));
    }

    private static bool IsQuantityMetric(string? metric) => metric is
        "metric_monthly_production_quantity" or "MONTHLY_PRODUCTION_QUANTITY" or
        "metric_monthly_sales_quantity" or "MONTHLY_SALES_QUANTITY";

    /// <summary>The short sentence label of a monthly measure; null for any other measure.</summary>
    private static string? MonthlyMeasureLabel(string? metric) => metric switch
    {
        "metric_monthly_production_quantity" or "MONTHLY_PRODUCTION_QUANTITY" => "تولید",
        "metric_monthly_sales_quantity" or "MONTHLY_SALES_QUANTITY" => "مقدار فروش",
        "metric_monthly_sales" or "MONTHLY_SALES" => "مبلغ فروش",
        _ => null
    };

    /// <summary>The measure's display label; a statement item is named by its registered title when the plan carries it.</summary>
    private static string MeasureLabel(string? metric, string? sourceTitle = null) => metric switch
    {
        "metric_monthly_production_quantity" or "MONTHLY_PRODUCTION_QUANTITY" => "مقدار تولید ماهانه",
        "metric_monthly_sales_quantity" or "MONTHLY_SALES_QUANTITY" => "مقدار فروش ماهانه",
        "metric_monthly_sales" or "MONTHLY_SALES" => "مبلغ فروش ماهانه",
        _ => string.IsNullOrWhiteSpace(sourceTitle) ? "مقدار مالی گزارش‌شده" : sourceTitle.Trim()
    };

    private static string Value(IReadOnlyDictionary<string, object?> row, string key) =>
        row.TryGetValue(key, out var value) && value is not null ? FormatValue(value) : "در دسترس نیست";

    internal static string FormatValue(object value)
    {
        var text = value switch
        {
            decimal number => number.ToString("#,##0.############################", Invariant),
            double number => number.ToString("#,##0.############################", Invariant),
            float number => number.ToString("#,##0.############################", Invariant),
            DateOnly date => date.ToString("yyyy-MM-dd", Invariant),
            DateTime date => date.ToString("yyyy-MM-dd", Invariant),
            _ => Convert.ToString(value, Invariant) ?? string.Empty
        };
        return Digits(text);
    }

    internal const string MillionIrrLabel = "میلیون ریال";

    /// <summary>
    /// A value with its unit label for display. A Million IRR amount is shown in Toman (1 Million IRR = 100,000 Toman):
    /// «میلیارد تومان» from one billion Toman, otherwise «میلیون تومان» or «تومان», at most two decimals. Other units unchanged.
    /// </summary>
    internal static string Amount(object? value, string unit, string missing = "در دسترس نیست")
    {
        if (value is null or DBNull) return $"{missing} {unit}";
        if (unit != MillionIrrLabel || !TryDecimal(value, out var millionIrr)) return $"{FormatValue(value)} {unit}";
        var toman = Math.Abs(millionIrr * 100_000m);
        var (scaled, label) = toman >= 1_000_000_000m ? (toman / 1_000_000_000m, "میلیارد تومان")
            : toman >= 1_000_000m ? (toman / 1_000_000m, "میلیون تومان") : (toman, "تومان");
        var text = Math.Round(scaled, 2, MidpointRounding.AwayFromZero).ToString("#,##0.##", Invariant);
        return (millionIrr < 0 ? "منفی " : string.Empty) + Digits(text) + " " + label;
    }

    /// <summary>The unit named beside converted amounts (Million IRR amounts are displayed in Toman).</summary>
    internal static string DisplayUnit(string unit) => unit == MillionIrrLabel ? "تومان" : unit;

    private static bool TryDecimal(object value, out decimal number)
    {
        switch (value)
        {
            case decimal d: number = d; return true;
            case string s: return decimal.TryParse(s, NumberStyles.Number, Invariant, out number);
            case IConvertible convertible:
                try { number = convertible.ToDecimal(Invariant); return true; }
                catch (Exception exception) when (exception is FormatException or InvalidCastException or OverflowException) { break; }
        }
        number = 0;
        return false;
    }

    internal static string Digits(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
            builder.Append(character switch
            {
                >= '0' and <= '9' => (char)('۰' + character - '0'),
                ',' => '٬',
                '.' => '٫',
                _ => character
            });
        return builder.ToString();
    }

    private static string Subject(StructuredAnswerEvidence item) =>
        string.Join(" ", new[] { item.Company, item.Product }.Where(value => !string.IsNullOrWhiteSpace(value)));
    private static string Period(StructuredAnswerEvidence item) => PeriodPhrase(item) is { Length: > 0 } phrase ? "در " + phrase : "";

    /// <summary>
    /// Jalali text of an observation's period (the evidence keeps its Gregorian ISO dates): a statement period from its period
    /// type, a monthly report as its Jalali month, any other window by its two boundaries.
    /// </summary>
    private static string PeriodPhrase(StructuredAnswerEvidence item)
    {
        if (item.PeriodStart is null && item.PeriodEnd is null) return "";
        var start = PersianReportingPeriodText.Parse(item.PeriodStart);
        var end = PersianReportingPeriodText.Parse(item.PeriodEnd);
        if (item.Facts.TryGetValue("PeriodType", out var periodType))
            return PersianReportingPeriodText.StatementPeriod(periodType, start, end);
        return start is { } first && end is { } last ? PersianReportingPeriodText.Window(first, last)
            : end is { } only ? "دوره منتهی به " + PersianReportingPeriodText.Date(only)
            : "دوره گزارش نامشخص";
    }
    private static string Fact(StructuredAnswerEvidence item, string key) =>
        item.Facts.TryGetValue(key, out var value) ? Digits(value) : "ناموجود";
    private static string? Text(object? value) => value switch
    {
        null or DBNull => null,
        DateOnly date => date.ToString("yyyy-MM-dd", Invariant),
        DateTime date => date.ToString("yyyy-MM-dd", Invariant),
        _ => Convert.ToString(value, Invariant)
    };
    private static IReadOnlyList<string> Strings(object? value) => value switch
    {
        string text when !string.IsNullOrWhiteSpace(text) => [text],
        IEnumerable<string> items => items.Where(item => !string.IsNullOrWhiteSpace(item)).ToArray(),
        IEnumerable<object?> items => items.Select(item => Convert.ToString(item, Invariant) ?? string.Empty)
            .Where(item => item.Length > 0).ToArray(),
        IEnumerable items => items.Cast<object?>().Select(item => Convert.ToString(item, Invariant) ?? string.Empty)
            .Where(item => item.Length > 0).ToArray(),
        _ => []
    };
    /// <summary>
    /// The source shown to the user: the provider only, and nothing for Noavaran, the product's default source. Report
    /// identities stay in the evidence and table rows for audit and are never part of the text.
    /// </summary>
    private static string Provenance(StructuredAnswerEvidence item) => item.Provider is null || IsDefaultProvider(item.Provider)
        ? "" : $"\nمنبع: {item.Provider}";

    internal static bool IsDefaultProvider(string? provider) =>
        string.Equals(provider, "NoavaranCurrentApi", StringComparison.Ordinal);
    private static string AppendWarnings(string text, IReadOnlyList<string> warnings) => warnings.Count == 0
        ? text : text + " هشدار: " + string.Join("؛ ", warnings) + ".";
}
