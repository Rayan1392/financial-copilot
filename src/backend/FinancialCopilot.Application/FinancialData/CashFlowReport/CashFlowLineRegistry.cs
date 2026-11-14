namespace FinancialCopilot.Application.FinancialData.CashFlowReport;

public enum CashFlowSection { None, Operating, Investing, Financing, Reconciliation }
public enum CashFlowVerification { Both, Bank, Proposed, Excluded }

/// <summary>Approved source role and evidence state; order follows the mapping matrix.</summary>
public sealed record CashFlowLineDefinition(
    int SourceItemId, string Label, CashFlowSection Section, string Role, string Visibility,
    string Industry, CashFlowVerification Verification, string? MetricComponent)
{
    public string ReportLabel => SourceItemId switch
    {
        128 => "جریان خالص ورود (خروج) نقد حاصل از فعالیت‌های عملیاتی",
        121 => "جریان خالص ورود (خروج) نقد حاصل از فعالیت‌های سرمایه‌گذاری",
        123 => "جریان خالص ورود (خروج) نقد حاصل از فعالیت‌های تأمین مالی",
        33 => "مانده وجه نقد در ابتدای سال مالی",
        125 => "مانده وجه نقد در پایان دوره",
        _ => Label
    };

    public bool VerifiedFor(string template) => Verification == CashFlowVerification.Both ||
        Verification == CashFlowVerification.Bank && template == CashFlowLineRegistry.BankingTemplate;

    public bool CanDisplay(string template) => (template is CashFlowLineRegistry.GeneralTemplate or CashFlowLineRegistry.BankingTemplate) && VerifiedFor(template) &&
        (Industry == "all" || Industry == "general" && template == CashFlowLineRegistry.GeneralTemplate ||
         Industry == "bank" && template == CashFlowLineRegistry.BankingTemplate) &&
        Visibility is "detail" or "of-which" or "subtotal";
}

public sealed record CashFlowDerivedLineDefinition(string RowId, string Label, CashFlowSection Section, string Role);

/// <summary>Single owner of Feature 161 source mappings. Proposed mappings are not displayable.</summary>
public static class CashFlowLineRegistry
{
    public const string GeneralTemplate = "GeneralCashFlow";
    public const string BankingTemplate = "BankingCashFlow";
    public const int OperatingTotal = 128;
    public const int InvestingTotal = 121;
    public const int BeforeFinancingCrossCheck = 122;
    public const int FinancingTotal = 123;
    public const int NetChange = 124;
    public const int OpeningCash = 33;
    public const int FxEffect = 35;
    public const int ClosingCash = 125;
    public const int LegacyPresentationMarker = 120;

    // Hard exclusion from value reads, including former fallback candidates and legacy item 1.
    public static IReadOnlySet<int> ExcludedValueItems { get; } =
        new HashSet<int> { 1, 58, 59, 60, 61, 62, 63, 106, 107, 108, 109, 110, 111, 112, 113, 126, 127 };

