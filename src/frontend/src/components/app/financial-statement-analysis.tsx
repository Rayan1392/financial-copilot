import type { FinancialStatementAnalysisResult } from "@/lib/chat.functions";
import { formatPercent } from "@/lib/format/persian";
import { replaceProviderDisplayNames } from "@/lib/format/provider-display";

export function FinancialStatementAnalysisCard({
  data,
}: {
  data: FinancialStatementAnalysisResult;
}) {
  return (
    <article
      dir="rtl"
      className="space-y-4 rounded-2xl border border-hairline bg-surface/35 p-4 sm:p-5"
    >
      <header className="space-y-1 border-b border-hairline/70 pb-3">
        <h2 className="text-sm font-semibold text-foreground">
          تحلیل صورت‌های مالی{" "}
          {data.companyName ? `${data.companyName} (${data.companySymbol})` : data.companySymbol}
        </h2>
        <p className="text-xs leading-relaxed text-muted-foreground">
          دوره {toPersianDigits(String(data.selectedPeriodMonths))} ماهه
          {data.jalaliPeriodEnd ? `، منتهی به ${data.jalaliPeriodEnd}` : ""}
          {data.selectedVariant ? `، ${variantLabel(data.selectedVariant)}` : ""}
        </p>
      </header>

      {data.summaryBullets.length > 0 && (
        <details className="group rounded-xl border border-emerald/15 bg-emerald-soft/15 px-3 py-2.5">
          <summary className="cursor-pointer text-xs font-medium text-emerald">
            جمع‌بندی سریع
          </summary>
          <ul className="mt-2 space-y-2 pr-4 text-sm leading-7 text-foreground marker:text-emerald">
            {data.summaryBullets.map((bullet, index) => (
              <li key={`${index}:${bullet}`}>{bullet}</li>
            ))}
          </ul>
        </details>
      )}

      {data.sections
        .filter((section) => section.metrics.length > 0 || section.summaryBullets.length > 0)
        .map((section, sectionIndex) => (
          <section key={`${section.titleFa}:${sectionIndex}`} className="space-y-2.5">
            <h3 className="text-xs font-semibold text-muted-foreground">{section.titleFa}</h3>
            {section.summaryBullets.length > 0 && (
              <ul className="space-y-1.5 rounded-xl bg-background/45 p-3 text-xs leading-6 text-muted-foreground">
                {section.summaryBullets.map((bullet, index) => (
                  <li key={`${index}:${bullet}`}>{bullet}</li>
                ))}
              </ul>
            )}
            {section.metrics.length > 0 && (
              <div className="divide-y divide-hairline/60 rounded-xl border border-hairline/70 bg-background/45 px-3">
                {section.metrics.map((metric, metricIndex) => (
                  <div key={`${metric.metricCode}:${metricIndex}`} className="space-y-1.5 py-3">
                    <div className="text-sm font-medium text-foreground">{metric.labelFa}</div>
                    {metric.isUnavailable ? (
                      <p className="text-xs leading-relaxed text-muted-foreground">
                        {metric.warning ?? "داده‌ای برای این قلم در صورت انتخاب‌شده موجود نیست."}
                      </p>
                    ) : (
                      <div className="flex flex-wrap gap-x-4 gap-y-1.5 text-xs leading-relaxed text-muted-foreground">
                        {metric.currentFormattedValue && (
                          <span>
                            <span className="text-muted-foreground/75">فعلی:</span>{" "}
                            <strong className="font-medium text-foreground">
                              {metric.currentFormattedValue}
                            </strong>
                          </span>
                        )}
                        {metric.previousFormattedValue && (
                          <span>
                            <span className="text-muted-foreground/75">دوره مقایسه:</span>{" "}
                            {metric.previousFormattedValue}
                          </span>
                        )}
                        {metric.changePercent != null && (
                          <span className="font-medium text-foreground">
                            تغییر: <bdi dir="ltr">{formatPercent(metric.changePercent)}</bdi>
                            {metric.indicator ? ` ${metric.indicator}` : ""}
                          </span>
                        )}
                      </div>
                    )}
                  </div>
                ))}
              </div>
            )}
          </section>
        ))}

      {data.warnings.length > 0 && (
        <div
          role="status"
          className="space-y-1 rounded-xl border border-amber-400/25 bg-amber-400/5 p-3 text-xs leading-relaxed text-muted-foreground"
        >
          {data.warnings.map((warning, index) => (
            <p key={`${index}:${warning}`}>{warning}</p>
          ))}
        </div>
      )}

      {data.sourceReferences.length > 0 && (
        <section className="space-y-2 border-t border-hairline/70 pt-3 text-[11px] leading-relaxed text-muted-foreground">
          <h3 className="font-medium text-muted-foreground">منابع و زمان انتشار</h3>
          {data.sourceReferences.map((source, index) => (
            <div
              key={`${source.statementType}:${source.periodMonths}:${index}`}
              className="space-y-0.5 rounded-lg bg-background/40 px-3 py-2"
            >
              <p>
                {statementLabel(source.statementType)}،{" "}
                {toPersianDigits(String(source.periodMonths))} ماهه
                {source.jalaliPeriodEnd ? `، منتهی به ${source.jalaliPeriodEnd}` : ""}،{" "}
                {source.isAudited ? "حسابرسی‌شده" : "حسابرسی‌نشده"}
                {source.isComposing ? "، تلفیقی" : ""}
              </p>
              {source.jalaliAnnouncementDate && <p>زمان انتشار: {source.jalaliAnnouncementDate}</p>}
              <p>ارائه‌دهنده: {replaceProviderDisplayNames(source.providerName)}</p>
            </div>
          ))}
        </section>
      )}
    </article>
  );
}

function statementLabel(type: string) {
  switch (type) {
    case "IncomeStatement":
      return "صورت سود و زیان";
    case "BalanceSheet":
      return "ترازنامه";
    case "CashFlow":
      return "صورت جریان وجوه نقد";
    default:
      return type;
  }
}

function variantLabel(variant: string) {
  if (variant === "Consolidated" || variant === "True") return "تلفیقی";
  if (variant === "NonConsolidated" || variant === "False") return "غیرتلفیقی";
  return variant;
}

function toPersianDigits(value: string) {
  return value.replace(/[0-9]/g, (digit) => "۰۱۲۳۴۵۶۷۸۹"[Number(digit)]);
}
