using System.Globalization;
using FinancialCopilot.Application.FinancialData.Options;

namespace FinancialCopilot.Application.AI.Orchestration;

public static class OptionComparisonCapability
{
    public const string Code = "option_comparison";
}

/// <summary>
/// An explicit option question: the view, the company or contract text (null when missing), and
/// the optional quantity and expiry taken from the wording.
/// </summary>
/// <param name="ExpiryMonth">Jalali month (1–12) named after «سررسید»; resolved against the listed expiries.</param>
/// <param name="ExpiryDate">A full expiry date written in the question.</param>
/// <param name="ContractSymbol">Set when the question named one contract («ضمخا9021»); the company text is then its underlying.</param>
public sealed record OptionComparisonRequest(
    OptionComparisonView View,
    string? CompanyText,
    int Quantity = 1,
    int? ExpiryMonth = null,
    DateOnly? ExpiryDate = null,
    bool NearestExpiryOnly = false,
    string? ContractSymbol = null)
{
    /// <summary>The request as it travels in the task state, so a clarified symbol keeps view, quantity and expiry.</summary>
    public string SlotValue => string.Join(';', new[]
    {
        "view=" + View switch
        {
            OptionComparisonView.Put => "put",
            OptionComparisonView.CoveredCall => "covered_call",
            _ => "call"
        },
        "qty=" + Quantity.ToString(CultureInfo.InvariantCulture),
        ExpiryMonth is { } month ? "month=" + month.ToString(CultureInfo.InvariantCulture) : null,
        ExpiryDate is { } date ? "date=" + date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : null,
        NearestExpiryOnly ? "nearest=1" : null,
        ContractSymbol is { Length: > 0 } contract ? "contract=" + contract : null
    }.Where(part => part is not null));

    public static OptionComparisonRequest ParseSlot(string? value, string? companyText)
    {
        var request = new OptionComparisonRequest(OptionComparisonView.Call, companyText);
        foreach (var part in (value ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0) continue;
            var (key, text) = (part[..separator], part[(separator + 1)..]);
            request = key switch
            {
                "view" => request with
                {
                    View = text switch
                    {
                        "put" => OptionComparisonView.Put,
                        "covered_call" => OptionComparisonView.CoveredCall,
                        _ => OptionComparisonView.Call
                    }
                },
                "qty" when int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var quantity) &&
                           quantity is >= 1 and <= OptionComparisonUseCase.MaxQuantity => request with { Quantity = quantity },
                "month" when int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var month) &&
                             month is >= 1 and <= 12 => request with { ExpiryMonth = month },
                "date" when DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                    => request with { ExpiryDate = date },
                "nearest" => request with { NearestExpiryOnly = text == "1" },
                "contract" when text.Length > 0 => request with { ContractSymbol = text },
                _ => request
            };
        }

        return request;
    }
}

/// <summary>
/// Recognizes explicit, single-symbol option questions: «اختیار خرید اخابر», «زنجیره اختیار اخابر»,
/// «اختیار فروش اخابر», «کاورد کال اخابر», optionally with «۵ قرارداد» and «سررسید دی».
/// Advice wording («بخرم», «توصیه», «سیگنال») and analysis requests are never matched, so they keep
/// their own route. Whether the company exists is the resolver's job.
/// </summary>
public static class OptionComparisonIntentRules
{
    public const int MaxMessageLength = 100;
    private const int MaxCompanyTokens = 3;

    private static readonly HashSet<string> OptionWords = new(StringComparer.Ordinal)
    {
        "اختیار", "اختیارهای", "اختیارات", "آپشن", "اپشن", "آپشنهای", "option", "options"
    };

    private static readonly HashSet<string> CallWords = new(StringComparer.Ordinal) { "خرید", "خریدهای", "خریدها", "call", "calls", "کال" };
    private static readonly HashSet<string> PutWords = new(StringComparer.Ordinal) { "فروش", "فروشهای", "فروشها", "put", "puts", "پوت" };
    private static readonly HashSet<string> CoveredWords = new(StringComparer.Ordinal) { "کاورد", "کاوردکال", "covered" };

    /// <summary>Words that make the message advice, analysis, an alert request or a multi-symbol question.</summary>
    private static readonly HashSet<string> RejectWords = new(StringComparer.Ordinal)
    {
        "بخرم", "بفروشم", "بخریم", "بفروشیم", "بخر", "بفروش", "توصیه", "سیگنال", "پیشنهاد", "buy", "sell",
        "تحلیل", "تکنیکال", "بنیادی", "analysis", "یا", "vs", "و",
        "خبر", "خبرم", "هشدار", "وقتی", "هروقت", "زمانی", "اطلاع", "alert", "notify"
    };

