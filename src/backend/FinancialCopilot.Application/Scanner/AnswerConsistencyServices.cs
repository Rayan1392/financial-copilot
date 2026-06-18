using System.Globalization;
using System.Text.RegularExpressions;
using FinancialCopilot.Domain.Financial.Metrics;

namespace FinancialCopilot.Application.Scanner;

// Resolves bilingual metric display names from the governed semantic catalog so deterministic
// prose speaks the canonical metric name. The LLM never supplies these names or values.
public sealed class MetricDisplayNameResolver(
    IFinancialMetricRegistry registry,
    TimeProvider timeProvider)
{
    // Localized display name for prose. For Persian answers, prefers a Persian-script alias from the
    // governed catalog (e.g. "نسبت پی به ای"); otherwise the canonical English display name
    // (e.g. "P/E (TTM)"). Falls back to the raw metric code when no definition is registered.
    public string ResolveDisplayName(string metricCode, bool persian)
    {
        var asOf = DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime);
        FinancialMetricDefinition definition;
        try
        {
            definition = registry.ResolveDefinition(new MetricCode(metricCode), asOf);
        }
        catch
        {
            return metricCode;
        }

        if (persian)
        {
            var persianAlias = definition.Aliases
                .Where(a => a.Language.StartsWith("fa", StringComparison.OrdinalIgnoreCase))
                .Select(a => a.Expression)
                .FirstOrDefault(expression => ConsistencyText.ContainsPersian(expression));
            if (persianAlias is not null) return persianAlias;
        }

        return definition.DisplayName;
    }
}

public sealed class SymbolLookupProseBuilder(MetricDisplayNameResolver displayNames) : ISymbolLookupProseBuilder
{
    private const string MonthlySalesUnitNote = "Unit: million Rials";
    private const string MonthlySalesMetricCode = "MONTHLY_SALES";

    public string Build(SymbolLookupTableResult table)
    {
        var persian = ContainsPersian(table);

        if (HasAvailableMonthlySalesMonetaryCell(table))
            return MonthlySalesSentence(table, persian);

        var metricColumns = table.Columns
            .Where(c => c.ColumnType is ScannerColumnType.Metric
                or ScannerColumnType.MarketCap)
            .ToList();

        if (table.Rows.Count == 0)
        {
            var requested = FirstRequestedSymbol(table);
            if (metricColumns.Count == 1)
            {
                var onlyMetricCode = metricColumns[0].MetricCode ?? metricColumns[0].Identifier;
                var onlyMetricDisplay = displayNames.ResolveDisplayName(onlyMetricCode, persian);
                return requested is null
                    ? UnavailableSentenceNoSymbol(persian, onlyMetricDisplay)
                    : UnavailableSentence(persian, requested, onlyMetricDisplay);
            }

            return requested is null
                ? UnavailableLookupSentenceNoSymbol(persian)
                : UnavailableLookupSentence(persian, requested);
        }

        // Multiple symbols, or multiple metrics: prose must not state a single value. Defer to the table.
        if (table.Rows.Count > 1 || metricColumns.Count != 1)
            return WithUnitNote(table, MultiResultSentence(persian));

        var column = metricColumns[0];
        var metricCode = column.MetricCode ?? column.Identifier;
        var metricDisplay = displayNames.ResolveDisplayName(metricCode, persian);

        var row = table.Rows.First();
        var symbol = row.SymbolCode;

        if (!row.Cells.TryGetValue(column.Identifier, out var cell)
            || cell.FreshnessStatus == CellFreshnessStatus.Missing
            || string.IsNullOrWhiteSpace(cell.FormattedValue))
        {
            return UnavailableSentence(persian, symbol, metricDisplay);
        }

        return WithUnitNote(table, ValueSentence(persian, symbol, metricDisplay, cell.FormattedValue!));
    }

    private static string ValueSentence(bool persian, string symbol, string metricDisplay, string formattedValue) =>
        persian
            ? $"{MetricDisplayForPersianSentence(metricDisplay)} نماد {symbol} برابر است با {formattedValue}."
            : $"The {metricDisplay} of {symbol} is {formattedValue}.";

    private static string MetricDisplayForPersianSentence(string metricDisplay) =>
        metricDisplay.StartsWith("نسبت", StringComparison.OrdinalIgnoreCase)
            ? metricDisplay
            : $"نسبت {metricDisplay}";

