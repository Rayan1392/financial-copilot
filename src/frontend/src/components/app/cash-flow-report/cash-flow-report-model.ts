const amount = new Intl.NumberFormat("fa-IR-u-nu-arabext", { maximumFractionDigits: 0 });
const percent = new Intl.NumberFormat("fa-IR-u-nu-arabext", {
  minimumFractionDigits: 2,
  maximumFractionDigits: 2,
});

export function formatCashFlowAmount(value?: number | null, state?: string): string {
  if (value == null || ["unavailable", "not_reported", "not_applicable"].includes(state ?? ""))
    return "—";
  return value < 0 ? `(${amount.format(Math.abs(value))})` : amount.format(value);
}

export function formatCashFlowPercent(value?: number | null): string {
  return value == null ? "—" : `${percent.format(value)}٪`;
}

export function cashFlowStateLabel(state: string): string {
  return (
    (
      {
        verified: "تطبیق‌شده",
        verified_with_warnings: "تطبیق‌شده با هشدار",
        passed: "تطبیق کامل",
        complete: "پوشش کامل اقلام ثبت‌شده",
        documented_difference: "اختلاف تخصیص‌نیافته",
        insufficient_detail: "اطلاعات تفصیلی ناکافی",
        inconsistent_source_data: "ناسازگاری دادهٔ منبع",
        mapping_unverified: "طبقه‌بندی تأیید نشده",
        warning: "نیازمند بررسی",
        failed: "مغایرت",
        unverified: "تطبیق‌نشده",
        unavailable: "ناموجود",
        not_reported: "گزارش نشده",
        not_applicable: "کاربرد ندارد",
      } as Record<string, string>
    )[state] ?? "وضعیت نامشخص"
  );
}

export function cashFlowReasonText(reason?: string | null): string {
  if (!reason) return "";
  return (
    (
      {
        operating_total_missing: "اقلام لازم برای محاسبه جریان نقد عملیاتی گزارش نشده‌اند.",
        missing_predecessor: "گزارش تجمعی دوره قبل برای محاسبه فصل مستقل موجود نیست.",
        presentation_changed: "مبنای ارائه گزارش‌های مقایسه‌ شده یکسان نیست.",
        not_reported: "این قلم در گزارش منتشرشده وجود ندارد.",
        component_unavailable: "یکی از اجزای لازم در دسترس نیست.",
        capex_component_missing: "مبلغ خرید دارایی ثابت مشهود در گزارش موجود نیست.",
        capex_mapping_unverified:
          "محاسبهٔ جامع مخارج سرمایه‌ای به‌دلیل طبقه‌بندی تأییدنشدهٔ برخی اقلام ارائه نمی‌شود.",
        unexpected_capex_sign: "علامت مبلغ خرید دارایی ثابت مشهود با ماهیت پرداخت سازگار نیست.",
        zero_baseline: "در دورهٔ مبنا مبلغی گزارش نشده و درصد تغییر معنادار نیست.",
        negative_baseline: "به‌دلیل منفی‌بودن مبلغ دورهٔ مبنا، درصد تغییر ارائه نمی‌شود.",
        duplicate_line_item: "برای یک قلم، مبالغ تکراری و ناسازگار گزارش شده است.",
        fx_missing_reconciliation_gap: "اثر نرخ ارز گزارش نشده و مانده‌ها بدون آن تطبیق ندارند.",
        non_positive_revenue: "نسبت به‌دلیل فروش صفر یا منفی معنادار نیست.",
        non_positive_operating_cash_flow:
          "نسبت به‌دلیل جریان نقد عملیاتی صفر یا منفی معنادار نیست.",
        zero_capex: "پوشش مخارج سرمایه‌ای با مخرج صفر محاسبه نمی‌شود.",
        non_positive_net_profit: "نسبت به‌دلیل سود خالص صفر یا منفی معنادار نیست.",
        income_statement_unavailable: "داده هم‌دوره صورت سود و زیان موجود نیست.",
        mixed_sign_change:
          "تغییرات اثر نرخ ارز و وجه نقد هم‌جهت نیست؛ محاسبه این نسبت معنادار نیست.",
      } as Record<string, string>
    )[reason] ?? "جزئیات دلیل محاسبه در دسترس نیست."
  );
}