    /// <summary>Politeness, command and framing words removed around the company text.</summary>
    private static readonly HashSet<string> Fillers = new(StringComparer.Ordinal)
    {
        "نماد", "سهم", "سهام", "شرکت", "برای", "را", "رو", "از", "به", "در", "با", "روی", "of", "for", "the", "show", "me", "on",
        "نشان", "نمایش", "بده", "بدهید", "بدین", "کن", "کنید", "بگو", "بگید", "ببینم", "میخواهم", "میخوام",
        "چیست", "چیه", "چه", "چطور", "چقدر", "چند", "است", "هست", "لطفا", "لطفاً", "please", "دارد", "داره", "ی", "های", "ها",
        "معامله", "معاملات", "قراردادهای", "زنجیره", "زنجیرهی", "chain", "مقایسه", "مقایسهی", "compare", "هزینه", "هزینهی",
        "قیمت", "قیمتهای", "بازده", "بازدهی", "مسیر", "موقعیت", "موقعیتهای", "مناسب", "بهترین", "تمام", "میشود", "می", "شود", "اعمال",
        // «خرید اخابر از مسیر اختیار خرید»: a buy/sell word that is not part of the option term is framing.
        "خرید", "فروش"
    };

    private static readonly Dictionary<string, int> JalaliMonths = new(StringComparer.Ordinal)
    {
        ["فروردین"] = 1, ["اردیبهشت"] = 2, ["خرداد"] = 3, ["تیر"] = 4, ["مرداد"] = 5, ["امرداد"] = 5, ["شهریور"] = 6,
        ["مهر"] = 7, ["آبان"] = 8, ["ابان"] = 8, ["آذر"] = 9, ["اذر"] = 9, ["دی"] = 10, ["بهمن"] = 11, ["اسفند"] = 12
    };

    public static OptionComparisonRequest? TryMatch(string? message)
    {
        if (string.IsNullOrWhiteSpace(message) || message.Length > MaxMessageLength)
            return null;
        var tokens = Tokens(message);
        if (tokens.Count == 0 || tokens.Any(RejectWords.Contains))
            return null;

        var consumed = new bool[tokens.Count];
        OptionComparisonView? view = null;
        var hasOptionWord = false;
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (CoveredWords.Contains(token))
            {
                hasOptionWord = true;
                view = OptionComparisonView.CoveredCall;
                consumed[index] = true;
                if (index + 1 < tokens.Count && CallWords.Contains(tokens[index + 1])) consumed[++index] = true;
                continue;
            }

            if (!OptionWords.Contains(token)) continue;

            hasOptionWord = true;
            consumed[index] = true;
            // «فروش اختیار خرید»: writing a call, which is the covered-call view.
            var writing = index > 0 && PutWords.Contains(tokens[index - 1]) && !consumed[index - 1];
            if (index + 1 < tokens.Count && CallWords.Contains(tokens[index + 1]))
            {
                consumed[index + 1] = true;
                view ??= writing ? OptionComparisonView.CoveredCall : OptionComparisonView.Call;
                if (writing) consumed[index - 1] = true;
                index++;
            }
            else if (index + 1 < tokens.Count && PutWords.Contains(tokens[index + 1]))
            {
                consumed[index + 1] = true;
                view ??= OptionComparisonView.Put;
                index++;
            }
        }

        if (!hasOptionWord)
            return null;

        var quantity = 1;
        int? expiryMonth = null;
        DateOnly? expiryDate = null;
        var nearestOnly = false;
        for (var index = 0; index < tokens.Count; index++)
        {
            if (consumed[index]) continue;
            var token = tokens[index];

            // «۵ قرارداد» / «5 قرارداد»
            if (token is "قرارداد" or "قراردادی" && index > 0 && !consumed[index - 1] &&
                int.TryParse(tokens[index - 1], NumberStyles.None, CultureInfo.InvariantCulture, out var count))
            {
                if (count is < 1 or > OptionComparisonUseCase.MaxQuantity) return null;
                quantity = count;
                consumed[index] = consumed[index - 1] = true;
                continue;
            }

            if (token is "سررسید" or "سررسیدهای" or "سررسیدی")
            {
                consumed[index] = true;
                if (index + 1 < tokens.Count && JalaliMonths.TryGetValue(tokens[index + 1], out var month))
                {
                    expiryMonth = month;
                    consumed[++index] = true;
                    if (index + 1 < tokens.Count && tokens[index + 1] == "ماه") consumed[++index] = true;
                }
                else if (TryReadJalaliDate(tokens, index + 1, out var length) is { } date)
                {
                    expiryDate = date;
                    for (var offset = 1; offset <= length; offset++) consumed[index + offset] = true;
                    index += length;
                }
                continue;
            }

            if (token is "نزدیکترین" || token == "نزدیک" && index + 1 < tokens.Count && tokens[index + 1] == "ترین")
            {
                nearestOnly = true;
                consumed[index] = true;
                if (token == "نزدیک") consumed[++index] = true;
                continue;
            }

            if (TryReadJalaliDate(tokens, index, out var dateLength) is { } writtenDate)
            {
                expiryDate = writtenDate;
                for (var offset = 0; offset < dateLength; offset++) consumed[index + offset] = true;
                index += dateLength - 1;
            }
        }

