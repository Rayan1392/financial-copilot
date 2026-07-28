using System.Globalization;
using FinancialCopilot.Application.Scanner;
using FinancialCopilot.Domain.Financial.Insights;

namespace FinancialCopilot.Application.FinancialData.ProfessionalScanners;

public sealed class GovernedProfessionalFilterCatalog : IProfessionalFilterCatalog
{
    public const string EntitlementCode = "AiQuery.Scanner";
    private readonly IReadOnlyCollection<ProfessionalFilterDefinition> _definitions = BuildDefinitions();
    private static readonly IReadOnlyCollection<UnsupportedProfessionalFilter> Unsupported =
    [
        new("RSI / MACD / Ichimoku", "No governed canonical technical metric is persisted for these indicators."),
        new("broker-by-broker order-book depth", "The normalized market feed does not persist broker-level depth evidence."),
        new("opaque smart-money formulas", "Executable or undisclosed formulas are prohibited; only governed metrics and insight events are supported."),
        new("buy or sell recommendations", "Ready filters report deterministic evidence and are not recommendations.")
    ];

    public GovernedProfessionalFilterCatalog()
    {
        var duplicate = _definitions.GroupBy(item => (item.Code, item.Version))
            .FirstOrDefault(group => group.Count() != 1);
        if (duplicate is not null)
            throw new InvalidOperationException($"Professional filter {duplicate.Key.Code}/{duplicate.Key.Version} is duplicated.");
    }

    public ProfessionalCatalogPage List(ProfessionalCatalogQuery query)
    {
        var pageSize = Math.Clamp(query.PageSize, 1, 100);
        var page = Math.Max(1, query.Page);
        IEnumerable<ProfessionalFilterDefinition> filtered = _definitions.Where(item => item.State == ProfessionalFilterState.Active);
        if (query.Category.HasValue) filtered = filtered.Where(item => item.Category == query.Category.Value);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = Normalize(query.Search);
            filtered = filtered.Where(item => Normalize(item.Code).Contains(search, StringComparison.Ordinal) ||
                Normalize(item.TitleFa).Contains(search, StringComparison.Ordinal) ||
                item.PersianAliases.Any(alias => Normalize(alias).Contains(search, StringComparison.Ordinal)));
        }

