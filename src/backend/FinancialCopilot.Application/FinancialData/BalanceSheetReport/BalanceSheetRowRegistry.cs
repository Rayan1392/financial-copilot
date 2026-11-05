namespace FinancialCopilot.Application.FinancialData.BalanceSheetReport;

public enum BalanceSheetSectionId { Assets, Liabilities, Equity }

/// <summary>Where a row sits inside its section (spec §6.3).</summary>
public enum BalanceSheetPlacement { Section, Current, NonCurrent }

public enum BalanceSheetRowKind { Detail, Subtotal, Total, FinalTotal, Analytical }

/// <summary>
/// A fallback source item and whether its semantic equivalence to the primary is verified (E-1).
/// Only verified fallbacks may supply a value or raise a visible discrepancy (PD-13).
/// </summary>
public sealed record BalanceSheetFallback(int SourceItemId, bool Verified);

public sealed record BalanceSheetRowDefinition(
    string RowId,
    string Label,
    BalanceSheetSectionId Section,
    BalanceSheetPlacement Placement,
    BalanceSheetRowKind Kind,
    int PrimarySourceItemId,
    BalanceSheetFallback? Fallback,
    bool Common,
    string PlacementBasis);

/// <summary>
/// Single owner of the Feature 154 balance-sheet templates (design §2): row order, sections and subgroups,
/// source-title labels, primaries, fallback candidates with their verification state, exclusions and
/// analytical items. Evidence-gated changes happen only here: E-1 flips one fallback to verified, E-8 moves
/// one row into a subgroup. T01 (2026-11-04) verified neither, so every fallback is unverified and every
/// General detail row except 47 and 51 stays at section level (PD-22).
/// </summary>
public static class BalanceSheetRowRegistry
{
    public const int CashItem = 1;
    public const int CurrentAssetsTotalItem = 135;
    public const int NonCurrentAssetsTotalItem = 137;
    public const int TotalAssetsItem = 138;
    public const int CurrentLiabilitiesTotalItem = 139;
    public const int NonCurrentLiabilitiesTotalItem = 140;
    public const int TotalLiabilitiesItem = 143;
    public const int ShareCapitalItem = 57;
    public const int RetainedEarningsItem = 71;
    public const int TotalEquityItem = 147;
    public const int TotalLiabilitiesAndEquityItem = 148;

    /// <summary>Never rendered as rows: 286 period metadata, 60 share premium, 146 reserves, 160 parent equity, 188 paid capital.</summary>
    public static readonly IReadOnlyCollection<int> ExcludedSourceItemIds = [286, 60, 146, 160, 188];

    private static BalanceSheetFallback Unverified(int id) => new(id, Verified: false);

    private static BalanceSheetRowDefinition Row(string rowId, string label, BalanceSheetSectionId section,
        BalanceSheetPlacement placement, BalanceSheetRowKind kind, int primary, BalanceSheetFallback? fallback,
        bool common, string basis = "section_only") =>
        new(rowId, label, section, placement, kind, primary, fallback, common, basis);

