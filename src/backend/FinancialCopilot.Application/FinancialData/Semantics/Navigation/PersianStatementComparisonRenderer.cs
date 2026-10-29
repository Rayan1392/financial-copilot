using System.Globalization;

namespace FinancialCopilot.Application.FinancialData.Semantics.Navigation;

/// <summary>
/// Renders a cross-company statement comparison or ranking in Persian. Every value, label, unit and
/// period comes from the executor's policy-resolved rows; nothing is computed here, and a missing or
/// ambiguous member always yields a typed non-answer rather than a partial verdict.
/// </summary>
internal static class PersianStatementComparisonRenderer
{
    public static bool Applies(StructuredQueryPlan plan) => StatementComparisonPlans.IsStatementComparison(plan);

    public static PersianStructuredQueryResponse Render(StructuredQueryAnswerRequest request, StructuredQueryPlan plan,
        StructuredQueryExecutionResult execution)
    {
        var title = string.IsNullOrWhiteSpace(plan.Sources[0].TitleFa) ? "قلم مالی درخواستی" : plan.Sources[0].TitleFa!.Trim();
        var unit = UnitLabel(execution.EffectiveUnit);
        var diagnostics = (request.Diagnostics ?? []).Concat(execution.DiagnosticCode is null ? [] : [execution.DiagnosticCode])
            .Distinct(StringComparer.Ordinal).ToArray();
        var population = string.Equals(plan.PopulationCoveragePolicyId, StatementComparisonPolicyIds.AuthorizedCompanyUniverse,
            StringComparison.Ordinal);
        var rows = execution.Rows;
        var evidence = rows.Select(Evidence).ToArray();
        var tableRows = rows.Select(row => (IReadOnlyDictionary<string, string>)new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Rank"] = Convert.ToString(row.GetValueOrDefault("Rank"), CultureInfo.InvariantCulture) ?? string.Empty,
            ["Company"] = Label(row),
            ["Value"] = Convert.ToString(row.GetValueOrDefault("Value"), CultureInfo.InvariantCulture) ?? string.Empty,
            ["Unit"] = execution.EffectiveUnit ?? string.Empty,
            ["PeriodStart"] = DateText(row.GetValueOrDefault("PeriodStart")) ?? string.Empty,
            ["PeriodEnd"] = DateText(row.GetValueOrDefault("PeriodEnd")) ?? string.Empty,
            ["PeriodType"] = Convert.ToString(row.GetValueOrDefault("PeriodType"), CultureInfo.InvariantCulture) ?? string.Empty,
            ["IsComposing"] = Convert.ToString(row.GetValueOrDefault("IsComposing"), CultureInfo.InvariantCulture) ?? string.Empty
        }).ToArray();