        var values = filtered.OrderBy(item => item.Category).ThenBy(item => item.Code, StringComparer.Ordinal).ToArray();
        var pages = values.Length == 0 ? 1 : (int)Math.Ceiling(values.Length / (double)pageSize);
        page = Math.Min(page, pages);
        return new ProfessionalCatalogPage(values.Skip((page - 1) * pageSize).Take(pageSize).ToArray(),
            Unsupported, page, pageSize, values.Length, pages);
    }

    public ProfessionalFilterDefinition Get(string code, string? version = null)
    {
        if (string.IsNullOrWhiteSpace(code)) throw new ProfessionalScannerValidationException("Filter code is required.");
        var normalizedCode = code.Trim().ToUpperInvariant();
        var matches = _definitions.Where(item => item.Code == normalizedCode &&
            (version is null || item.Version.Equals(version.Trim(), StringComparison.OrdinalIgnoreCase))).ToArray();
        if (matches.Length == 0) throw new ProfessionalScannerValidationException($"Ready filter '{code}' was not found.");
        return matches.OrderByDescending(item => item.Version, StringComparer.Ordinal).First();
    }

    public ProfessionalAliasResolution ResolveAlias(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new(false, false, null, [], "Filter code or alias is required.");
        var input = Normalize(text);
        var exact = _definitions.Where(item => item.State == ProfessionalFilterState.Active &&
            (Normalize(item.Code) == input || Normalize(item.TitleFa) == input ||
             item.PersianAliases.Any(alias => Normalize(alias) == input))).ToArray();
        if (exact.Length == 1) return new(true, false, exact[0], exact, null);
        if (exact.Length > 1) return new(false, true, null, exact, "Alias matches more than one governed filter.");
        var partial = _definitions.Where(item => item.State == ProfessionalFilterState.Active &&
            (Normalize(item.TitleFa).Contains(input, StringComparison.Ordinal) ||
             item.PersianAliases.Any(alias => Normalize(alias).Contains(input, StringComparison.Ordinal)))).ToArray();
        return partial.Length switch
        {
            1 => new(true, false, partial[0], partial, null),
            > 1 => new(false, true, null, partial, "Alias is ambiguous; choose a catalog code."),
            _ => new(false, false, null, [], $"No governed ready filter matches '{text}'.")
        };
    }

    public IReadOnlyDictionary<string, string> ValidateParameters(
        ProfessionalFilterDefinition definition, IReadOnlyDictionary<string, string>? supplied)
    {
        supplied ??= new Dictionary<string, string>();
        var unknown = supplied.Keys.Where(key => !definition.Parameters.Any(parameter =>
            parameter.Name.Equals(key, StringComparison.OrdinalIgnoreCase) ||
            parameter.PersianAliases.Any(alias => Normalize(alias) == Normalize(key)))).ToArray();
        if (unknown.Length > 0)
            throw new ProfessionalScannerValidationException($"Unknown parameter(s): {string.Join(", ", unknown)}. Arbitrary expressions are not accepted.");

        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var parameter in definition.Parameters)
        {
            var pair = supplied.FirstOrDefault(item => parameter.Name.Equals(item.Key, StringComparison.OrdinalIgnoreCase) ||
                parameter.PersianAliases.Any(alias => Normalize(alias) == Normalize(item.Key)));
            var raw = pair.Key is null ? null : pair.Value;
            if (string.IsNullOrWhiteSpace(raw) && parameter.DefaultValue.HasValue)
                raw = parameter.DefaultValue.Value.ToString(CultureInfo.InvariantCulture);
            if (string.IsNullOrWhiteSpace(raw))
            {
                if (parameter.Required) throw new ProfessionalScannerValidationException($"Parameter '{parameter.Name}' is required.");
                continue;
            }
            if (raw.Contains(';') || raw.Contains("--", StringComparison.Ordinal) || raw.Contains("/*", StringComparison.Ordinal))
                throw new ProfessionalScannerValidationException("Executable expressions and SQL fragments are prohibited.");
            if (parameter.Type is ProfessionalParameterType.Decimal or ProfessionalParameterType.Integer)
            {
                if (!decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
                    throw new ProfessionalScannerValidationException($"Parameter '{parameter.Name}' must be numeric.");
                if (parameter.Type == ProfessionalParameterType.Integer && decimal.Truncate(number) != number)
                    throw new ProfessionalScannerValidationException($"Parameter '{parameter.Name}' must be an integer.");
                if (parameter.Minimum.HasValue && number < parameter.Minimum || parameter.Maximum.HasValue && number > parameter.Maximum)
                    throw new ProfessionalScannerValidationException(
                        $"Parameter '{parameter.Name}' must be between {parameter.Minimum} and {parameter.Maximum} {parameter.Unit}.");
                result[parameter.Name] = number.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                var value = raw.Trim();
                if (value.Length > 64) throw new ProfessionalScannerValidationException($"Parameter '{parameter.Name}' is too long.");
                result[parameter.Name] = value;
            }
        }
        return result;
    }

    private static IReadOnlyCollection<ProfessionalFilterDefinition> BuildDefinitions() =>
    [
        Event("PRICE_MOMENTUM", "1.0.0", "شتاب قیمت", ["تحرک قیمت", "حرکت قیمت"], ProfessionalFilterCategory.Technical, InsightType.PriceMovement, [MinimumImportance()], "importance desc, detected desc"),
        Event("BUYER_POWER_SURGE", "1.0.0", "قدرت خریدار", ["قدرت خریدار حقیقی", "سرانه خرید"], ProfessionalFilterCategory.Flow, InsightType.BuyerSellerPowerChanged, [MinimumImportance()], "importance desc, detected desc"),
        Event("REAL_MONEY_FLOW", "1.0.0", "ورود پول حقیقی", ["پول حقیقی", "جریان پول حقیقی"], ProfessionalFilterCategory.Flow, InsightType.RealMoneyFlowChanged, [MinimumImportance()], "importance desc, detected desc"),
        Event("VOLUME_ANOMALY", "1.0.0", "حجم مشکوک", ["حجم غیرعادی", "حجم معاملات بالا"], ProfessionalFilterCategory.Volume, InsightType.TradingVolumeAnomaly, [MinimumImportance()], "importance desc, detected desc"),
        Event("QUEUE_CHANGE", "1.0.0", "تغییر صف", ["صف خرید", "صف فروش", "تغییر صف خرید و فروش"], ProfessionalFilterCategory.Queue, InsightType.OrderQueueChanged, [MinimumImportance()], "importance desc, detected desc"),
        Event("LARGE_TRADE_ACTIVITY", "1.0.0", "معاملات عمده", ["خرید و فروش عمده", "معامله بزرگ"], ProfessionalFilterCategory.LargeTrade, InsightType.LargeTradeDetected, [MinimumImportance()], "importance desc, detected desc"),
        Metric("LOW_PE", "1.0.0", "پی به ای پایین", ["P/E پایین", "ارزندگی پی ای"], ProfessionalFilterCategory.Fundamental,
            [Decimal("maxPe", "حداکثر P/E", "ratio", 0.01m, 100m, 5m, ["حداکثر پی ای"])],
            [new("PE_TTM", ConditionOperator.LessThanOrEqual, "maxPe", "ratio", "TrailingTwelveMonths")]),
        Metric("INDUSTRY_SALES_GROWTH", "1.0.0", "رشد فروش صنعت", ["فروش رو به رشد صنعت"], ProfessionalFilterCategory.Industry,
            [Decimal("minGrowthPercent", "حداقل رشد فروش", "percent", -100m, 10000m, 30m, ["حداقل رشد"])],
            [new("MONTHLY_SALES_GROWTH_YOY", ConditionOperator.GreaterThanOrEqual, "minGrowthPercent", "percent", "LatestMonth")]),
        Metric("GROWTH_AT_VALUE", "1.0.0", "رشد با ارزندگی", ["رشد و ارزندگی", "P/E پایین و رشد فروش"], ProfessionalFilterCategory.Composite,
            [Decimal("maxPe", "حداکثر P/E", "ratio", 0.01m, 100m, 7m, ["حداکثر پی ای"]),
             Decimal("minGrowthPercent", "حداقل رشد فروش", "percent", -100m, 10000m, 30m, ["حداقل رشد"])],
            [new("PE_TTM", ConditionOperator.LessThanOrEqual, "maxPe", "ratio", "TrailingTwelveMonths"),
             new("MONTHLY_SALES_GROWTH_YOY", ConditionOperator.GreaterThanOrEqual, "minGrowthPercent", "percent", "LatestMonth")])
    ];

    private static ProfessionalFilterDefinition Event(string code, string version, string title,
        IReadOnlyCollection<string> aliases, ProfessionalFilterCategory category, InsightType type,
        IReadOnlyCollection<ProfessionalFilterParameter> parameters, string ranking) =>
        new(code, version, title, aliases, category, ProfessionalFilterExecutionKind.InsightEvents,
            parameters, [], type, ["InsightEvents", type.ToString()], ProfessionalSessionPolicy.TodayOrHistorical,
            ranking, "symbol asc", EntitlementCode, ProfessionalFilterState.Active);

    private static ProfessionalFilterDefinition Metric(string code, string version, string title,
        IReadOnlyCollection<string> aliases, ProfessionalFilterCategory category,
        IReadOnlyCollection<ProfessionalFilterParameter> parameters,
        IReadOnlyCollection<ProfessionalMetricConditionTemplate> conditions) =>
        new(code, version, title, aliases, category, ProfessionalFilterExecutionKind.MetricScanner,
            parameters, conditions, null, ["DerivedMetrics", ..conditions.Select(item => item.MetricCode)],
            ProfessionalSessionPolicy.LatestCompleteObservation, "scanner score desc", "symbol asc",
            EntitlementCode, ProfessionalFilterState.Active);

    private static ProfessionalFilterParameter MinimumImportance() =>
        Decimal("minimumImportance", "حداقل اهمیت", "score", 0m, 100m, 50m, ["حداقل اهمیت"]);

    private static ProfessionalFilterParameter Decimal(string name, string title, string unit,
        decimal min, decimal max, decimal defaultValue, IReadOnlyCollection<string> aliases) =>
        new(name, title, ProfessionalParameterType.Decimal, unit, min, max, defaultValue, aliases);

    private static string Normalize(string value) => value.Trim().ToLowerInvariant()
        .Replace('ي', 'ی').Replace('ك', 'ک').Replace("‌", " ").Replace("_", " ")
        .Replace("/", " ").Replace("  ", " ");
}