    // Common rows always render and show «—» when absent.
    private static readonly BalanceSheetRowDefinition Cash = Row("cash", "موجودی نقد",
        BalanceSheetSectionId.Assets, BalanceSheetPlacement.Section, BalanceSheetRowKind.Detail, 1, null, true, "E-8");
    private static readonly BalanceSheetRowDefinition CurrentAssets = Row("current_assets_total", "جمع دارایی‌های جاری",
        BalanceSheetSectionId.Assets, BalanceSheetPlacement.Current, BalanceSheetRowKind.Subtotal, 135, Unverified(298), true, "definition");
    private static readonly BalanceSheetRowDefinition NonCurrentAssets = Row("non_current_assets_total", "جمع دارایی‌های غیرجاری",
        BalanceSheetSectionId.Assets, BalanceSheetPlacement.NonCurrent, BalanceSheetRowKind.Subtotal, 137, Unverified(299), true, "definition");
    private static readonly BalanceSheetRowDefinition TotalAssets = Row("total_assets", "جمع کل دارایی‌ها",
        BalanceSheetSectionId.Assets, BalanceSheetPlacement.Section, BalanceSheetRowKind.FinalTotal, 138, Unverified(300), true, "definition");
    private static readonly BalanceSheetRowDefinition CurrentLiabilities = Row("current_liabilities_total", "جمع بدهی‌های جاری",
        BalanceSheetSectionId.Liabilities, BalanceSheetPlacement.Current, BalanceSheetRowKind.Subtotal, 139, Unverified(301), true, "definition");
    private static readonly BalanceSheetRowDefinition NonCurrentLiabilities = Row("non_current_liabilities_total", "جمع بدهی‌های غیرجاری",
        BalanceSheetSectionId.Liabilities, BalanceSheetPlacement.NonCurrent, BalanceSheetRowKind.Subtotal, 140, Unverified(302), true, "definition");
    private static readonly BalanceSheetRowDefinition TotalLiabilities = Row("total_liabilities", "جمع کل بدهی‌ها",
        BalanceSheetSectionId.Liabilities, BalanceSheetPlacement.Section, BalanceSheetRowKind.Total, 143, Unverified(303), true, "definition");
    private static readonly BalanceSheetRowDefinition ShareCapital = Row("share_capital", "سرمایه",
        BalanceSheetSectionId.Equity, BalanceSheetPlacement.Section, BalanceSheetRowKind.Detail, 57, null, true);
    private static readonly BalanceSheetRowDefinition RetainedEarnings = Row("retained_earnings", "سود (زیان) انباشته",
        BalanceSheetSectionId.Equity, BalanceSheetPlacement.Section, BalanceSheetRowKind.Detail, 71, null, true);
    private static readonly BalanceSheetRowDefinition TotalEquity = Row("total_equity", "جمع حقوق صاحبان سهام",
        BalanceSheetSectionId.Equity, BalanceSheetPlacement.Section, BalanceSheetRowKind.Total, 147, Unverified(304), true, "definition");
    private static readonly BalanceSheetRowDefinition TotalLiabilitiesAndEquity = Row("total_liabilities_and_equity",
        "جمع بدهی‌ها و حقوق صاحبان سهام", BalanceSheetSectionId.Equity, BalanceSheetPlacement.Section,
        BalanceSheetRowKind.FinalTotal, 148, Unverified(305), true, "definition");

    /// <summary>Banking template (PD-12): details directly under their section, then the reported subtotals.</summary>
    public static readonly IReadOnlyList<BalanceSheetRowDefinition> Banking =
    [
        Cash,
        Row("bank_receivables_central_bank", "مطالبات از بانک مرکزی", BalanceSheetSectionId.Assets, BalanceSheetPlacement.Section, BalanceSheetRowKind.Detail, 2, null, false),
        Row("bank_receivables_banks", "مطالبات از بانک‌ها و موسسات اعتباری", BalanceSheetSectionId.Assets, BalanceSheetPlacement.Section, BalanceSheetRowKind.Detail, 4, null, false),
        Row("bank_loans_granted", "مجموع تسهیلات اعطایی", BalanceSheetSectionId.Assets, BalanceSheetPlacement.Section, BalanceSheetRowKind.Detail, 291, null, false),
        CurrentAssets,
        NonCurrentAssets,
        TotalAssets,
        Row("bank_deposits", "مجموع سپرده‌های جذب‌شده", BalanceSheetSectionId.Liabilities, BalanceSheetPlacement.Section, BalanceSheetRowKind.Detail, 223, null, false),
        Row("bank_due_to_banks", "بدهی به بانک‌ها و موسسات اعتباری", BalanceSheetSectionId.Liabilities, BalanceSheetPlacement.Section, BalanceSheetRowKind.Detail, 39, null, false),
        CurrentLiabilities,
        NonCurrentLiabilities,
        TotalLiabilities,
        ShareCapital,
        Row("bank_treasury_shares", "سهام خزانه", BalanceSheetSectionId.Equity, BalanceSheetPlacement.Section, BalanceSheetRowKind.Detail, 170, null, false),
        RetainedEarnings,
        TotalEquity,
        TotalLiabilitiesAndEquity
    ];

