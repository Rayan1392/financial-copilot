using System.Diagnostics;
using FinancialCopilot.Application.FinancialData.BalanceSheetReport;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Application.FinancialData.CashFlowReport;

/// <summary>Composes the bounded, persisted cash-flow report without a model or provider call.</summary>
public sealed class CashFlowReportQueryUseCase(
    ICashFlowStatementReader reader, ICompanyResolutionOutcomeService companyResolution,
    IQuarterlyFinancialReportsRepository classificationReader, BankingFinancialReportsConfiguration banking,
    TimeProvider timeProvider) : ICashFlowReportQueryUseCase
{
    public async Task<CashFlowReportResult> ExecuteAsync(CashFlowReportRequest request, CancellationToken cancellationToken = default)
    {
        var readTime = TimeSpan.Zero;
        var statementsRead = 0;
        var result = await ExecuteCoreAsync(request, cancellationToken, elapsed => readTime += elapsed, count => statementsRead += count);
        Activity.Current?.SetTag("feature161.template", result.TemplateId);
        Activity.Current?.SetTag("feature161.outcome", result.Outcome);
        Activity.Current?.SetTag("feature161.view", request.View);
        Activity.Current?.SetTag("feature161.focus", request.Focus);
        Activity.Current?.SetTag("feature161.metric", request.MetricId);
        Activity.Current?.SetTag("feature161.analysis_topic", request.AnalysisTopic);
        Activity.Current?.SetTag("feature161.verification_status", result.Verification.Status);
        Activity.Current?.SetTag("feature161.failed_checks", result.Verification.Checks.Count(x => x.State == "failed"));
        Activity.Current?.SetTag("feature161.warnings", result.Warnings.Count);
        Activity.Current?.SetTag("feature161.residual_nonzero_count", result.Sections.SelectMany(x => x.Rows).Count(x => x.Kind == "residual"));
        Activity.Current?.SetTag("feature161.columns", result.Columns.Count);
        Activity.Current?.SetTag("feature161.statements_read", statementsRead);
        Activity.Current?.SetTag("feature161.read_latency_ms", readTime.TotalMilliseconds);
        return result;
    }

    private async Task<CashFlowReportResult> ExecuteCoreAsync(CashFlowReportRequest request, CancellationToken ct,
        Action<TimeSpan> onRead, Action<int> onStatementsRead)
    {
        async Task<T> Read<T>(Func<Task<T>> action)
        {
            var started = Stopwatch.GetTimestamp();
            try { return await action(); }
            finally { onRead(Stopwatch.GetElapsedTime(started)); }
        }
        var now = timeProvider.GetUtcNow();
        var company = new CashFlowReportCompany(request.CompanyQuery, null, request.ProviderName, null);
        CashFlowReportResult Unavailable(string outcome, string? reason = null, string? message = null,
            string? template = null, bool consolidated = false, IReadOnlyList<DateOnly>? nearest = null) =>
            new(CashFlowReportContract.Version, now, outcome, reason, message, nearest ?? [], consolidated, template,
                company, request, new Dictionary<Guid, CashFlowReportStatement>(), null, [], [], null, [], null,
                new("unverified", []), [], [], null);

        if (request.ProviderName != CashFlowReportContract.ProviderName)
            return Unavailable("provider_unavailable", "unsupported_provider", "صورت جریان وجوه نقد از این منبع در دسترس نیست.");
        if (request.View is not ("default" or "annual" or "quarterly") || request.Focus is not ("report" or "metric" or "analysis") ||
            request.RequestedPeriodMonths is not (null or 3 or 6 or 9 or 12) || request.RequestedFiscalYear is <= 0)
            return Unavailable("unsupported_request", "unsupported_view_or_focus", "این شکل گزارش پشتیبانی نمی‌شود.");

        var resolution = await companyResolution.ResolveAsync(request.CompanyQuery, request.ProviderName, ct);
        if (resolution.Status != CompanyResolutionStatus.Resolved || resolution.Company is null)
        {
            var outcome = resolution.Status switch
            {
                CompanyResolutionStatus.Ambiguous => "company_ambiguous",
                CompanyResolutionStatus.UnsupportedProviderCompany => "provider_unavailable",
                _ => "company_unresolved"
            };
            return Unavailable(outcome, resolution.Reason ?? resolution.Status.ToString(), "شرکت یا منبع داده برای این درخواست در دسترس نیست.");
        }
        var resolved = resolution.Company;
        company = new(resolved.CompanySymbol ?? resolved.TseSymbol ?? resolved.Ticker ?? request.CompanyQuery,
            resolved.Name, request.ProviderName, resolved.ExternalCompanyId);
        var groups = await classificationReader.GetEligibleCompanyGroupsAsync(resolved.Id, request.ProviderName, resolved.ExternalCompanyId, ct);
        if (groups.Count != 1 || groups[0] is not { } group)
            return Unavailable("company_classification_unavailable", groups.Count == 0 ? "no_eligible_group" : "contradictory_group_evidence",
                "طبقه‌بندی صنعت برای انتخاب قالب صورت جریان وجوه نقد در دسترس نیست.");
        var template = group == banking.IndustryGroupId ? CashFlowLineRegistry.BankingTemplate : CashFlowLineRegistry.GeneralTemplate;
        var headerSet = await Read(() => reader.ReadHeadersAsync(request.ProviderName, resolved.ExternalCompanyId, ct));
        var headers = headerSet.Statements.ToList();
        var truncated = headers.Count >= CashFlowReportContract.HeaderWindow;
        var oldest = headers.Count == 0 ? (DateOnly?)null : headers.Min(x => x.PeriodEnd);
        if (truncated && oldest is { } boundary) headers.RemoveAll(x => x.PeriodEnd == boundary);
        bool Outside(DateOnly date) => truncated && oldest is { } earliest && date <= earliest && headers.All(x => x.PeriodEnd != date);
        if (truncated && headers.Count == 0 && oldest is { } onlyDate)
            headers.AddRange(await Read(() => reader.ReadHeadersAtDatesAsync(request.ProviderName, resolved.ExternalCompanyId, [onlyDate], ct)));
        if (request.RequestedDate is { } explicitDate && Outside(explicitDate))
            headers.AddRange(await Read(() => reader.ReadHeadersAtDatesAsync(request.ProviderName, resolved.ExternalCompanyId, [explicitDate], ct)));

        var periodRequest = new CashFlowPeriodRequest(request.View, request.RequestedPeriodMonths, request.RequestedFiscalYear,
            request.PeriodCount, request.RequestedDate, request.PreviousReport);
        CashFlowPeriodSelection Preliminary() => CashFlowPeriodSelector.Select(headers,
            headers.DistinctBy(x => x.StatementId).ToDictionary(x => x.StatementId, _ => (IReadOnlyList<CashFlowItemObservation>)[]), periodRequest, truncated, headerSet.FiscalEvidence);
        var preliminary = Preliminary();
        if (preliminary.Outcome == "no_statement_for_period" && request.RequestedPeriodMonths is { } requestedMonths &&
            request.RequestedFiscalYear is { } requestedYear)
        {
            var target = headerSet.FiscalEvidence.Select(x => FiscalCalendarPolicy.TryParseJalaliDate(x.JalaliFiscalYearEnd, out var end)
                    ? (DateOnly?)end : null)
                .Where(end => end.HasValue && FiscalCalendarPolicy.FiscalYearLabel(FiscalCalendarPolicy.FiscalStartFromYearEnd(end.Value)) == requestedYear)
                .Select(end => FiscalCalendarPolicy.JalaliEndAfterMonths(FiscalCalendarPolicy.FiscalStartFromYearEnd(end!.Value), requestedMonths))
                .Where(Outside).Distinct().ToArray();
            if (target.Length > 0)
            {
                headers.AddRange(await Read(() => reader.ReadHeadersAtDatesAsync(request.ProviderName, resolved.ExternalCompanyId, target, ct)));
                preliminary = Preliminary();
            }
        }
        if (preliminary.Outcome == "no_standalone_cash_flow")
        {
            var consolidated = await Read(() => reader.ConsolidatedExistsAsync(request.ProviderName, resolved.ExternalCompanyId, ct));
            return Unavailable(preliminary.Outcome, "no_eligible_standalone_statement", "صورت جریان وجوه نقد جداگانه یافت نشد.", template, consolidated);
        }
        if (preliminary.Outcome != "available")
            return Unavailable(preliminary.Outcome, preliminary.Outcome, "دوره درخواستی یا واحد داده در دسترس نیست.", template, nearest: preliminary.NearestPeriods);

        var outsideDates = preliminary.Columns.Where(x => x.Statement is null && x.PeriodEnd != default && Outside(x.PeriodEnd))
            .Select(x => x.PeriodEnd).Distinct().ToArray();
        if (outsideDates.Length > 0)
        {
            headers.AddRange(await Read(() => reader.ReadHeadersAtDatesAsync(request.ProviderName, resolved.ExternalCompanyId, outsideDates, ct)));
            preliminary = Preliminary();
        }

        var orderedHeaders = new[] { preliminary.Anchor }.Concat(preliminary.Columns.SelectMany(x => new[] { x.Statement, x.Predecessor }))
            .Where(x => x is not null).Cast<CashFlowStatementHeader>().DistinctBy(x => (x.PeriodEnd, x.PeriodType)).ToArray();
        var readIds = new HashSet<Guid>();
        var omitted = new HashSet<(DateOnly, string)>();
        foreach (var header in orderedHeaders)
        {
            var versions = headers.Where(x => x.PeriodEnd == header.PeriodEnd && x.PeriodType == header.PeriodType && !x.IsComposing &&
                (x.SourceIsComposing is null || x.SourceIsComposing == x.IsComposing) && x.AssumedScale == "MillionRials");
            var ranked = FinancialStatementVersionPolicy.Rank(versions, x => new(x.IsAudited, x.IsRepresented, x.PublishedAt));
            var peers = FinancialStatementVersionPolicy.TopTieGroup(ranked, x => new(x.IsAudited, x.IsRepresented, x.PublishedAt));
            var ids = peers.Select(x => x.StatementId).Where(x => !readIds.Contains(x)).ToArray();
            if (readIds.Count + ids.Length > CashFlowReportContract.MaxCashFlowStatements)
            {
                if (header.StatementId == preliminary.Anchor?.StatementId)
                    return Unavailable("unsupported_request", "too_many_report_versions", "تعداد نسخه‌های هم‌اولویت برای پردازش این صورت بیش از حد مجاز است.", template);
                omitted.Add((header.PeriodEnd, header.PeriodType));
                continue;
            }
            readIds.UnionWith(ids);
        }
        var read = await Read(() => reader.ReadLineItemsAsync(request.ProviderName, readIds.ToArray(), CashFlowLineRegistry.ReadableSourceItemIds, ct));
        onStatementsRead(readIds.Count);
        var lines = readIds.ToDictionary(id => id, id => (IReadOnlyList<CashFlowItemObservation>)(read.GetValueOrDefault(id) ?? [])
            .Where(item => !CashFlowLineRegistry.ExcludedValueItems.Contains(item.SourceItemId)).ToArray());
        var selected = CashFlowPeriodSelector.Select(headers, lines, periodRequest, truncated, headerSet.FiscalEvidence);
        if (selected.Outcome != "available" || selected.Anchor is null)
            return Unavailable(selected.Outcome, selected.Outcome, "انتخاب نسخه صورت مالی ممکن نشد.", template, nearest: selected.NearestPeriods);

        CashFlowPresentationSelection Presentation(CashFlowStatementHeader header) => CashFlowPresentationPolicy.Classify(
            (lines.GetValueOrDefault(header.StatementId) ?? []).GroupBy(x => x.SourceItemId)
                .ToDictionary(x => x.Key, x => (IReadOnlyList<decimal?>)x.Select(item => item.Value).ToArray()));
        var anchorPresentation = Presentation(selected.Anchor);
        if (anchorPresentation.Outcome != CashFlowPresentationPolicy.Available)
            return Unavailable(anchorPresentation.Outcome, anchorPresentation.Outcome, "مبنای ارائه یا جمع‌های لازم در صورت جریان وجوه نقد موجود نیست.", template);

        var safeColumns = selected.Columns.Select(column =>
        {
            if (column.Statement is { } statement && omitted.Contains((statement.PeriodEnd, statement.PeriodType)))
                return column with { Statement = null, State = "unavailable", Reason = "too_many_report_versions" };
            if (column.Statement is { } current && Presentation(current).Outcome is var outcome && outcome != CashFlowPresentationPolicy.Available)
                return column with { Statement = null, State = "unavailable", Reason = outcome };
            if (column.Predecessor is { } predecessor && omitted.Contains((predecessor.PeriodEnd, predecessor.PeriodType)))
                return column with { Predecessor = null, State = "unavailable", Reason = "too_many_report_versions" };
            if (column.Predecessor is { } prior && Presentation(prior).Outcome != CashFlowPresentationPolicy.Available)
                return column with { Predecessor = null, State = "unavailable", Reason = "presentation_changed" };
            return column;
        }).ToArray();

        var built = CashFlowStatementBuilder.Build(template, safeColumns, lines);
        var income = template == CashFlowLineRegistry.GeneralTemplate && request.View == "default"
            ? await Read(() => reader.ReadIncomeStatementsAsync(request.ProviderName, resolved.ExternalCompanyId,
                safeColumns.Where(x => x.Statement is not null).Select(x => x.Statement!).DistinctBy(x => x.StatementId).Take(2).ToArray(), ct))
            : [];
        onStatementsRead(income.Count);
        return BuildResult(request, company, template, now, selected.Anchor, built, lines, income, anchorPresentation.Warnings, truncated);
    }

    private static CashFlowReportResult BuildResult(CashFlowReportRequest request, CashFlowReportCompany company, string template,
        DateTimeOffset now, CashFlowStatementHeader anchor, CashFlowBuiltStatement built,
        IReadOnlyDictionary<Guid, IReadOnlyList<CashFlowItemObservation>> lines,
        IReadOnlyList<CashFlowIncomeStatementObservation> income, IReadOnlyList<string> anchorWarnings, bool truncated)
    {
        var columns = built.Columns.Select((column, index) =>
        {
            var period = column.Period;
            var date = period.PeriodEnd == default ? (DateOnly?)null : period.PeriodEnd;
            var label = period.Kind == "standalone_quarter" && period.FiscalYear is { } fy
                ? $"فصل {BalanceSheetFormatting.PersianDigits((period.PeriodMonths / 3).ToString())} {BalanceSheetFormatting.PersianDigits(fy.ToString())}"
                : date is { } end ? BalanceSheetFormatting.JalaliDate(end) : "دوره مشابه سال قبل";
            return new CashFlowReportColumn($"column_{index}", period.Kind, label, period.FiscalYear,
                period.Kind == "standalone_quarter" ? period.PeriodMonths / 3 : null,
                period.Kind == "standalone_quarter" ? 3 : period.PeriodMonths, date,
                date is { } value ? FiscalCalendarPolicy.ToJalali(value) : null,
                period.Kind == "standalone_quarter" && period.PeriodMonths > 3 ? "ytd_difference" : "reported_ytd",
                new[] { period.Statement, period.Predecessor }.Where(x => x is not null).Cast<CashFlowStatementHeader>()
                    .Select(x => x.StatementId).Distinct().ToArray(), period.State, period.Reason);
        }).ToArray();

        CashFlowCell Raw(CashFlowStatementHeader? header, int item)
        {
            var selected = header is null ? [] : (lines.GetValueOrDefault(header.StatementId) ?? []).Where(x => x.SourceItemId == item).ToArray();
            var observed = CashFlowObservedValue.From(selected.Select(x => x.Value).ToArray());
            var state = observed.State switch { CashFlowValueState.Available => "available", CashFlowValueState.Ambiguous => "ambiguous", _ => "not_reported" };
            return new(observed.Value, state, state == "ambiguous" ? "duplicate_line_item" : null,
                new(header is null ? [] : [header.StatementId], selected.Select(x => x.LineItemId).ToArray(), [item]));
        }

        var verifications = built.Columns.Select((column, index) =>
        {
            var period = column.Period;
            var priorAnnual = period.FiscalYear is { } fiscalYear
                ? built.Columns.FirstOrDefault(x => x.Period.FiscalYear == fiscalYear - 1 && x.Period.PeriodMonths == 12 && x.Period.Statement is not null)
                : null;
            var quarterPair = period.Kind == "standalone_quarter" && period.PeriodMonths > 3;
            var observedWarnings = period.Statement is null ? [] : CashFlowPresentationPolicy.Classify(
                (lines.GetValueOrDefault(period.Statement.StatementId) ?? []).GroupBy(x => x.SourceItemId)
                    .ToDictionary(x => x.Key, x => (IReadOnlyList<decimal?>)x.Select(item => item.Value).ToArray())).Warnings;
            return CashFlowReconciliation.Verify(column.Values, priorAnnual?.Values.GetValueOrDefault(125),
                quarterPair ? Raw(period.Statement, 33) : null, quarterPair ? Raw(period.Predecessor, 33) : null,
                period.Statement?.StatementId == anchor.StatementId ? observedWarnings.Concat(anchorWarnings).Distinct().ToArray() : observedWarnings,
                quarterPair ? Raw(period.Statement, 33) : null);
        }).ToArray();
        var metrics = built.Columns.Select((column, index) => CashFlowMetricsCalculator.Calculate(template, column.Values,
            verifications[index], SelectIncome(column.Period.Statement, income), column.Period.Kind != "standalone_quarter")).ToArray();
        var rows = built.Rows.Select(row => new
        {
            row.Section,
            Row = new CashFlowReportRow(row.RowId, row.Label, row.Kind, row.SourceItemIds,
                template == CashFlowLineRegistry.BankingTemplate && row.SourceItemIds.Count == 1 && row.SourceItemIds[0] is 23 or 24
                    ? "طبق طبقه‌بندی کدال در فعالیت‌های تأمین مالی؛ به معنای تغییر سپرده‌ها نیست" : null,
                columns.Select((column, index) => (column.ColumnId, Cell: row.SourceItemIds.Count == 1 && row.SourceItemIds[0] == 35
                    ? verifications[index].ResolvedFx : row.Cells[index])).ToDictionary(x => x.ColumnId, x => x.Cell))
        }).ToArray();
        var sections = new[] { CashFlowSection.Operating, CashFlowSection.Investing, CashFlowSection.Financing, CashFlowSection.Reconciliation }
            .Select(section => new CashFlowReportSection(SectionKey(section), SectionLabel(section), rows.Where(row => row.Section == section).Select(row => row.Row).ToArray())).ToArray();

        CashFlowReportChanges? changes = null;
        if (request.View == "default" && columns.Length >= 2 && columns[1].State == "available")
        {
            var comparisons = rows.ToDictionary(x => x.Row.RowId, x =>
            {
                var current = x.Row.Cells[columns[0].ColumnId]; var baseline = x.Row.Cells[columns[1].ColumnId];
                if (current.Value is null || baseline.Value is null)
                    return new CashFlowReportChange(null, null, "unavailable", "component_unavailable");
                var amount = current.Value.Value - baseline.Value.Value;
                return new CashFlowReportChange(amount, baseline.Value > 0m ? amount / baseline.Value * 100m : null, "available",
                    baseline.Value == 0m ? "zero_baseline" : baseline.Value < 0m ? "negative_baseline" : null);
            });
            changes = new(columns[1].ColumnId, columns[0].ColumnId, comparisons);
        }

        var warnings = new List<CashFlowReportWarning>();
        var checks = new List<CashFlowReportCheck>();
        for (var i = 0; i < columns.Length; i++)
        {
            checks.AddRange(verifications[i].Checks.Select(x => new CashFlowReportCheck(x.CheckId, columns[i].ColumnId, x.State, x.Expected, x.Actual, x.Difference)));
            warnings.AddRange(verifications[i].Warnings.Select(code => new CashFlowReportWarning(code, "warning", columns[i].ColumnId, WarningMessage(code))));
            warnings.AddRange(rows.Where(x => x.Row.Kind == "residual" && x.Row.Cells[columns[i].ColumnId].Value is not null and not 0m)
                .Select(x => new CashFlowReportWarning("section_residual", "info", columns[i].ColumnId, "بخشی از مبلغ در ردیف سایر اقلام نمایش داده شده است.")));
        }
        var kpis = metrics.SelectMany((items, i) => items.Select(item => new CashFlowReportKpi(item.MetricId, MetricLabel(item.MetricId),
            columns[i].ColumnId, item.Value, item.Unit, item.State, item.Reason, item.Formula, item.Components))).ToArray();
        var anchorIndex = Array.FindIndex(built.Columns.ToArray(), x => x.Period.Statement?.StatementId == anchor.StatementId);
        if (anchorIndex < 0) anchorIndex = 0;
        var anchorVerification = verifications[anchorIndex];
        var waterfall = request.View == "default" ? CashFlowWaterfallBuilder.Build(template, built.Columns[anchorIndex].Values, anchorVerification, columns[anchorIndex].ColumnId) : null;
        var analysis = request.Focus == "analysis" ? CashFlowAnalysisFacts.Build(request.AnalysisTopic ?? "overview", columns[anchorIndex].ColumnId,
            template, metrics[anchorIndex], metrics.Length > 1 && request.View == "default" ? metrics[1] : null,
            request.View == "quarterly" ? metrics.SelectMany(x => x).ToArray() : null) : null;
        var statements = built.Columns.SelectMany(x => new[] { x.Period.Statement, x.Period.Predecessor }).Append(anchor)
            .Where(x => x is not null).Cast<CashFlowStatementHeader>().DistinctBy(x => x.StatementId).ToDictionary(x => x.StatementId,
                x => new CashFlowReportStatement(x.StatementId, x.ExternalStatementId, x.PeriodEnd, FiscalCalendarPolicy.ToJalali(x.PeriodEnd),
                    x.PeriodType, CashFlowPeriodSelector.PeriodMonths(x.PeriodType), x.IsAudited, x.IsRepresented, x.PublishedAt,
                    x.JalaliFiscalYearEnd, "codal", "million_irr"));
        var notices = new List<string>();
        if (template == CashFlowLineRegistry.BankingTemplate)
            notices.Add("در بانک‌ها جریان نقد عملیاتی عمدتاً از تغییر سپرده‌ها و تسهیلات اعطایی ناشی می‌شود و با سود خالص یا جریان نقد آزاد شرکت‌های تولیدی قابل مقایسه نیست. این صورت، شاخص‌های نقدینگی نظارتی بانک را نشان نمی‌دهد.");
        if (truncated) notices.Add("پنجره صورت‌های مالی موجود محدود است؛ برخی دوره‌های قدیمی‌تر ممکن است نمایش داده نشوند.");
        return new(CashFlowReportContract.Version, now, "available", null, null, [], false, template, company, request, statements,
            anchor.StatementId, columns, sections, changes, kpis, waterfall,
            new(anchorVerification.Status, checks), warnings, notices, analysis);
    }

    private static CashFlowIncomeStatementValues? SelectIncome(CashFlowStatementHeader? period,
        IReadOnlyList<CashFlowIncomeStatementObservation> candidates)
    {
        if (period is null) return null;
        var matches = candidates.Where(x => x.Header.PeriodEnd == period.PeriodEnd && x.Header.PeriodType == period.PeriodType &&
            !x.Header.IsComposing && (x.Header.SourceIsComposing is null || x.Header.SourceIsComposing == x.Header.IsComposing) &&
            x.Header.AssumedScale == "MillionRials");
        var ordered = FinancialStatementVersionPolicy.Rank(matches, x => new(x.Header.IsAudited, x.Header.IsRepresented, x.Header.PublishedAt));
        var tied = FinancialStatementVersionPolicy.TopTieGroup(ordered, x => new(x.Header.IsAudited, x.Header.IsRepresented, x.Header.PublishedAt));
        var pick = FinancialStatementVersionPolicy.Pick(tied,
            x => string.Join(';', x.Items.Where(item => item.SourceItemId is 15 or 143 or 313).OrderBy(item => item.SourceItemId)
                .ThenBy(item => item.Value).Select(item => $"{item.SourceItemId}={item.Value}")),
            x => x.Header.ExternalStatementId, x => x.Header.StatementId);
        if (pick.IsConflict || pick.Winner is null) return null;
        CashFlowObservedValue Get(int id) => CashFlowObservedValue.From(pick.Winner.Items.Where(x => x.SourceItemId == id).Select(x => x.Value).ToArray());
        var revenue = Get(15); var primary = Get(143); var fallback = Get(313);
        var profit = primary.State == CashFlowValueState.NotReported ? fallback : primary;
        return new(revenue.Value, profit.Value, primary.State == CashFlowValueState.NotReported ? 313 : 143);
    }

    private static string SectionKey(CashFlowSection section) => section.ToString().ToLowerInvariant();
    private static string SectionLabel(CashFlowSection section) => section switch
    {
        CashFlowSection.Operating => "فعالیت‌های عملیاتی", CashFlowSection.Investing => "فعالیت‌های سرمایه‌گذاری",
        CashFlowSection.Financing => "فعالیت‌های تأمین مالی", _ => "تطبیق موجودی نقد"
    };
    private static string MetricLabel(string id) => id switch
    {
        "operating_cash_flow" => "جریان خالص نقد عملیاتی", "investing_cash_flow" => "جریان نقد سرمایه‌گذاری",
        "financing_cash_flow" => "جریان نقد تأمین مالی", "net_change" => "خالص افزایش (کاهش) در وجه نقد",
        "opening_cash" => "مانده وجه نقد در آغاز دوره", "closing_cash" => "مانده وجه نقد در پایان دوره", "fx_effect" => "اثر تغییرات نرخ ارز",
        "facilities_received" => "تسهیلات دریافتی", "facilities_repaid" => "بازپرداخت اصل تسهیلات", "dividends_paid" => "سود سهام پرداختی",
        "cash_before_fx" => "مانده نقد قبل از اثر نرخ ارز", "total_change_in_cash" => "تغییر کل موجودی نقد",
        "interest_paid" => "سود پرداختی بابت تأمین مالی", "capital_transactions" => "وجوه حاصل از افزایش سرمایه",
        "treasury_net" => "خالص معاملات سهام خزانه", "capex" => "مخارج سرمایه‌ای نقدی", "free_cash_flow" => "جریان نقد آزاد",
        "capex_coverage" => "پوشش مخارج سرمایه‌ای", "net_debt_cash_flow" => "خالص جریان نقد استقراض",
        "cash_conversion" => "نسبت تبدیل سود به نقد", "ocf_to_revenue" => "نسبت جریان نقد عملیاتی به فروش",
        "fx_share_of_cash_change" => "سهم اثر نرخ ارز از تغییر موجودی نقد", _ => id
    };
    private static string WarningMessage(string code) => code switch
    {
        "duplicate_line_item" => "برای یک قلم، مبالغ تکراری و متفاوت گزارش شده است.",
        "consolidated_item_on_standalone" => "در صورت جداگانه قلم مربوط به صورت تلفیقی وجود دارد.",
        "fx_possibly_embedded_in_net_change" => "اثر ارز ممکن است در خالص تغییر وجه نقد لحاظ شده باشد؛ مبلغ گزارش‌شده تغییر داده نشده است.",
        "fx_missing_reconciliation_gap" => "اثر ارز گزارش نشده و مانده‌ها بدون آن تطبیق ندارند.",
        "ocf_codal_subtotal_mismatch" => "جمع جریان عملیاتی و سرمایه‌گذاری با جمع گزارش‌شده قبل از تأمین مالی برابر نیست.",
        "opening_balance_discontinuity" => "مانده ابتدای سال با مانده پایان سال قبل برابر نیست.",
        "quarter_opening_discontinuity" => "مانده ابتدای سال در دو صورت تجمعی برابر نیست.",
        _ => "برخی اقلام گزارش نیاز به بررسی دارند."
    };
}