        var company = tokens.Where((token, index) => !consumed[index] && !Fillers.Contains(token)).ToArray();
        if (company.Length > MaxCompanyTokens)
            return null;
        // A stray number that is not a quantity, a date or part of a contract symbol makes the question unclear.
        if (company.Any(token => token.All(char.IsAsciiDigit)))
            return null;

        return new OptionComparisonRequest(
            view ?? OptionComparisonView.Call,
            company.Length == 0 ? null : string.Join(' ', company),
            quantity, expiryMonth, expiryDate, nearestOnly);
    }

    public static QueryInterpretation BuildInterpretation(string message, OptionComparisonRequest request, int registryVersion)
    {
        const decimal confidence = 1m;
        var evidence = new InterpretationEvidence(OptionComparisonCapability.Code, "explicit-option-comparison", QueryValueProvenance.UserExplicit);
        var mentions = request.CompanyText is null
            ? Array.Empty<EntityMention>()
            : [new EntityMention(request.CompanyText, 0, request.CompanyText.Length)];
        return new QueryInterpretation(
            message,
            QueryNormalization.Normalize(message),
            "fa",
            [new CapabilityCandidate(OptionComparisonCapability.Code, registryVersion, confidence, [evidence])],
            mentions,
            [],
            null,
            null,
            null,
            [],
            [],
            confidence,
            [evidence],
            registryVersion,
            InterpretationConfidencePolicy.Band(confidence));
    }

    /// <summary>The Jalali month of a date, for matching «سررسید دی» against listed expiries.</summary>
    public static int JalaliMonthOf(DateOnly date) =>
        new PersianCalendar().GetMonth(date.ToDateTime(TimeOnly.MinValue));

    /// <summary>
    /// A Jalali date starting at <paramref name="index"/>: one token («1405/09/18») or, because
    /// normalization turns the separators into spaces, three number tokens («1405 09 18»).
    /// </summary>
    private static DateOnly? TryReadJalaliDate(IReadOnlyList<string> tokens, int index, out int length)
    {
        length = 0;
        if (index >= tokens.Count) return null;
        if (TryParseJalaliDate(tokens[index]) is { } single)
        {
            length = 1;
            return single;
        }

        if (index + 2 < tokens.Count &&
            TryParseJalaliDate(string.Join('/', tokens[index], tokens[index + 1], tokens[index + 2])) is { } split)
        {
            length = 3;
            return split;
        }

        return null;
    }

    /// <summary>«1405/09/18» or «1405-09-18» as a Gregorian date; null when it is not a valid Jalali date.</summary>
    private static DateOnly? TryParseJalaliDate(string token)
    {
        var parts = token.Split('/', '-');
        if (parts.Length != 3 ||
            !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var year) ||
            !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var month) ||
            !int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var day) ||
            year is < 1390 or > 1500 || month is < 1 or > 12 || day is < 1 or > 31)
            return null;
        try
        {
            return DateOnly.FromDateTime(new PersianCalendar().ToDateTime(year, month, day, 0, 0, 0, 0));
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static List<string> Tokens(string message)
    {
        var digits = new string(message.Select(character => character switch
        {
            >= '۰' and <= '۹' => (char)('0' + character - '۰'),
            >= '٠' and <= '٩' => (char)('0' + character - '٠'),
            '؟' or '?' or '،' or ',' or '.' or '!' or '«' or '»' or ':' => ' ',
            _ => character
        }).ToArray());
        var text = QueryNormalization.Normalize(digits.Replace('ۀ', 'ه').Replace("ٔ", string.Empty, StringComparison.Ordinal))
            .ToLowerInvariant();
        return text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }
}