    /// <summary>General template (PD-22): only 47 and 51 are verified subgroup members, by their source labels.</summary>
    public static readonly IReadOnlyList<BalanceSheetRowDefinition> General =
    [
        Cash,
        Row("trade_receivables", "دریافتنی‌های تجاری و سایر دریافتنی‌ها", BalanceSheetSectionId.Assets, BalanceSheetPlacement.Section, BalanceSheetRowKind.Detail, 306, Unverified(293), false, "E-8"),
        Row("inventories", "موجودی مواد و کالا", BalanceSheetSectionId.Assets, BalanceSheetPlacement.Section, BalanceSheetRowKind.Detail, 18, null, false, "E-8"),
        Row("orders_prepayments", "سفارشات و پیش‌پرداخت‌ها", BalanceSheetSectionId.Assets, BalanceSheetPlacement.Section, BalanceSheetRowKind.Detail, 19, null, false, "E-8"),
        Row("ppe", "دارایی‌های ثابت مشهود", BalanceSheetSectionId.Assets, BalanceSheetPlacement.Section, BalanceSheetRowKind.Detail, 21, null, false, "E-8"),
        CurrentAssets,
        NonCurrentAssets,
        TotalAssets,
        Row("trade_payables", "پرداختنی‌های تجاری و سایر پرداختنی‌ها", BalanceSheetSectionId.Liabilities, BalanceSheetPlacement.Section, BalanceSheetRowKind.Detail, 307, Unverified(308), false, "E-8"),
        Row("advances_received", "پیش‌دریافت‌ها", BalanceSheetSectionId.Liabilities, BalanceSheetPlacement.Section, BalanceSheetRowKind.Detail, 45, null, false, "E-8"),
        Row("short_term_borrowings", "تسهیلات مالی دریافتی کوتاه مدت", BalanceSheetSectionId.Liabilities, BalanceSheetPlacement.Current, BalanceSheetRowKind.Detail, 47, null, false, "source_label"),
        CurrentLiabilities,
        Row("long_term_borrowings", "تسهیلات مالی دریافتی بلند مدت", BalanceSheetSectionId.Liabilities, BalanceSheetPlacement.NonCurrent, BalanceSheetRowKind.Detail, 51, null, false, "source_label"),
        NonCurrentLiabilities,
        TotalLiabilities,
        ShareCapital,
        Row("capital_increase_funds", "وجوه ناشی از افزایش سرمایه", BalanceSheetSectionId.Equity, BalanceSheetPlacement.Section, BalanceSheetRowKind.Detail, 58, null, false),
        Row("revaluation_surplus", "مازاد تجدید ارزیابی", BalanceSheetSectionId.Equity, BalanceSheetPlacement.Section, BalanceSheetRowKind.Detail, 64, null, false),
        RetainedEarnings,
        TotalEquity,
        TotalLiabilitiesAndEquity
    ];

    /// <summary>Reported analytical items outside the hierarchy; never summed (General template only).</summary>
    public static readonly IReadOnlyList<BalanceSheetRowDefinition> GeneralAnalytical =
    [
        Row("net_working_capital", "خالص سرمایه در گردش (گزارش‌شده)", BalanceSheetSectionId.Assets, BalanceSheetPlacement.Section, BalanceSheetRowKind.Analytical, 145, null, false, "analytical"),
        Row("interest_bearing_liabilities", "جمع کل بدهی‌های بهره‌دار (گزارش‌شده)", BalanceSheetSectionId.Liabilities, BalanceSheetPlacement.Section, BalanceSheetRowKind.Analytical, 144, null, false, "analytical")
    ];

    public static IReadOnlyList<BalanceSheetRowDefinition> Rows(string templateId) =>
        templateId == BalanceSheetTemplates.Banking ? Banking : General;

    public static IReadOnlyList<BalanceSheetRowDefinition> Analytical(string templateId) =>
        templateId == BalanceSheetTemplates.Banking ? [] : GeneralAnalytical;

    /// <summary>Every primary and fallback source item of both templates; the line-item query reads only these.</summary>
    public static IReadOnlyList<int> RegisteredSourceItemIds { get; } = Banking.Concat(General).Concat(GeneralAnalytical)
        .SelectMany(row => row.Fallback is null ? [row.PrimarySourceItemId] : new[] { row.PrimarySourceItemId, row.Fallback.SourceItemId })
        .Distinct().Order().ToArray();

    public static string SectionKey(BalanceSheetSectionId section) => section switch
    {
        BalanceSheetSectionId.Assets => "assets",
        BalanceSheetSectionId.Liabilities => "liabilities",
        _ => "equity"
    };

    public static string SectionLabel(BalanceSheetSectionId section) => section switch
    {
        BalanceSheetSectionId.Assets => "دارایی‌ها",
        BalanceSheetSectionId.Liabilities => "بدهی‌ها",
        _ => "حقوق صاحبان سهام"
    };

    public static string KindKey(BalanceSheetRowKind kind) => kind switch
    {
        BalanceSheetRowKind.Detail => "detail",
        BalanceSheetRowKind.Subtotal => "subtotal",
        BalanceSheetRowKind.Total => "total",
        BalanceSheetRowKind.FinalTotal => "final_total",
        _ => "analytical"
    };
}