    public static IReadOnlyList<CashFlowLineDefinition> Lines { get; } =
    [
        new(128, "جریان وجه نقد عملیاتی - جدید", CashFlowSection.Operating, "primary-total", "subtotal", "all", CashFlowVerification.Both, "operating_cash_flow"),
        new(9, "مالیات بر درآمد پرداختی", CashFlowSection.Operating, "detail", "of-which", "all", CashFlowVerification.Proposed, null),
        new(121, "جریان وجه نقد سرمایه گذاری - کدال", CashFlowSection.Investing, "primary-total", "subtotal", "all", CashFlowVerification.Both, "investing_cash_flow"),
        new(10, "وجوه پرداختی بابت خرید دارایی‌های ثابت مشهود", CashFlowSection.Investing, "detail", "detail", "all", CashFlowVerification.Both, "capex"),
        new(48, "وجوه پرداختی بابت تحصیل دارایی های نامشهود", CashFlowSection.Investing, "detail", "detail", "all", CashFlowVerification.Bank, "capex"),
        new(46, "وجوه پرداختی جهت خرید دارایی زیستی مولد", CashFlowSection.Investing, "detail", "detail", "general", CashFlowVerification.Proposed, "capex"),
        new(14, "وجوه حاصل از فروش دارایی های ثابت مشهود", CashFlowSection.Investing, "detail", "detail", "all", CashFlowVerification.Both, null),
        new(83, "وجوه دریافتی بابت فروش دارایی های نامشهود", CashFlowSection.Investing, "detail", "detail", "all", CashFlowVerification.Proposed, null),
        new(47, "وجوه حاصل از فروش دارایی زیستی مولد", CashFlowSection.Investing, "detail", "detail", "general", CashFlowVerification.Proposed, null),
        new(11, "وجوه پرداختی جهت خرید سرمایه‌گذاری‌های بلندمدت", CashFlowSection.Investing, "detail", "detail", "all", CashFlowVerification.Proposed, null),
        new(15, "وجوه حاصل از فروش سرمایه گذاری بلند مدت", CashFlowSection.Investing, "detail", "detail", "all", CashFlowVerification.Proposed, null),
        new(12, "وجوه پرداختی بابت تحصیل سرمایه‌گذاری‌های کوتاه مدت", CashFlowSection.Investing, "detail", "detail", "all", CashFlowVerification.Proposed, null),
        new(17, "وجوه حاصل از فروش سرمایه گذاری های کوتاه مدت", CashFlowSection.Investing, "detail", "detail", "all", CashFlowVerification.Proposed, null),
        new(70, "وجوه پرداختی جهت تحصیل شرکتهای فرعی و وابسته", CashFlowSection.Investing, "detail", "detail", "all", CashFlowVerification.Proposed, null),
        new(71, "وجوه دریافتی بابت فروش شرکتهای فرعی و وابسته", CashFlowSection.Investing, "detail", "detail", "all", CashFlowVerification.Proposed, null),
        new(103, "وجوه پرداختی بابت خرید دارایی های غیر جاری نگهداری شده برای فروش", CashFlowSection.Investing, "detail", "detail", "all", CashFlowVerification.Proposed, null),
        new(104, "وجوه دریافتی حاصل از فروش دارایی های غیر جاری نگهداری شده برای فروش", CashFlowSection.Investing, "detail", "detail", "all", CashFlowVerification.Bank, null),
        new(130, "پرداخت های نقدی برای تحصیل سرمایه گذاری در املاک", CashFlowSection.Investing, "detail", "detail", "all", CashFlowVerification.Proposed, null),
        new(129, "دریافت های نقدی حاصل از فروش سرمایه گذاری در املاک", CashFlowSection.Investing, "detail", "detail", "all", CashFlowVerification.Proposed, null),
        new(4, "سود سهام دریافتی", CashFlowSection.Investing, "detail", "detail", "all", CashFlowVerification.Bank, null),
        new(3, "سود دریافتی بابت سپرده های سرمایه گذاری و اوراق مشارکت بلند مدت", CashFlowSection.Investing, "detail", "detail", "general", CashFlowVerification.Proposed, null),
        new(42, "سود دریافتی بابت تسهیلات مالی", CashFlowSection.Investing, "detail", "detail", "general", CashFlowVerification.Proposed, null),
        new(95, "وام نقدی پرداختی به سایر اشخاص", CashFlowSection.Investing, "detail", "detail", "general", CashFlowVerification.Proposed, null),
        new(96, "وصول (اقساط / اصل) وام نقدی پرداختی به سایر اشخاص", CashFlowSection.Investing, "detail", "detail", "general", CashFlowVerification.Proposed, null),
        new(13, "وجوه پرداختی جهت خرید سایر دارایی‌ها و موجودی", CashFlowSection.Investing, "detail", "provenance", "all", CashFlowVerification.Proposed, null),
        new(122, "جریان وجه نقد قبل از فعالیت های تامین مالی - کدال", CashFlowSection.None, "cross-check", "provenance", "all", CashFlowVerification.Both, null),
        new(123, "جریان وجه نقد تامین مالی - کدال", CashFlowSection.Financing, "primary-total", "subtotal", "all", CashFlowVerification.Both, "financing_cash_flow"),
        new(23, "دریافت تسهیلات مالی", CashFlowSection.Financing, "detail", "detail", "all", CashFlowVerification.Both, "net_debt_cash_flow"),
        new(24, "بازپرداخت اصل تسهیلات مالی دریافتی", CashFlowSection.Financing, "detail", "detail", "all", CashFlowVerification.Both, "net_debt_cash_flow"),
        new(5, "سود پرداختی بابت تسهیلات مالی/ پیش دریافت ها و سایر حساب های پرداختنی", CashFlowSection.Financing, "detail", "detail", "all", CashFlowVerification.Both, "interest_paid"),
        new(6, "سود سهام پرداختی", CashFlowSection.Financing, "detail", "detail", "all", CashFlowVerification.Both, "dividends_paid"),
        new(117, "وجوه پرداختی بابت خرید سهام خزانه", CashFlowSection.Financing, "detail", "detail", "all", CashFlowVerification.Bank, "treasury_net"),
        new(119, "وجوه دریافتی حاصل از فروش سهام خزانه", CashFlowSection.Financing, "detail", "detail", "all", CashFlowVerification.Bank, "treasury_net"),
        new(21, "وجوه دریافتی حاصل از افزایش سرمایه/حق تقدم", CashFlowSection.Financing, "detail", "detail", "all", CashFlowVerification.Proposed, "capital_transactions"),
        new(131, "دریافت های نقدی حاصل از صرف سهام", CashFlowSection.Financing, "detail", "detail", "all", CashFlowVerification.Proposed, "capital_transactions"),
        new(40, "وجوه دریافت شده بابت انتشار اوراق مشارکت", CashFlowSection.Financing, "detail", "detail", "all", CashFlowVerification.Proposed, "net_debt_cash_flow"),
        new(41, "باز پرداخت اصل اوراق مشارکت", CashFlowSection.Financing, "detail", "detail", "all", CashFlowVerification.Proposed, "net_debt_cash_flow"),
        new(37, "سود پرداختی بابت اوراق مشارکت", CashFlowSection.Financing, "detail", "detail", "all", CashFlowVerification.Proposed, "interest_paid"),
        new(132, "دریافت های نقدی حاصل از انتشار اوراق خرید دین", CashFlowSection.Financing, "detail", "detail", "all", CashFlowVerification.Proposed, "net_debt_cash_flow"),
        new(133, "پرداخت های نقدی بابت اصل اوراق خرید دین", CashFlowSection.Financing, "detail", "detail", "all", CashFlowVerification.Proposed, "net_debt_cash_flow"),
        new(134, "پرداخت های نقدی بابت سود اوراق خرید دین", CashFlowSection.Financing, "detail", "detail", "all", CashFlowVerification.Proposed, "interest_paid"),
        new(114, "بازپرداخت اوراق اجاره (صکوک) / اجاره سرمایه ای", CashFlowSection.Financing, "detail", "detail", "all", CashFlowVerification.Proposed, "net_debt_cash_flow"),
        new(135, "پرداخت های نقدی بابت سود اجاره سرمایه‌ای", CashFlowSection.Financing, "detail", "detail", "all", CashFlowVerification.Proposed, "interest_paid"),
        new(22, "وجوه حاصل از افزایش سرمایه شرکت های فرعی - سهم اقلیت", CashFlowSection.Financing, "none", "provenance", "consolidated-only", CashFlowVerification.Excluded, null),
        new(136, "پرداخت‌های نقدی ناشی از تحصیل منافع فاقد حق کنترل", CashFlowSection.Financing, "none", "provenance", "consolidated-only", CashFlowVerification.Excluded, null),
        new(137, "دریافت‌های نقدی ناشی از واگذاری بخشی از شرکت فرعی - با حفظ کنترل", CashFlowSection.Financing, "none", "provenance", "consolidated-only", CashFlowVerification.Excluded, null),
        new(138, "دریافت‌های نقدی ناشی از فروش سهام خزانه توسط شرکت فرعی- سهم منافع فاقد حق کنترل", CashFlowSection.Financing, "none", "provenance", "consolidated-only", CashFlowVerification.Excluded, null),
        new(139, "پرداخت‌های نقدی برای خرید سهام خزانه توسط شرکت فرعی- سهم منافع فاقد حق کنترل", CashFlowSection.Financing, "none", "provenance", "consolidated-only", CashFlowVerification.Excluded, null),
        new(124, "خالص افزایش (کاهش) در وجه نقد - کدال", CashFlowSection.Reconciliation, "primary-total", "subtotal", "all", CashFlowVerification.Both, "net_change"),
        new(33, "مانده وجه نقد در آغاز سال", CashFlowSection.Reconciliation, "reconciliation-input", "detail", "all", CashFlowVerification.Both, null),
        new(35, "تاثیر تغییرات نرخ ارز", CashFlowSection.Reconciliation, "reconciliation-input", "detail", "all", CashFlowVerification.Both, null),
        new(125, "مانده وجه نقد در پایان سال - کدال", CashFlowSection.Reconciliation, "primary-total", "subtotal", "all", CashFlowVerification.Both, "closing_cash"),
        new(120, "جریان وجه نقد بازده سرمایه گذاریها و سود پرداختی بابت تامین مالی - کدال", CashFlowSection.None, "presentation-marker", "hidden", "all", CashFlowVerification.Proposed, null),
        new(126, "جریان وجه نقد سرمایه گذاری - جدید", CashFlowSection.None, "fallback-candidate", "provenance", "all", CashFlowVerification.Proposed, null),
        new(127, "جریان وجه نقد تامین مالی - جدید", CashFlowSection.None, "fallback-candidate", "provenance", "all", CashFlowVerification.Proposed, null),
        new(1, "جریان وجه نقد عملیاتی", CashFlowSection.None, "cross-check-candidate", "provenance", "all", CashFlowVerification.Proposed, null),
        new(58, "جریان وجه نقد بازده سرمایه گذاریها و سود پرداختی بابت تامین مالی", CashFlowSection.None, "none", "hidden", "all", CashFlowVerification.Excluded, null),
        new(59, "جریان وجه نقد سرمایه گذاری - قدیم", CashFlowSection.None, "none", "hidden", "all", CashFlowVerification.Excluded, null),
        new(60, "جریان وجه نقد قبل از فعالیت های تامین مالی ", CashFlowSection.None, "none", "hidden", "all", CashFlowVerification.Excluded, null),
        new(61, "جریان وجه نقد تامین مالی - قدیم", CashFlowSection.None, "none", "hidden", "all", CashFlowVerification.Excluded, null),
        new(62, "خالص افزایش (کاهش) در وجه نقد", CashFlowSection.None, "none", "hidden", "all", CashFlowVerification.Excluded, null),
        new(63, "مانده وجه نقد در پایان سال", CashFlowSection.None, "none", "hidden", "all", CashFlowVerification.Excluded, null),
        new(106, "مجموع منابع بازده سرمایه گذاری ها", CashFlowSection.None, "none", "hidden", "all", CashFlowVerification.Excluded, null),
        new(107, "مجموع مصارف بازده سرمایه گذاری ها", CashFlowSection.None, "none", "hidden", "all", CashFlowVerification.Excluded, null),
        new(108, "مجموع منابع سرمایه گذاری ها", CashFlowSection.None, "none", "hidden", "all", CashFlowVerification.Excluded, null),
        new(109, "مجموع مصارف سرمایه گذاری ها", CashFlowSection.None, "none", "hidden", "all", CashFlowVerification.Excluded, null),
        new(110, "مجموع منابع تامین مالی", CashFlowSection.None, "none", "hidden", "all", CashFlowVerification.Excluded, null),
        new(111, "مجموع مصارف تامین مالی", CashFlowSection.None, "none", "hidden", "all", CashFlowVerification.Excluded, null),
        new(112, "مجموع جریانات نقد ورودی", CashFlowSection.None, "none", "hidden", "all", CashFlowVerification.Excluded, null),
        new(113, "مجموع جریانات نقد خروجی", CashFlowSection.None, "none", "hidden", "all", CashFlowVerification.Excluded, null),
    ];

    public static IReadOnlyList<int> ReadableSourceItemIds { get; } = Lines
        .Where(line => !ExcludedValueItems.Contains(line.SourceItemId))
        .Select(line => line.SourceItemId).Distinct().Order().ToArray();

    public static IReadOnlyList<CashFlowDerivedLineDefinition> ResidualLines { get; } =
    [
        new("investing_residual", "سایر اقلام سرمایه‌گذاری (مابه‌التفاوت)", CashFlowSection.Investing, "residual"),
        new("financing_residual", "سایر اقلام تأمین مالی (مابه‌التفاوت)", CashFlowSection.Financing, "residual")
    ];

    public static IReadOnlyList<CashFlowLineDefinition> Displayable(string template) =>
        Lines.Where(line => line.CanDisplay(template)).ToArray();

    public static CashFlowLineDefinition? Find(int sourceItemId) =>
        Lines.FirstOrDefault(line => line.SourceItemId == sourceItemId);
}
