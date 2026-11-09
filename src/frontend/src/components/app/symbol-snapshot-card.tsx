import type { ReactNode } from "react";
import { ArrowDown, ArrowUp } from "lucide-react";
import type { SymbolSnapshotResult } from "@/lib/chat.functions";
import { FollowSymbolButton } from "@/components/app/follow-symbol-button";
import { formatNumber, formatPercent, toPersianDigits } from "@/lib/format/persian";
import { cn } from "@/lib/utils";

const YEAR_OVER_YEAR_LABEL = "نسبت به فصل مشابه سال قبل";

function Change({
  label,
  value,
  labelHidden = false,
}: {
  label: string;
  value?: number | null;
  labelHidden?: boolean;
}) {
  if (value === undefined || value === null) return null;
  const Icon = value > 0 ? ArrowUp : value < 0 ? ArrowDown : null;
  return (
    <span className="inline-flex items-center gap-1 text-xs text-muted-foreground">
      <span className={labelHidden ? "sr-only" : undefined}>{label}</span>
      <span
        className={cn(
          "inline-flex items-center gap-0.5 font-medium tabular-nums",
          value > 0 && "text-emerald-600 dark:text-emerald-400",
          value < 0 && "text-rose-600 dark:text-rose-400",
        )}
      >
        {Icon && <Icon aria-hidden className="size-3" />}
        <bdi dir="ltr">{formatPercent(value)}</bdi>
      </span>
    </span>
  );
}

function Section({
  title,
  period,
  notice,
  children,
}: {
  title: string;
  period?: string | null;
  notice?: string | null;
  children?: ReactNode;
}) {
  return (
    <div className="space-y-1.5 p-3 sm:p-4">
      <h3 className="flex flex-wrap items-baseline gap-x-2 text-xs font-medium text-muted-foreground">
        <span>{title}</span>
        {period && <span className="font-normal">{toPersianDigits(period)}</span>}
      </h3>
      {notice ? <p className="text-xs leading-6 text-muted-foreground">{notice}</p> : children}
    </div>
  );
}

function auditLabel(isAudited?: boolean | null): string | null {
  if (isAudited === undefined || isAudited === null) return null;
  return isAudited ? "حسابرسی‌شده" : "حسابرسی‌نشده";
}

/** The default answer to a message that is only a ticker: latest price, monthly sales and quarterly report. */
export function SymbolSnapshotCard({
  data,
  followed = false,
}: {
  data: SymbolSnapshotResult;
  followed?: boolean;
}) {
  const { price, monthlySales: monthly, quarterlyReport: quarterly } = data;
  const details = [data.marketName, data.groupName].filter(Boolean).join(" · ");
  const audit = auditLabel(quarterly.isAudited);
  const quarterPeriod =
    quarterly.status === "Available"
      ? [quarterly.periodLabelFa, audit && `(${audit})`].filter(Boolean).join(" ")
      : null;

  return (
    <div className="space-y-3" dir="rtl">
      <p className="text-sm leading-7">{data.introFa}</p>
      <section
        aria-label={`خلاصهٔ نماد ${data.symbol}`}
        data-testid="symbol-snapshot-card"
        className="min-w-0 divide-y divide-hairline overflow-hidden rounded-xl bg-surface/60 ring-1 ring-hairline"
      >
        <header className="flex items-start justify-between gap-3 p-3 sm:p-4">
          <div className="min-w-0">
            <h2 className="text-sm font-semibold leading-6 break-words">
              {data.symbol}
              <span className="font-normal text-muted-foreground"> · {data.companyName}</span>
            </h2>
            {details && <p className="text-[11px] leading-5 text-muted-foreground">{details}</p>}
          </div>
          <FollowSymbolButton symbol={data.symbol} compact followed={followed} />
        </header>

        <Section title="آخرین قیمت" notice={price.status === "Available" ? null : price.noticeFa}>
          <div className="flex flex-wrap items-center gap-x-3 gap-y-1">
            <span className="text-lg font-semibold tabular-nums">
              {formatNumber(price.latestPrice ?? 0)}{" "}
              <span className="text-[11px] font-normal text-muted-foreground">ریال</span>
            </span>
            <Change label="تغییر روزانه" value={price.dailyChangePercent} />
          </div>
          {price.tradingDateJalali && (
            <p className="text-[11px] leading-5 text-muted-foreground">
              تاریخ معامله: <bdi>{toPersianDigits(price.tradingDateJalali)}</bdi>
            </p>
          )}
        </Section>

        <Section
          title="فروش ماهانه"
          period={monthly.status === "Available" ? monthly.periodLabelFa : null}
          notice={monthly.status === "Available" ? null : monthly.noticeFa}
        >
          <span className="text-lg font-semibold tabular-nums">
            {formatNumber(monthly.salesAmount ?? 0)}{" "}
            <span className="text-[11px] font-normal text-muted-foreground">
              {monthly.unitLabelFa}
            </span>
          </span>
          <div className="flex flex-wrap gap-x-4 gap-y-1">
            <Change label="نسبت به ماه قبل" value={monthly.monthOverMonthPercent} />
            <Change label="نسبت به ماه مشابه سال قبل" value={monthly.yearOverYearPercent} />
          </div>
        </Section>

        <Section
          title="گزارش فصلی"
          period={quarterPeriod}
          notice={quarterly.status === "Available" ? null : quarterly.noticeFa}
        >
          <dl className="space-y-2 sm:grid sm:grid-cols-[minmax(0,1fr)_auto_auto] sm:gap-x-5 sm:gap-y-2 sm:space-y-0">
            {(quarterly.rows ?? []).map((row) => (
              <div
                key={row.labelFa}
                className="grid gap-0.5 sm:col-span-3 sm:grid-cols-subgrid sm:items-baseline"
              >
                <dt className="min-w-0 text-xs text-muted-foreground">{row.labelFa}</dt>
                <dd className="text-sm font-medium tabular-nums">
                  {row.value === undefined || row.value === null ? (
                    "—"
                  ) : (
                    <>
                      <bdi dir="ltr">{formatNumber(row.value)}</bdi>{" "}
                      <span className="text-[11px] font-normal text-muted-foreground">
                        {row.unitLabelFa}
                      </span>
                    </>
                  )}
                </dd>
                <dd>
                  <Change
                    label={YEAR_OVER_YEAR_LABEL}
                    value={row.yearOverYearPercent}
                    labelHidden
                  />
                </dd>
              </div>
            ))}
          </dl>
          {(quarterly.rows ?? []).some((row) => typeof row.yearOverYearPercent === "number") && (
            <p className="text-[11px] leading-5 text-muted-foreground">
              درصدها {YEAR_OVER_YEAR_LABEL} است.
            </p>
          )}
        </Section>
      </section>
    </div>
  );
}
