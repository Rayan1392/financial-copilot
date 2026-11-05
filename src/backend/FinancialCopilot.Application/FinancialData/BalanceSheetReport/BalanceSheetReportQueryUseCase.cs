using System.Diagnostics;
using FinancialCopilot.Application.FinancialData.Ingestion;

namespace FinancialCopilot.Application.FinancialData.BalanceSheetReport;

/// <summary>
/// Feature 154 use case: resolve the company with provider awareness, classify it by GroupId only, read the
/// bounded persisted balance sheets, select versions, build the anchor snapshot, resolve the standard (and
/// explicit) baselines, compute changes and the funding chart. Callable without an LLM; no provider calls.
/// </summary>
public sealed class BalanceSheetReportQueryUseCase(
    IBalanceSheetStatementReader reader,
    ICompanyResolutionOutcomeService companyResolution,
    IQuarterlyFinancialReportsRepository classificationReader,
    BankingFinancialReportsConfiguration bankingConfiguration,
    TimeProvider timeProvider) : IBalanceSheetReportQueryUseCase
{
    /// <summary>The balance-sheet header window (the Feature 144 bound, about four fiscal years).</summary>
    public const int HeaderWindow = 48;

    public async Task<BalanceSheetReportResult> ExecuteAsync(
        BalanceSheetReportRequest request,
        CancellationToken cancellationToken = default)
    {
        var started = Stopwatch.GetTimestamp();
        var result = await ExecuteCoreAsync(request, cancellationToken);
        var activity = Activity.Current;
        activity?.SetTag("feature154.template", result.TemplateId);
        activity?.SetTag("feature154.outcome", result.Outcome);
        activity?.SetTag("feature154.view", result.RequestedView);
        activity?.SetTag("feature154.baseline_kind", result.Comparison?.SelectedBaselineKind);
        var selected = result.Comparison?.Candidates.FirstOrDefault(item => item.Kind == result.Comparison.SelectedBaselineKind);
        activity?.SetTag("feature154.baseline_state", selected?.State);
        activity?.SetTag("feature154.baseline_reason", selected?.Reason);
        activity?.SetTag("feature154.anchor_period_end",
            result.AnchorStatementId is { } anchorId && result.Snapshots.TryGetValue(anchorId, out var anchor)
                ? anchor.Statement.PeriodEnd.ToString("yyyy-MM-dd")
                : null);
        var cells = result.Snapshots.Values.SelectMany(BalanceSheetComparisonBuilder.AllRows).Select(row => row.Cell).ToArray();
        activity?.SetTag("feature154.alias_discrepancy_count", cells.Count(cell => cell.AliasDiscrepancy is not null));
        activity?.SetTag("feature154.unverified_candidate_count", cells.Sum(cell => cell.Candidates.Count(candidate => !candidate.Verified)));
        activity?.SetTag("feature154.chart_points", result.Chart.Points.Count);
        activity?.SetTag("feature154.read_latency_ms", Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }

    private async Task<BalanceSheetReportResult> ExecuteCoreAsync(BalanceSheetReportRequest request, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var dates = (request.Dates ?? []).Distinct().ToArray();
        var view = request.View;
        var company = new BalanceSheetCompany(request.CompanyQuery, null, request.ProviderName, null);

        if (request.UnsupportedVariantFilter is { } filter && dates.Length > 0)
        {
            var named = string.Join(" و ", dates.Order().Select(BalanceSheetFormatting.JalaliDate));
            return Unavailable(company, view, now, BalanceSheetOutcomes.UnsupportedDateWithVariant,
                "date_with_feature083_filter",
                $"گزارش ترازنامه در تاریخ {named} همراه با فیلتر «{filter}» پشتیبانی نمی‌شود: {UnsupportedFilterRationale(filter)} " +
                $"می‌توانید «ترازنامه {request.CompanyQuery} در {BalanceSheetFormatting.JalaliDate(dates.Max())}» (ترازنامه جداگانه در همان تاریخ) " +
                $"یا «ترازنامه {filter} {request.CompanyQuery}» (آخرین صورت با این فیلتر، بدون تاریخ) را درخواست کنید.",
                requestedDate: dates.Max(), unsupportedFilter: filter);
        }

        var resolution = await companyResolution.ResolveAsync(request.CompanyQuery, request.ProviderName, ct);
        if (resolution.Status != CompanyResolutionStatus.Resolved || resolution.Company is null)
        {
            var (outcome, message) = resolution.Status switch
            {
                CompanyResolutionStatus.Ambiguous => (BalanceSheetOutcomes.CompanyAmbiguous,
                    $"عبارت «{request.CompanyQuery}» به بیش از یک شرکت اشاره دارد؛ لطفاً نماد دقیق را مشخص کنید."),
                CompanyResolutionStatus.UnsupportedProviderCompany => (BalanceSheetOutcomes.ProviderUnavailable,
                    $"داده ترازنامه «{request.CompanyQuery}» از منبع نوآوران در دسترس نیست."),
                _ => (BalanceSheetOutcomes.CompanyUnresolved, $"شرکت یا نماد «{request.CompanyQuery}» شناسایی نشد.")
            };
            return Unavailable(company, view, now, outcome, resolution.Reason ?? resolution.Status.ToString(), message);
        }

        var resolved = resolution.Company;
        var symbol = resolved.CompanySymbol ?? resolved.TseSymbol ?? resolved.Ticker ?? request.CompanyQuery;
        company = new BalanceSheetCompany(symbol, resolved.Name, request.ProviderName, resolved.ExternalCompanyId);

        var groups = await classificationReader.GetEligibleCompanyGroupsAsync(resolved.Id, request.ProviderName,
            resolved.ExternalCompanyId, ct);
        if (groups.Count != 1 || groups[0] is not { } groupId)
            return Unavailable(company, view, now, BalanceSheetOutcomes.ClassificationUnavailable,
                groups.Count == 0 ? "no_eligible_group" : "contradictory_group_evidence",
                $"طبقه‌بندی صنعت {symbol} برای انتخاب قالب ترازنامه در دسترس نیست.");
        var templateId = groupId == bankingConfiguration.IndustryGroupId ? BalanceSheetTemplates.Banking : BalanceSheetTemplates.General;

        var headerSet = await reader.ReadHeadersAsync(request.ProviderName, resolved.ExternalCompanyId, ct);
        var headers = headerSet.BalanceSheets.ToList();
        var windowTruncated = headers.Count >= HeaderWindow;
        var oldestInWindow = headers.Count == 0 ? (DateOnly?)null : headers.Min(item => item.PeriodEnd);
        // A truncated window can split the versions of its oldest date; that date is treated as outside the window
        // and, when needed, re-read with all its versions by the exact-date lookup (AC-X04, PD-09).
        if (windowTruncated && oldestInWindow is { } boundary)
            headers.RemoveAll(item => item.PeriodEnd == boundary);
        bool OutsideWindow(DateOnly date) => windowTruncated && oldestInWindow is { } oldest && date <= oldest &&
                                             headers.All(item => item.PeriodEnd != date);

        var requestedOutside = dates.Where(OutsideWindow).ToArray();
        if (requestedOutside.Length > 0)
            headers.AddRange(await reader.ReadHeadersAtDatesAsync(request.ProviderName, resolved.ExternalCompanyId, requestedOutside, ct));

        var eligibility = BalanceSheetStatementSelector.Filter(headers);
        if (eligibility.Eligible.Count == 0)
        {
            if (eligibility.Ineligible.Count > 0 &&
                eligibility.Ineligible.Where(item => item.Reason != BalanceSheetStatementSelector.ConsolidatedScope)
                    .All(item => item.Reason == BalanceSheetStatementSelector.IneligibleStorageScale) &&
                eligibility.Ineligible.Any(item => item.Reason == BalanceSheetStatementSelector.IneligibleStorageScale))
                return Unavailable(company, view, now, BalanceSheetOutcomes.IneligibleStorageScale, "assumed_scale_not_million_rials",
                    $"واحد ذخیره‌سازی ترازنامه‌های جداگانه {symbol} تأیید نشده است و مبلغی نمایش داده نمی‌شود.",
                    templateId: templateId, groupId: groupId);
            var consolidated = await reader.ConsolidatedExistsAsync(request.ProviderName, resolved.ExternalCompanyId, ct);
            return Unavailable(company, view, now, BalanceSheetOutcomes.NoStandaloneBalanceSheet, "no_eligible_standalone_statement",
                $"ترازنامه جداگانه (غیرتلفیقی) برای {DisplayName(company)} یافت نشد." +
                (consolidated ? $" ترازنامه تلفیقی این شرکت موجود است؛ می‌توانید «ترازنامه تلفیقی {symbol}» را درخواست کنید." : string.Empty),
                templateId: templateId, groupId: groupId, consolidatedAvailable: consolidated);
        }

        var eligible = eligibility.Eligible;
        var datesDescending = BalanceSheetStatementSelector.DistinctDatesDescending(eligible);

        DateOnly anchorDate;
        DateOnly? explicitBaselineDate = null;
        if (view == BalanceSheetViews.SingleDate && dates.Length >= 1)
        {
            anchorDate = dates[0];
        }
        else if (view == BalanceSheetViews.Comparison && dates.Length >= 2)
        {
            anchorDate = dates.Max();
            explicitBaselineDate = dates.Where(date => date != anchorDate).Max();
        }
        else if (view == BalanceSheetViews.Comparison && dates.Length == 1 &&
                 request.BaselineKind is not null && request.BaselineKind != BalanceSheetBaselineKinds.Explicit)
        {
            // One date plus a baseline cue: anchor on the requested date (exact match) with the cued baseline (PD-20).
            anchorDate = dates[0];
        }
        else if (view == BalanceSheetViews.Comparison && dates.Length == 1)
        {
            anchorDate = datesDescending[0];
            explicitBaselineDate = dates[0];
        }
        else
        {
            anchorDate = datesDescending[0];
        }

        if (!datesDescending.Contains(anchorDate))
        {
            var (earlier, later) = BalanceSheetStatementSelector.Nearest(datesDescending, anchorDate);
            var options = string.Join("، ", new[]
            {
                earlier is { } before ? $"قبل از آن: {BalanceSheetFormatting.JalaliDate(before)}" : null,
                later is { } after ? $"بعد از آن: {BalanceSheetFormatting.JalaliDate(after)}" : null
            }.Where(item => item is not null));
            return Unavailable(company, view, now, BalanceSheetOutcomes.NoSnapshotAtDate, "no_exact_period_end",
                $"ترازنامه جداگانه {symbol} با تاریخ دقیق {BalanceSheetFormatting.JalaliDate(anchorDate)} موجود نیست." +
                (options.Length > 0 ? $" نزدیک‌ترین تاریخ‌های موجود — {options}." : string.Empty),
                requestedDate: anchorDate, nearest: new BalanceSheetNearestDates(earlier, earlier is { } e ? FiscalCalendarPolicy.ToJalali(e) : null,
                    later, later is { } l ? FiscalCalendarPolicy.ToJalali(l) : null),
                templateId: templateId, groupId: groupId);
        }

        var anchorRanked = BalanceSheetStatementSelector.RankedAt(eligible, anchorDate);
        var anchorReference = anchorRanked[0];
        var evidence = headers.Select(item => new BalanceSheetFiscalEvidence(item.PeriodEnd, item.JalaliFiscalYearEnd))
            .Concat(headerSet.FiscalEvidence).Distinct().ToArray();
        var targets = BalanceSheetBaselineResolver.ResolveStandardTargets(anchorReference, datesDescending, evidence).ToList();
        if (explicitBaselineDate is { } explicitDate)
            targets.Insert(0, BalanceSheetBaselineResolver.ResolveExplicitTarget(explicitDate));

        var baselineOutside = targets.Where(target => target.Date is { } date && OutsideWindow(date))
            .Select(target => target.Date!.Value).Distinct().ToArray();
        if (baselineOutside.Length > 0)
        {
            headers.AddRange(await reader.ReadHeadersAtDatesAsync(request.ProviderName, resolved.ExternalCompanyId, baselineOutside, ct));
            eligibility = BalanceSheetStatementSelector.Filter(headers);
            eligible = eligibility.Eligible;
            datesDescending = BalanceSheetStatementSelector.DistinctDatesDescending(eligible);
        }

        // Hard read budget (AC-X04): line items for at most 9 statements, counting equally ranked peer versions,
        // in priority order anchor → baselines → chart dates. A group that does not fit is never read partially:
        // the anchor fails closed, a baseline becomes unavailable, a chart date is left out.
        var groupsByKey = new Dictionary<(DateOnly Date, string? PeriodType), IReadOnlyList<BalanceSheetStatementHeader>>();
        IReadOnlyList<BalanceSheetStatementHeader> GroupAt(DateOnly date, string? periodType)
        {
            if (!groupsByKey.TryGetValue((date, periodType), out var group))
                groupsByKey[(date, periodType)] = group = BalanceSheetStatementSelector.TopTieGroup(
                    BalanceSheetStatementSelector.RankedAt(eligible, date, periodType));
            return group;
        }

        var readIds = new HashSet<Guid>();
        var included = new HashSet<(DateOnly Date, string? PeriodType)>();
        bool Include(DateOnly date, string? periodType)
        {
            if (included.Contains((date, periodType))) return true;
            var ids = GroupAt(date, periodType).Select(item => item.StatementId).Where(id => !readIds.Contains(id)).ToArray();
            if (readIds.Count + ids.Length > BalanceSheetReportContract.MaxLineItemStatements) return false;
            readIds.UnionWith(ids);
            included.Add((date, periodType));
            return true;
        }

        if (!Include(anchorDate, null))
            return Unavailable(company, view, now, BalanceSheetOutcomes.TooManyReportVersions, "anchor_versions_exceed_read_bound",
                $"برای ترازنامه {symbol} در تاریخ {BalanceSheetFormatting.JalaliDate(anchorDate)} نسخه‌های هم‌رتبه بیش از حد مجاز خواندن وجود دارد و گزارشی انتخاب نمی‌شود.",
                requestedDate: anchorDate, templateId: templateId, groupId: groupId);
        foreach (var target in targets.Where(target => target.Date is not null))
            Include(target.Date!.Value, target.PeriodType);

        var chartDates = new List<DateOnly>();
        foreach (var date in BalanceSheetStatementSelector.ChartDates(datesDescending, anchorDate).OrderDescending())
            if (Include(date, null)) chartDates.Add(date);

        chartDates.Sort();
        var statementIds = readIds.ToArray();
        var lineItems = await reader.ReadLineItemsAsync(request.ProviderName, statementIds,
            BalanceSheetRowRegistry.RegisteredSourceItemIds, ct);

        var anchorPick = BalanceSheetStatementSelector.Pick(GroupAt(anchorDate, null), lineItems);
        if (anchorPick.IsConflict || anchorPick.Winner is null)
            return Unavailable(company, view, now, BalanceSheetOutcomes.ConflictingReportRevision, "equally_ranked_versions_disagree",
                $"برای ترازنامه {symbol} در تاریخ {BalanceSheetFormatting.JalaliDate(anchorDate)} نسخه‌های هم‌رتبه با مقادیر متفاوت وجود دارد و گزارشی انتخاب نمی‌شود.",
                requestedDate: anchorDate, templateId: templateId, groupId: groupId);

        var snapshots = new Dictionary<string, BalanceSheetSnapshot>(StringComparer.Ordinal);
        BalanceSheetSnapshot SnapshotOf(BalanceSheetStatementHeader header)
        {
            var key = header.StatementId.ToString("D");
            if (!snapshots.TryGetValue(key, out var snapshot))
                snapshots[key] = snapshot = BalanceSheetReportBuilder.Build(templateId, header,
                    lineItems.GetValueOrDefault(header.StatementId) ?? []);
            return snapshot;
        }

        var anchorSnapshot = SnapshotOf(anchorPick.Winner);
        var candidates = new List<BalanceSheetBaselineCandidate>();
        foreach (var target in targets)
        {
            var label = BalanceSheetBaselineKinds.Label(target.Kind);
            if (target.Date is not { } baselineDate)
            {
                candidates.Add(Candidate(target.Kind, label, target.Reason, null, null, null, null));
                continue;
            }

            var group = GroupAt(baselineDate, target.PeriodType);
            if (group.Count > 0 && !included.Contains((baselineDate, target.PeriodType)))
            {
                candidates.Add(Candidate(target.Kind, label, BalanceSheetOutcomes.TooManyReportVersions, baselineDate, null, null, null));
                continue;
            }

            if (group.Count == 0)
            {
                candidates.Add(Candidate(target.Kind, label, BalanceSheetResolverReason(target), baselineDate, null, null, null));
                continue;
            }

            var pick = BalanceSheetStatementSelector.Pick(group, lineItems);
            if (pick.IsConflict || pick.Winner is null)
            {
                candidates.Add(Candidate(target.Kind, label, BalanceSheetOutcomes.ConflictingReportRevision, baselineDate, null, null, null));
                continue;
            }

            var baselineSnapshot = SnapshotOf(pick.Winner);
            candidates.Add(Candidate(target.Kind, label, null, baselineDate, pick.Winner.StatementId.ToString("D"),
                BalanceSheetComparisonBuilder.RowChanges(anchorSnapshot, baselineSnapshot),
                BalanceSheetComparisonBuilder.RatioChange(anchorSnapshot.Ratio, baselineSnapshot.Ratio)));
        }

        var available = candidates.Where(item => item.State == "available").Select(item => item.Kind).ToArray();
        candidates = candidates.Select(item => item.State == "available"
                ? item
                : item with
                {
                    Alternatives = available
                        .OrderByDescending(kind => kind == BalanceSheetBaselineKinds.PreviousReport &&
                                                   item.Kind == BalanceSheetBaselineKinds.PreviousFiscalQuarterEnd)
                        .ToArray()
                })
            .ToList();

        var points = new List<BalanceSheetChartPoint>();
        foreach (var date in chartDates)
        {
            var pick = BalanceSheetStatementSelector.Pick(GroupAt(date, null), lineItems);
            if (pick.IsConflict || pick.Winner is null)
            {
                points.Add(new BalanceSheetChartPoint(date, FiscalCalendarPolicy.ToJalali(date), null,
                    BalanceSheetCheckStates.Unavailable, null, false, false, BalanceSheetOutcomes.ConflictingReportRevision));
                continue;
            }

            var snapshot = SnapshotOf(pick.Winner);
            var liabilities = snapshot.Sections.First(section => section.SectionId == "liabilities").Total?.Cell;
            var equity = snapshot.Sections.First(section => section.SectionId == "equity").Total?.Cell;
            var nonNegative = liabilities?.Value is >= 0m && equity?.Value is >= 0m;
            points.Add(new BalanceSheetChartPoint(date, FiscalCalendarPolicy.ToJalali(date), pick.Winner.StatementId.ToString("D"),
                snapshot.Reconciliation.R2.State, snapshot.Reconciliation.R2.Difference,
                snapshot.Reconciliation.R2.State == BalanceSheetCheckStates.Reconciled && nonNegative,
                equity?.Value is < 0m, null));
        }

        var selectedKind = request.BaselineKind is { } kind && candidates.Any(item => item.Kind == kind)
            ? kind
            : explicitBaselineDate is not null ? BalanceSheetBaselineKinds.Explicit : BalanceSheetBaselineKinds.FiscalYearEnd;
        var resolvedView = view is BalanceSheetViews.SingleDate or BalanceSheetViews.Comparison ? view : BalanceSheetViews.Default;
        return new BalanceSheetReportResult(
            BalanceSheetReportContract.Version,
            BalanceSheetOutcomes.Available,
            null,
            null,
            view == BalanceSheetViews.SingleDate ? anchorDate : null,
            view == BalanceSheetViews.SingleDate ? FiscalCalendarPolicy.ToJalali(anchorDate) : null,
            null,
            null,
            false,
            templateId,
            BalanceSheetReportContract.Title,
            company,
            groupId,
            snapshots,
            anchorPick.Winner.StatementId.ToString("D"),
            resolvedView,
            new BalanceSheetComparison(selectedKind, candidates),
            new BalanceSheetChart(BalanceSheetReportContract.MaxChartDates, points),
            now);
    }

    private static string UnsupportedFilterRationale(string filter) => filter switch
    {
        "تلفیقی" => "صورت تلفیقی فقط به‌صورت آخرین صورت منتشرشده (جدول کامل) در دسترس است و این گزارش فقط صورت جداگانه (غیرتلفیقی) را نشان می‌دهد.",
        "حسابرسی‌شده" or "حسابرسی‌نشده" => "فیلتر وضعیت حسابرسی فقط برای آخرین صورت منتشرشده (جدول کامل) اعمال می‌شود؛ ترازنامه در تاریخ مشخص نسخه را بر پایه حسابرسی، تجدید ارائه و تاریخ انتشار خودش انتخاب و برچسب‌گذاری می‌کند.",
        "تجدید ارائه‌شده" => "فیلتر تجدید ارائه فقط برای آخرین صورت منتشرشده (جدول کامل) اعمال می‌شود؛ ترازنامه در تاریخ مشخص نسخه تجدید ارائه‌شده را در صورت وجود ترجیح می‌دهد و برچسب می‌زند.",
        _ when filter.EndsWith("ماهه", StringComparison.Ordinal) => "طول دوره گزارش فقط برای آخرین صورت منتشرشده (جدول کامل) فیلتر می‌شود؛ در ترازنامه، تاریخ مشخص خود دوره گزارش را تعیین می‌کند.",
        _ => "جدول کامل همه اقلام فقط آخرین صورت منتشرشده را نشان می‌دهد و فیلتر تاریخ ندارد؛ این گزارش اقلام منتخب ترازنامه جداگانه را نشان می‌دهد."
    };

    private static string BalanceSheetResolverReason(BalanceSheetBaselineTarget target) =>
        target.Kind == BalanceSheetBaselineKinds.SameStagePreviousYear
            ? BalanceSheetBaselineResolver.NoMatchingStage
            : BalanceSheetBaselineResolver.NoSnapshotAtDate;

    private static BalanceSheetBaselineCandidate Candidate(string kind, string label, string? reason, DateOnly? date,
        string? statementId, IReadOnlyDictionary<string, BalanceSheetChange>? changes, BalanceSheetRatioChange? ratioChange) =>
        new(kind, label, statementId is null ? "unavailable" : "available", statementId is null ? reason : null, date,
            date is { } value ? FiscalCalendarPolicy.ToJalali(value) : null, statementId,
            BalanceSheetBaselineSourcePolicy.StoredSnapshot, BalanceSheetBaselineSourcePolicy.AnchorLinkVerified,
            changes ?? new Dictionary<string, BalanceSheetChange>(), ratioChange, []);

    private static string DisplayName(BalanceSheetCompany company) =>
        string.IsNullOrWhiteSpace(company.Name) || company.Name == company.Symbol ? company.Symbol : $"{company.Name} ({company.Symbol})";

    private static BalanceSheetReportResult Unavailable(
        BalanceSheetCompany company,
        string view,
        DateTimeOffset now,
        string outcome,
        string? reason,
        string message,
        DateOnly? requestedDate = null,
        BalanceSheetNearestDates? nearest = null,
        string? unsupportedFilter = null,
        string? templateId = null,
        Guid? groupId = null,
        bool consolidatedAvailable = false) =>
        new(BalanceSheetReportContract.Version, outcome, reason, message, requestedDate,
            requestedDate is { } date ? FiscalCalendarPolicy.ToJalali(date) : null, nearest, unsupportedFilter,
            consolidatedAvailable, templateId, BalanceSheetReportContract.Title, company, groupId,
            new Dictionary<string, BalanceSheetSnapshot>(), null, view, null,
            new BalanceSheetChart(BalanceSheetReportContract.MaxChartDates, []), now);
}