    private static string UnavailableSentence(bool persian, string symbol, string metricDisplay) =>
        persian
            ? $"برای نماد {symbol} مقدار قابل اتکایی برای {metricDisplay} در داده‌های فعلی موجود نیست."
            : $"No reliable {metricDisplay} value is available for {symbol} in the current data.";

    private static string UnavailableSentenceNoSymbol(bool persian, string metricDisplay) =>
        persian
            ? $"مقدار قابل اتکایی برای {metricDisplay} نماد درخواستی در داده‌های فعلی موجود نیست."
            : $"No reliable {metricDisplay} value is available for the requested symbol in the current data.";

    private static string UnavailableLookupSentence(bool persian, string symbolOrCompany) =>
        persian
            ? $"برای {symbolOrCompany} داده قابل اتکایی در اطلاعات فعلی موجود نیست."
            : $"No reliable data is available for {symbolOrCompany} in the current dataset.";

    private static string UnavailableLookupSentenceNoSymbol(bool persian) =>
        persian
            ? "داده قابل اتکایی برای نماد یا شرکت درخواستی در اطلاعات فعلی موجود نیست."
            : "No reliable data is available for the requested symbol or company in the current dataset.";

    private static string MultiResultSentence(bool persian) =>
        persian
            ? "بر اساس داده‌های موجود، مقدار درخواستی برای نمادهای موردنظر در جدول زیر آمده است."
            : "Based on the available data, the requested values for the symbols are shown in the table below.";

    private static string MonthlySalesSentence(SymbolLookupTableResult table, bool persian)
    {
        if (table.Rows.Count == 1 &&
            TryGetAvailableCell(table.Rows.First(), MonthlySalesMetricCode, out var cell) &&
            !string.IsNullOrWhiteSpace(cell.FormattedValue))
        {
            var symbol = table.Rows.First().SymbolCode;
            return persian
                ? $"آخرین فروش ماهانه {symbol} برابر با {cell.FormattedValue} میلیون ریال است."
                : $"The latest monthly sales for {symbol} is {cell.FormattedValue} million Rials.";
        }

        if (table.Rows.Count == 1 &&
            TryGetFirstAvailableMonthlySalesMonetaryCell(table, table.Rows.First(), out var column, out var companionCell) &&
            !string.IsNullOrWhiteSpace(companionCell.FormattedValue))
        {
            var symbol = table.Rows.First().SymbolCode;
            var displayName = column.DisplayName;
            return persian
                ? $"{displayName} نماد {symbol} برابر با {companionCell.FormattedValue} میلیون ریال است."
                : $"The {displayName} for {symbol} is {companionCell.FormattedValue} million Rials.";
        }

        return MultiResultSentence(persian);
    }

    private static bool TryGetFirstAvailableMonthlySalesMonetaryCell(
        SymbolLookupTableResult table,
        ScannerTableRow row,
        out ScannerTableColumn column,
        out ScannerTableCell cell)
    {
        foreach (var candidateColumn in table.Columns.Where(IsMonthlySalesMonetaryColumn))
        {
            if (TryGetAvailableCell(row, candidateColumn.Identifier, out var candidateCell))
            {
                column = candidateColumn;
                cell = candidateCell;
                return true;
            }
        }

        column = default!;
        cell = default!;
        return false;
    }

    private static bool TryGetAvailableCell(
        ScannerTableRow row,
        string cellId,
        out ScannerTableCell cell)
    {
        if (row.Cells.TryGetValue(cellId, out cell!) &&
            cell.Value is not null &&
            cell.FreshnessStatus != CellFreshnessStatus.Missing)
        {
            return true;
        }

        return false;
    }

    private static string WithUnitNote(SymbolLookupTableResult table, string sentence) =>
        HasMonthlySalesMonetaryColumn(table)
            ? $"{MonthlySalesUnitNote}{Environment.NewLine}{sentence}"
            : sentence;

    private static bool HasMonthlySalesMonetaryColumn(SymbolLookupTableResult table) =>
        table.Columns.Any(IsMonthlySalesMonetaryColumn);