        var status = execution.Status switch
        {
            StructuredQueryExecutionStatus.Succeeded => StructuredAnswerStatus.Answered,
            StructuredQueryExecutionStatus.NoData => StructuredAnswerStatus.NoData,
            StructuredQueryExecutionStatus.Ambiguous => StructuredAnswerStatus.Ambiguous,
            StructuredQueryExecutionStatus.Incomplete or StructuredQueryExecutionStatus.Truncated => StructuredAnswerStatus.Incomplete,
            _ => StructuredAnswerStatus.Failed
        };
        var partialPopulation = execution.Status == StructuredQueryExecutionStatus.Incomplete
            && execution.DiagnosticCode == "population_ranking_authorized_company_coverage_incomplete" && rows.Count > 0;
        string text;
        if (status == StructuredAnswerStatus.Answered || partialPopulation)
        {
            text = RenderResult(plan, title, unit, rows, population, partialPopulation, AppliedFilters(execution.Warnings));
            var notes = Notes(execution.Warnings, title);
            if (notes.Length > 0) text += "\n" + notes;
        }
        else
        {
            text = FailureText(execution, title, unit, rows, AppliedFilters(execution.Warnings));
            evidence = status == StructuredAnswerStatus.Incomplete && rows.Count > 0
                ? evidence.Select(item => item with { EvidenceStatus = "partial-not-a-comparison" }).ToArray()
                : evidence.Select(item => item with { EvidenceStatus = "not-answerable" }).ToArray();
        }
        return new(status, text, execution.EffectiveUnit,
            status == StructuredAnswerStatus.Answered ? request.FailureStage : request.FailureStage ?? "comparison_validation",
            execution.Warnings, evidence, status == StructuredAnswerStatus.Answered || partialPopulation ? tableRows : [],
            diagnostics);
    }

    private static string RenderResult(StructuredQueryPlan plan, string title, string unit,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, bool population, bool partial,
        IReadOnlyList<AppliedFilter> filters)
    {
        if (filters.Count > 0) return RenderFilterResult(title, unit, rows, population, partial, filters);
        var first = rows[0];
        var ranked = first.GetValueOrDefault("RankingApplied") is true;
        var descending = plan.Sort.Count == 0 || plan.Sort[0].Direction == PlanSortDirection.Descending;
        var period = PeriodText(first);
        var basis = first.GetValueOrDefault("IsComposing") is true ? "صورت‌های مالی تلفیقی" : "صورت‌های مالی جداگانه (غیرتلفیقی)";
        var provider = Convert.ToString(first.GetValueOrDefault("ProviderName"), CultureInfo.InvariantCulture);
        var lines = rows.Select(row =>
        {
            var tie = row.GetValueOrDefault("TiedWithOthers") is true ? " (هم‌رتبه / مقدار برابر)" : string.Empty;
            var prefix = ranked ? $"{PersianStructuredQueryResponseGenerator.Digits(Convert.ToString(row["Rank"], CultureInfo.InvariantCulture)!)}. " : "- ";
            return $"{prefix}{Label(row)}: {Value(row)} {unit}{tie}";
        });
        string header;
        if (ranked)
        {
            var order = descending ? "از بیشترین به کمترین" : "از کمترین به بیشترین";
            var subject = population
                ? $"شرکت‌های دامنهٔ مجاز شما ({PersianStructuredQueryResponseGenerator.Digits(rows.Count.ToString(CultureInfo.InvariantCulture))} شرکت دارای داده)"
                : "شرکت‌های درخواستی";
            header = $"رتبه‌بندی {subject} بر اساس {title} {order}، در آخرین دورهٔ گزارش مشترک ({period}):";
        }
        else
            header = $"مقایسهٔ {title} در آخرین دورهٔ گزارش مشترک ({period}):";
        var body = header + "\n" + string.Join("\n", lines);
        if (!ranked) body += "\n" + Verdict(title, unit, rows);
        body += $"\nمبنا: {basis}؛ واحد: {unit}" + (string.IsNullOrWhiteSpace(provider) ? "." : $"؛ منبع: {provider}.");
        if (population)
            body += partial
                ? "\nاین رتبه‌بندی جزئی است: پوشش دامنهٔ شرکت‌های مجاز شما کامل اعلام نشده است و جایگاه شرکت‌های خارج از این فهرست مشخص نیست."
                : "\nاین رتبه‌بندی همهٔ شرکت‌های دامنهٔ مجاز شما را که در این دوره دادهٔ معتبر دارند پوشش می‌دهد.";
        return body;
    }

    /// <summary>
    /// Lists the companies whose values satisfy every applied condition, ordered by the listed value, with
    /// each condition's own value and unit; the conditions themselves are stated in the header.
    /// </summary>
    private static string RenderFilterResult(string title, string unit, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        bool population, bool partial, IReadOnlyList<AppliedFilter> filters)
    {
        var first = rows[0];
        var ranked = first.GetValueOrDefault("RankingApplied") is true;
        var basis = first.GetValueOrDefault("IsComposing") is true ? "صورت‌های مالی تلفیقی" : "صورت‌های مالی جداگانه (غیرتلفیقی)";
        var provider = Convert.ToString(first.GetValueOrDefault("ProviderName"), CultureInfo.InvariantCulture);
        var subject = population ? "شرکت‌های دامنهٔ مجاز شما" : "شرکت‌های درخواستی";
        var header = $"{subject} با شرط {Conditions(filters)}، در آخرین دورهٔ گزارش مشترک ({PeriodText(first)}):";
        var lines = rows.Select(row =>
        {
            var prefix = ranked ? $"{PersianStructuredQueryResponseGenerator.Digits(Convert.ToString(row["Rank"], CultureInfo.InvariantCulture)!)}. " : "- ";
            var others = filters.Where(filter => filter.Title != title)
                .Select(filter => $"؛ {filter.Title}: {(row.GetValueOrDefault(filter.ValueColumn) is { } value ? PersianStructuredQueryResponseGenerator.FormatValue(value) : "ثبت نشده")} {UnitLabel(filter.Unit)}");
            return $"{prefix}{Label(row)}: {title} {Value(row)} {unit}{string.Concat(others)}";
        });
        var body = header + "\n" + string.Join("\n", lines);
        body += $"\nمبنا: {basis}؛ واحد {title}: {unit}" + (string.IsNullOrWhiteSpace(provider) ? "." : $"؛ منبع: {provider}.");
        if (population)
            body += partial
                ? "\nاین فهرست جزئی است: پوشش دامنهٔ شرکت‌های مجاز شما کامل اعلام نشده است."
                : "\nهمهٔ شرکت‌های دامنهٔ مجاز شما که در این دوره دادهٔ معتبر دارند بررسی شده‌اند.";
        return body;
    }

    /// <summary>A threshold condition as stated by the compiled query (title, operator, threshold, unit).</summary>
    private sealed record AppliedFilter(int Index, string Operator, string Threshold, string Unit, string Title)
    {
        public string ValueColumn => StatementComparisonFilter.ValueColumnPrefix + Index.ToString(CultureInfo.InvariantCulture);
    }

    private static IReadOnlyList<AppliedFilter> AppliedFilters(IReadOnlyList<string> warnings) => warnings
        .Where(warning => warning.StartsWith(CrossCompanyStatementComparisonPolicy.WarningFilterApplied + ":", StringComparison.Ordinal))
        .Select(warning => warning.Split(':', 6))
        .Where(parts => parts.Length == 6 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
        .Select(parts => new AppliedFilter(int.Parse(parts[1], CultureInfo.InvariantCulture), parts[2], parts[3], parts[4], parts[5].Trim()))
        .OrderBy(filter => filter.Index).ToArray();

    private static string Conditions(IReadOnlyList<AppliedFilter> filters) => string.Join(" و ", filters.Select(filter =>
    {
        var comparison = filter.Operator switch
        {
            nameof(PlanFilterOperator.GreaterThan) => "بیشتر از",
            nameof(PlanFilterOperator.GreaterThanOrEqual) => "بیشتر یا مساوی",
            nameof(PlanFilterOperator.LessThan) => "کمتر از",
            nameof(PlanFilterOperator.LessThanOrEqual) => "کمتر یا مساوی",
            _ => "برابر با"
        };
        var threshold = decimal.TryParse(filter.Threshold, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? PersianStructuredQueryResponseGenerator.FormatValue(parsed) : filter.Threshold;
        return $"«{filter.Title} {comparison} {threshold} {UnitLabel(filter.Unit)}»";
    }));

    /// <summary>States which company has the highest and lowest value; equal values are named as a tie.</summary>
    private static string Verdict(string title, string unit, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows)
    {
        var values = rows.Select(row => (Label: Label(row), Value: Convert.ToDecimal(row["Value"], CultureInfo.InvariantCulture), Row: row))
            .ToArray();
        var max = values.Max(item => item.Value);
        var min = values.Min(item => item.Value);
        if (max == min)
            return $"مقدار {title} همهٔ این شرکت‌ها برابر است ({Value(values[0].Row)} {unit}).";
        var top = values.Where(item => item.Value == max).Select(item => item.Label).ToArray();
        var bottom = values.Where(item => item.Value == min).Select(item => item.Label).ToArray();
        var topText = top.Length == 1 ? $"بیشترین {title} متعلق به {top[0]} است" : $"{string.Join(" و ", top)} با مقدار برابر بیشترین {title} را دارند";
        var bottomText = bottom.Length == 1 ? $"کمترین متعلق به {bottom[0]} است" : $"{string.Join(" و ", bottom)} با مقدار برابر کمترین را دارند";
        var topValue = Value(values.First(item => item.Value == max).Row);
        var bottomValue = Value(values.First(item => item.Value == min).Row);
        return $"{topText} ({topValue} {unit})؛ {bottomText} ({bottomValue} {unit}).";
    }

    private static string FailureText(StructuredQueryExecutionResult execution, string title, string unit,
        IReadOnlyList<IReadOnlyDictionary<string, object?>> rows, IReadOnlyList<AppliedFilter> filters)
    {
        var warned = (string prefix) => execution.Warnings.Where(warning => warning.StartsWith(prefix + ":", StringComparison.Ordinal))
            .Select(warning => warning[(prefix.Length + 1)..]).ToArray();
        string Names(IEnumerable<string> names) => string.Join("، ", names);
        var ambiguousCompanies = warned(CrossCompanyStatementComparisonPolicy.WarningAmbiguousCompany);
        var who = ambiguousCompanies.Length == 0 ? "یکی از شرکت‌ها" : Names(ambiguousCompanies);
        switch (execution.DiagnosticCode)
        {
            case "comparison_company_statement_missing" or "comparison_companies_without_statements":
                return $"برای {Names(warned(CrossCompanyStatementComparisonPolicy.WarningCompanyWithoutStatements))} صورت مالی‌ای که {title} در آن گزارش شود ثبت نشده است؛ مقایسه بدون دادهٔ همهٔ شرکت‌ها انجام نمی‌شود.";
            case "comparison_company_value_missing":
            {
                var missing = Names(warned(CrossCompanyStatementComparisonPolicy.WarningCompanyValueMissing));
                var available = rows.Count == 0 ? string.Empty
                    : " مقادیر موجود (فقط برای اطلاع، بدون مقایسه): " + string.Join("؛ ", rows.Select(row => $"{Label(row)} {Value(row)} {unit}")) + ".";
                var period = rows.Count == 0 ? string.Empty : $" در دورهٔ {PeriodText(rows[0])}";
                return $"مقدار {title}{period} برای {missing} ثبت نشده است و مقدار گم‌شده صفر فرض نمی‌شود؛ مقایسه انجام نمی‌شود.{available}";
            }
            case "no_common_reporting_period":
            {
                var latest = warned(CrossCompanyStatementComparisonPolicy.WarningCompanyLatestPeriod).Select(item =>
                {
                    var split = item.LastIndexOf(':');
                    return split < 0 ? item : $"{item[..split]} تا {JalaliText(item[(split + 1)..])}";
                });
                return $"این شرکت‌ها دورهٔ گزارش مشترکی برای {title} ندارند و مقادیر دوره‌های متفاوت با هم مقایسه نمی‌شوند. آخرین دورهٔ گزارش هر شرکت: {Names(latest)}.";
            }
            case "statement_scope_unspecified_across_consolidation_bases":
                return $"برای {who} هم صورت مالی تلفیقی و هم صورت مالی جداگانه وجود دارد؛ لطفاً مشخص کنید مقایسه بر اساس صورت‌های تلفیقی باشد یا جداگانه.";
            case "cross_company_consolidation_basis_mismatch":
                return "مبنای صورت‌های مالی شرکت‌ها یکسان نیست (برخی تلفیقی و برخی جداگانه)؛ بدون مبنای مشترک مقایسه نمی‌شود. لطفاً مبنای تلفیقی یا جداگانه را مشخص کنید.";
            case "requested_item_values_conflict":
                return $"برای {who} چند صورت مالی معتبر در همین دوره با مقادیر متفاوت {title} وجود دارد؛ هیچ‌کدام به‌دلخواه انتخاب نمی‌شود و مقایسه انجام نمی‌شود.";
            case "different_reporting_period_scopes" or "cross_company_reporting_periods_differ" or "reporting_period_evidence_unavailable":
                return $"دوره‌های گزارش {title} برای {who} یکتا و هم‌نوع نیستند؛ مقایسه انجام نمی‌شود.";
            case "cross_company_units_incompatible":
                return $"واحد گزارش {title} برای شرکت‌ها یکسان نیست؛ مقادیر با واحدهای متفاوت مقایسه نمی‌شوند.";
            case "comparison_population_without_statements" or "comparison_values_missing_for_all_companies":
                return $"در دامنهٔ شرکت‌های مجاز، مقدار معتبری برای {title} پیدا نشد.";
            case "statement_filter_no_matching_company":
            {
                var notes = Notes(execution.Warnings, title);
                return $"در آخرین دورهٔ گزارش مشترک، هیچ شرکتی شرط {Conditions(filters)} را ندارد." + (notes.Length > 0 ? "\n" + notes : string.Empty);
            }
            case "statement_filter_value_missing":
                return $"برای {Names(warned(CrossCompanyStatementComparisonPolicy.WarningFilterValueMissing))} مقدار لازم برای بررسی شرط {Conditions(filters)} ثبت نشده است و مقدار گم‌شده صفر فرض نمی‌شود؛ نتیجهٔ فیلتر ارائه نمی‌شود.";
            case "result_row_limit_exceeded" or "result_byte_limit_exceeded":
                return "حجم داده‌های لازم برای این مقایسه از سقف مجاز یک پرس‌وجو بیشتر است؛ نتیجهٔ ناقص ارائه نمی‌شود.";
            default:
                return execution.Status switch
                {
                    StructuredQueryExecutionStatus.Ambiguous => "داده‌های موجود برای یک مقایسهٔ یکتا کافی نیستند؛ نتیجه مبهم است.",
                    StructuredQueryExecutionStatus.NoData => $"برای این شرکت‌ها دادهٔ واجد شرایطی از {title} پیدا نشد.",
                    StructuredQueryExecutionStatus.Incomplete => "داده یا پوشش لازم برای مقایسهٔ کامل نیست؛ عددی گزارش نمی‌کنم.",
                    _ => "اجرای امن پرس‌وجو کامل نشد؛ نتیجهٔ مالی ارائه نمی‌شود."
                };
        }
    }

    private static string Notes(IReadOnlyList<string> warnings, string title)
    {
        var notes = new List<string>();
        foreach (var warning in warnings)
        {
            var split = warning.IndexOf(':');
            var code = split < 0 ? warning : warning[..split];
            var detail = split < 0 ? string.Empty : warning[(split + 1)..];
            switch (code)
            {
                case CrossCompanyStatementComparisonPolicy.WarningNewerPeriodAvailable:
                {
                    var last = detail.LastIndexOf(':');
                    notes.Add(last < 0 ? detail : $"برای {detail[..last]} دورهٔ جدیدتری تا {JalaliText(detail[(last + 1)..])} گزارش شده که چون همهٔ شرکت‌ها آن را ندارند در مقایسه استفاده نشد.");
                    break;
                }
                case CrossCompanyStatementComparisonPolicy.WarningCompanyValueMissing:
                    notes.Add($"{detail} در این دوره مقدار {title} ثبت‌شده ندارد و رتبه‌بندی نشد (صفر فرض نمی‌شود).");
                    break;
                case CrossCompanyStatementComparisonPolicy.WarningPopulationMembersWithoutStatements:
                    notes.Add($"{PersianStructuredQueryResponseGenerator.Digits(detail)} شرکت از دامنهٔ مجاز صورت مالی مرتبطی ندارند و در رتبه‌بندی نیامده‌اند.");
                    break;
                case CrossCompanyStatementComparisonPolicy.WarningFilterExcluded:
                    notes.Add($"{PersianStructuredQueryResponseGenerator.Digits(detail)} شرکت دیگر دارای داده، شرط را نداشتند و در فهرست نیامده‌اند.");
                    break;
                case CrossCompanyStatementComparisonPolicy.WarningFilterValueMissing:
                    notes.Add($"{detail} مقدار ثبت‌شده‌ای برای بررسی شرط ندارد و در فهرست نیامد (صفر فرض نمی‌شود).");
                    break;
                case CrossCompanyStatementComparisonPolicy.WarningTopNExtendedByTies:
                    notes.Add("به‌دلیل مقادیر برابر در مرز رتبه‌بندی، همهٔ شرکت‌های هم‌رتبه نمایش داده شده‌اند.");
                    break;
            }
        }
        return string.Join("\n", notes.Select(note => "توضیح: " + note));
    }

    private static StructuredAnswerEvidence Evidence(IReadOnlyDictionary<string, object?> row)
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in new[] { "Value", "Rank", "TiedWithOthers", "SourceUnit", "PeriodType", "IsComposing", "MatchingStatementCount" })
            if (row.TryGetValue(key, out var fact) && fact is not null) facts[key] = Convert.ToString(fact, CultureInfo.InvariantCulture) ?? string.Empty;
        var statements = row.GetValueOrDefault("StatementIdentities") is IEnumerable<object?> identities
            ? identities.Select(item => Convert.ToString(item, CultureInfo.InvariantCulture) ?? string.Empty).Where(item => item.Length > 0).ToArray()
            : row.GetValueOrDefault("StatementId") is { } single ? [Convert.ToString(single, CultureInfo.InvariantCulture)!] : [];
        return new(Label(row), null, DateText(row.GetValueOrDefault("PeriodStart")), DateText(row.GetValueOrDefault("PeriodEnd")),
            Convert.ToString(row.GetValueOrDefault("ProviderName"), CultureInfo.InvariantCulture), statements,
            row.GetValueOrDefault("SourceItemCatalogId") is { } source ? [Convert.ToString(source, CultureInfo.InvariantCulture)!] : [],
            facts);
    }

    private static string Label(IReadOnlyDictionary<string, object?> row) =>
        row.GetValueOrDefault("CompanyLabel") as string
        ?? row.GetValueOrDefault("CompanySymbol") as string
        ?? row.GetValueOrDefault("CompanyName") as string
        ?? "شرکت با هویت نامشخص";

    private static string Value(IReadOnlyDictionary<string, object?> row) =>
        row.GetValueOrDefault("Value") is { } value ? PersianStructuredQueryResponseGenerator.FormatValue(value) : "ثبت نشده";

    /// <summary>The shared statement period in Jalali from the row's period type and bounds (rows keep their ISO dates).</summary>
    private static string PeriodText(IReadOnlyDictionary<string, object?> row) => PersianReportingPeriodText.StatementPeriod(
        Convert.ToString(row.GetValueOrDefault("PeriodType"), CultureInfo.InvariantCulture),
        PersianReportingPeriodText.Parse(row.GetValueOrDefault("PeriodStart")),
        PersianReportingPeriodText.Parse(row.GetValueOrDefault("PeriodEnd")));

    private static string JalaliText(string? isoDate) => PersianReportingPeriodText.Parse(isoDate) is { } date
        ? PersianReportingPeriodText.Date(date)
        : PersianStructuredQueryResponseGenerator.Digits(isoDate ?? "?");

    private static string? DateText(object? value) => value switch
    {
        null => null,
        DateOnly date => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime dateTime => dateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture)
    };

    private static string UnitLabel(string? unit) => unit switch
    {
        "MillionIRR" => "میلیون ریال",
        "BillionIRR" => "میلیارد ریال",
        "IRR" => "ریال",
        "IRRPerShare" => "ریال به ازای هر سهم",
        "Percent" or "percent" => "درصد",
        null or "" => "واحد نامشخص",
        _ => unit
    };
}