    private static bool IsMonthlySalesMonetaryColumn(ScannerTableColumn column)
    {
        var metricCode = column.MetricCode ?? column.Identifier;
        return string.Equals(metricCode, "MONTHLY_SALES", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(metricCode, "AVG_12M_MONTHLY_SALES", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(metricCode, "MONTHLY_SALES_PRIOR_FISCAL_YEAR_SAME_MONTH", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(metricCode, "MONTHLY_SALES_YTD", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(metricCode, "MONTHLY_SALES_YTD_PREVIOUS_MONTH", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool HasAvailableMonthlySalesMonetaryCell(SymbolLookupTableResult table)
    {
        var monthlySalesColumnIds = table.Columns
            .Where(c =>
                string.Equals(c.MetricCode ?? c.Identifier, "MONTHLY_SALES", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.MetricCode ?? c.Identifier, "AVG_12M_MONTHLY_SALES", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.MetricCode ?? c.Identifier, "MONTHLY_SALES_PRIOR_FISCAL_YEAR_SAME_MONTH", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.MetricCode ?? c.Identifier, "MONTHLY_SALES_YTD", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(c.MetricCode ?? c.Identifier, "MONTHLY_SALES_YTD_PREVIOUS_MONTH", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Identifier)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return monthlySalesColumnIds.Count > 0 &&
            table.Rows.Any(row =>
                monthlySalesColumnIds.Any(id =>
                    row.Cells.TryGetValue(id, out var cell) &&
                    cell.Value is not null &&
                    cell.FreshnessStatus != CellFreshnessStatus.Missing));
    }

    private static string? FirstRequestedSymbol(SymbolLookupTableResult table) =>
        table.UnresolvedSymbols.FirstOrDefault();

    private static bool ContainsPersian(SymbolLookupTableResult table) =>
        table.Rows.Any(r => ConsistencyText.ContainsPersian(r.SymbolCode))
        || table.UnresolvedSymbols.Any(ConsistencyText.ContainsPersian);
}

public sealed class AnswerConsistencyValidator(
    ISymbolLookupProseBuilder proseBuilder,
    MetricDisplayNameResolver displayNames,
    IAnswerConsistencyWarningSink warningSink) : IAnswerConsistencyValidator
{
    public AnswerConsistencyResult ValidateSymbolLookup(
        SymbolLookupTableResult table,
        string? candidateProse,
        AnswerConsistencyContext context)
    {
        var authoritative = ExtractSymbolLookupValues(table);
        var deterministic = proseBuilder.Build(table);
        if (table.Rows.Count == 0)
            return new AnswerConsistencyResult(
                AnswerConsistencyAction.ReplacedWithDeterministic,
                deterministic,
                []);

        if (SymbolLookupProseBuilder.HasAvailableMonthlySalesMonetaryCell(table))
            return new AnswerConsistencyResult(
                AnswerConsistencyAction.ReplacedWithDeterministic,
                deterministic,
                []);

        // Strict mode: a symbol-lookup answer states a single metric value. Any prose number that is
        // not an authoritative cell value is a conflicting/invented metric claim.
        var conflicts = DetectStrictConflicts(candidateProse, authoritative);
        return Resolve(candidateProse, deterministic, conflicts, context);
    }

    public AnswerConsistencyResult ValidateScanner(
        ScannerTableResult table,
        ScannerQueryPlan plan,
        string? candidateProse,
        AnswerConsistencyContext context)
    {
        var authoritative = ExtractScannerValues(table);
        // Scanner prose is descriptive (counts/criteria), not a single value. A safe deterministic
        // replacement is the count summary; only used when a real conflict is detected.
        var deterministic = ScannerSafeSentence(table);
        // Conservative mode: scanner prose legitimately contains counts and plan thresholds. Those are
        // allowed; only numbers that resemble a metric cell value yet match none are flagged.
        var allowed = BuildAllowedScannerNumbers(table, plan);
        var conflicts = DetectConservativeConflicts(candidateProse, authoritative, allowed);
        return Resolve(candidateProse, deterministic, conflicts, context);
    }

    private AnswerConsistencyResult Resolve(
        string? candidateProse,
        string deterministic,
        IReadOnlyCollection<AnswerConsistencyConflict> conflicts,
        AnswerConsistencyContext context)
    {
        if (string.IsNullOrWhiteSpace(candidateProse))
            return new AnswerConsistencyResult(AnswerConsistencyAction.Unchanged, deterministic, []);

        if (conflicts.Count == 0)
            return new AnswerConsistencyResult(AnswerConsistencyAction.Unchanged, candidateProse, []);

        foreach (var conflict in conflicts)
            warningSink.RecordCorrectedInconsistency(context, conflict);

        return new AnswerConsistencyResult(
            AnswerConsistencyAction.ReplacedWithDeterministic, deterministic, conflicts);
    }

    // Strict (symbol lookup): the answer states a single metric value. A conflict exists when:
    //  - the prose contains a number that equals no authoritative cell value (hallucinated/stale,
    //    e.g. prose "7.88" while the table cell is "5.06", or any number invented when unavailable), OR
    //  - the result is ambiguous (more than one authoritative value) yet the prose still states a
    //    specific number — a single value must not be asserted for multiple symbols/metrics.
    private static IReadOnlyCollection<AnswerConsistencyConflict> DetectStrictConflicts(
        string? prose,
        IReadOnlyCollection<AuthoritativeMetricValue> authoritative)
    {
        if (string.IsNullOrWhiteSpace(prose) || authoritative.Count == 0)
            return [];

        var proseNumbers = ConsistencyText.ExtractNumbers(prose);
        if (proseNumbers.Count == 0)
            return [];

        var distinctValues = authoritative
            .Where(a => a.Value is not null)
            .Select(a => a.Value!.Value)
            .Distinct()
            .Count();
        var ambiguous = authoritative.Count > 1 || distinctValues > 1;

        var offending = proseNumbers
            .Where(n => ambiguous
                || !authoritative.Any(a => a.Value is { } av && ConsistencyText.NumbersEqual(n, av)))
            .ToList();
        if (offending.Count == 0)
            return [];

        // Attribute the conflict to the first AVAILABLE authoritative pair so the logged table value
        // is the real value the prose contradicts (not a null cell that merely sorted first).
        var primary = PrimaryFor(authoritative);
        return offending
            .Select(n => new AnswerConsistencyConflict(
                primary.SymbolCode, primary.MetricCode,
                ConsistencyText.Format(n), primary.FormattedValue))
            .ToList();
    }

    private static AuthoritativeMetricValue PrimaryFor(
        IReadOnlyCollection<AuthoritativeMetricValue> authoritative) =>
        authoritative.FirstOrDefault(a => a.IsAvailable) ?? authoritative.First();

    // Conservative (scanner): allow counts and plan thresholds; flag a prose number only when it is
    // not allowed and matches no authoritative cell value, i.e. it asserts a metric figure the table
    // does not support.
    private static IReadOnlyCollection<AnswerConsistencyConflict> DetectConservativeConflicts(
        string? prose,
        IReadOnlyCollection<AuthoritativeMetricValue> authoritative,
        IReadOnlyCollection<decimal> allowed)
    {
        if (string.IsNullOrWhiteSpace(prose) || authoritative.Count == 0)
            return [];

        var proseNumbers = ConsistencyText.ExtractNumbers(prose);
        if (proseNumbers.Count == 0)
            return [];

        var offending = proseNumbers
            .Where(n => !allowed.Any(a => ConsistencyText.NumbersEqual(n, a)))
            .Where(n => !authoritative.Any(a => a.Value is { } av && ConsistencyText.NumbersEqual(n, av)))
            .ToList();
        if (offending.Count == 0)
            return [];

        var primary = PrimaryFor(authoritative);
        return offending
            .Select(n => new AnswerConsistencyConflict(
                primary.SymbolCode, primary.MetricCode,
                ConsistencyText.Format(n), TableValue: null))
            .ToList();
    }

    // Numbers that scanner prose may legitimately contain without being a metric-value claim:
    // the result/evaluated counts and the plan's condition thresholds. Pagination facts (page,
    // page size, total pages) are intentionally NOT whitelisted — their common values (1, 20) would
    // become blind spots that let an invented metric figure of the same value escape detection.
    private static IReadOnlyCollection<decimal> BuildAllowedScannerNumbers(
        ScannerTableResult table,
        ScannerQueryPlan plan)
    {
        var allowed = new List<decimal>
        {
            table.Rows.Count,
            table.ExecutionFacts.MatchingSymbolCount,
            table.ExecutionFacts.TotalSymbolsEvaluated
        };
        allowed.AddRange(plan.Conditions.Select(c => c.Threshold));
        return allowed;
    }

    private IReadOnlyCollection<AuthoritativeMetricValue> ExtractSymbolLookupValues(
        SymbolLookupTableResult table)
    {
        var persian = table.Rows.Any(r => ConsistencyText.ContainsPersian(r.SymbolCode));
        return ExtractValues(table.Columns, table.Rows, persian);
    }

    private IReadOnlyCollection<AuthoritativeMetricValue> ExtractScannerValues(ScannerTableResult table)
    {
        var persian = table.Rows.Any(r => ConsistencyText.ContainsPersian(r.SymbolCode));
        return ExtractValues(table.Columns, table.Rows, persian);
    }

    private IReadOnlyCollection<AuthoritativeMetricValue> ExtractValues(
        IReadOnlyCollection<ScannerTableColumn> columns,
        IReadOnlyCollection<ScannerTableRow> rows,
        bool persian)
    {
        var metricColumns = columns
            .Where(c => c.ColumnType is ScannerColumnType.Metric
                or ScannerColumnType.LatestPrice or ScannerColumnType.MarketCap)
            .ToList();

        var values = new List<AuthoritativeMetricValue>();
        foreach (var row in rows)
        {
            foreach (var column in metricColumns)
            {
                var metricCode = column.MetricCode ?? column.Identifier;
                var display = displayNames.ResolveDisplayName(metricCode, persian);
                row.Cells.TryGetValue(column.Identifier, out var cell);
                var available = cell is not null
                    && cell.FreshnessStatus != CellFreshnessStatus.Missing
                    && cell.Value is not null;

                values.Add(new AuthoritativeMetricValue(
                    row.SymbolCode,
                    metricCode,
                    display,
                    available ? cell!.Value : null,
                    available ? cell!.FormattedValue : null,
                    available));
            }
        }

        return values;
    }

    private static string ScannerSafeSentence(ScannerTableResult table) =>
        table.Rows.Any(r => ConsistencyText.ContainsPersian(r.SymbolCode))
            ? $"اسکنر {table.Rows.Count} نماد منطبق پیدا کرد. مقادیر دقیق در جدول زیر آمده است."
            : $"The scanner found {table.Rows.Count} matching symbol(s). The exact values are shown in the table below.";
}

internal static class ConsistencyText
{
    // Matches decimal numbers including thousands separators, e.g. "7.88", "5.06", "1,234.5", "1234".
    private static readonly Regex NumberPattern = new(
        @"(?<!\w)\d{1,3}(?:,\d{3})*(?:\.\d+)?(?!\w)|(?<!\w)\d+(?:\.\d+)?(?!\w)",
        RegexOptions.CultureInvariant | RegexOptions.Compiled);

    // Two values are equal when their displayed magnitude matches at the table's rendering precision.
    // The table renders metric ratios at N2 (two decimals), so compare rounded to 2 decimals while
    // also accepting an exact match for non-rounded callers.
    public static bool NumbersEqual(decimal a, decimal b)
    {
        if (a == b) return true;
        return Math.Round(a, 2, MidpointRounding.AwayFromZero)
            == Math.Round(b, 2, MidpointRounding.AwayFromZero);
    }

    public static IReadOnlyCollection<decimal> ExtractNumbers(string text)
    {
        // The LLM may render numbers with Persian/Arabic-Indic digits and the Persian decimal/thousands
        // separators. Normalize to ASCII first so the consistency check is not bypassed for Persian
        // prose (e.g. "۷٫۸۸" must be detected as 7.88).
        var normalized = NormalizeDigits(text);

        var numbers = new List<decimal>();
        foreach (Match match in NumberPattern.Matches(normalized))
        {
            var raw = match.Value.Replace(",", string.Empty);
            if (decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
                numbers.Add(value);
        }

        return numbers;
    }

    private static string NormalizeDigits(string text)
    {
        var buffer = new char[text.Length];
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            buffer[i] = c switch
            {
                // Persian digits ۰-۹ (U+06F0–U+06F9) and Arabic-Indic digits ٠-٩ (U+0660–U+0669).
                >= '۰' and <= '۹' => (char)('0' + (c - '۰')),
                >= '٠' and <= '٩' => (char)('0' + (c - '٠')),
                // Persian/Arabic decimal separator (٫) and thousands separators (٬, ، ).
                '٫' => '.',
                '٬' or '،' => ',',
                _ => c
            };
        }

        return new string(buffer);
    }

    public static string Format(decimal value) => value.ToString("N2", CultureInfo.InvariantCulture);

    public static bool ContainsPersian(string text) =>
        text.Any(c => c is >= '؀' and <= 'ۿ' or >= 'ݐ' and <= 'ݿ');
}
