import { useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { createFileRoute } from "@tanstack/react-router";
import { CalendarDays } from "lucide-react";
import { faIRJalali } from "react-day-picker/locale/fa-IR-jalali";
import { getDateLib } from "react-day-picker/persian";
import { Calendar } from "@/components/ui/calendar";
import { Popover, PopoverContent, PopoverTrigger } from "@/components/ui/popover";
import { FinancialCopilotApiError } from "@/integrations/financial-copilot/api-client";
import {
  getDisclosures,
  type DisclosureScope,
  type DisclosureType,
} from "@/integrations/financial-copilot/disclosures";
import {
  disclosureTypeLabels,
  formatDisclosureDateOnly,
  formatDisclosurePagination,
  formatDisclosurePeriod,
  formatDisclosurePeriodType,
  formatDisclosureReceiptDate,
  parseDisclosureDateOnly,
  serializeDisclosureDateOnly,
} from "@/lib/format/disclosure";
import { normalizeDisclosureSearch, patchDisclosureSearch } from "@/lib/disclosure-search";

const disclosureTypes: Array<[DisclosureType, string]> = [
  ["MonthlyProductionSales", "تولید و فروش ماهانه"],
  ["IncomeStatement", "صورت سود و زیان"],
  ["BalanceSheet", "ترازنامه"],
  ["CashFlowStatement", "جریان وجه نقد"],
];

const scopes: Array<[DisclosureScope, string]> = [
  ["NonConsolidated", "غیرتلفیقی"],
  ["Consolidated", "تلفیقی"],
  ["Both", "هر دو"],
];

const jalaliDateLib = getDateLib({ locale: faIRJalali, numerals: "arabext" });

export const Route = createFileRoute("/_app/disclosures")({
  validateSearch: normalizeDisclosureSearch,
  component: DisclosuresPage,
});

function DisclosuresPage() {
  const filters = Route.useSearch();
  const navigate = Route.useNavigate();
  const selectedTypes = filters.types
    ? (filters.types.split(",").filter(isDisclosureType) as DisclosureType[])
    : [];
  const query = useQuery({
    queryKey: ["disclosures", filters],
    queryFn: () =>
      getDisclosures({
        page: filters.page,
        search: filters.search || undefined,
        types: selectedTypes,
        scope: filters.scope,
        publishedFrom: filters.publishedFrom || undefined,
        publishedTo: filters.publishedTo || undefined,
      }),
    retry: false,
    placeholderData: (previous) => previous,
  });

  const update = (patch: Partial<typeof filters>, resetPage = true) =>
    navigate({
      search: patchDisclosureSearch(filters, patch, resetPage),
    });
  const toggleType = (type: DisclosureType) =>
    update({
      types: selectedTypes.includes(type)
        ? selectedTypes.filter((item) => item !== type).join(",")
        : [...selectedTypes, type].join(","),
    });
  const errorMessage = localizedError(query.error);
  const hasStaleData = query.data?.freshnessReasonCode.toLowerCase().includes("stale");

  return (
    <main dir="rtl" className="flex-1 overflow-y-auto p-4 md:p-8">
      <div className="mx-auto max-w-7xl space-y-5">
        <header>
          <p className="text-xs font-semibold text-emerald">اطلاعات شرکت‌ها</p>
          <h1 className="text-2xl font-bold">فهرست اطلاعیه‌های منتشرشده</h1>
          <p className="text-sm text-muted-foreground">
            داده‌ها از گزارش‌های نرمال‌شدهٔ سیستم نمایش داده می‌شوند و توصیهٔ سرمایه‌گذاری نیستند.
          </p>
        </header>

        <section
          aria-label="فیلتر اطلاعیه‌ها"
          className="space-y-4 rounded-2xl border border-hairline bg-surface/60 p-4 shadow-sm md:p-5"
        >
          <div className="flex items-center justify-between gap-3">
            <h2 className="text-sm font-semibold text-foreground">فیلترها</h2>
            <button
              type="button"
              onClick={() =>
                update({
                  search: "",
                  types: "",
                  scope: "NonConsolidated",
                  publishedFrom: "",
                  publishedTo: "",
                })
              }
              className="rounded-lg px-2.5 py-1.5 text-xs text-muted-foreground transition hover:bg-background hover:text-foreground"
            >
              پاک‌کردن فیلترها
            </button>
          </div>
          <div className="grid gap-3 md:grid-cols-[minmax(0,1.5fr)_minmax(13rem,1fr)]">
            <label className="space-y-1.5 text-xs font-medium text-muted-foreground">
              <span>نماد یا نام شرکت</span>
              <input
                aria-label="جست‌وجوی نماد یا شرکت"
                value={filters.search}
                onChange={(event) => update({ search: event.target.value })}
                placeholder="مثلاً فملی یا فولاد مبارکه"
                className="w-full rounded-xl border border-hairline bg-background px-3 py-2.5 text-sm text-foreground outline-none transition placeholder:text-muted-foreground/70 focus:border-emerald/50 focus:ring-2 focus:ring-emerald/10"
              />
            </label>
            <label className="space-y-1.5 text-xs font-medium text-muted-foreground">
              <span>دامنه گزارش‌های مالی</span>
              <select
                aria-label="دامنه تلفیق"
                value={filters.scope}
                onChange={(event) => update({ scope: event.target.value as DisclosureScope })}
                className="w-full rounded-xl border border-hairline bg-background px-3 py-2.5 text-sm text-foreground outline-none transition focus:border-emerald/50 focus:ring-2 focus:ring-emerald/10"
              >
                {scopes.map(([value, label]) => (
                  <option key={value} value={value}>
                    {label}
                  </option>
                ))}
              </select>
            </label>
          </div>
          <div className="grid gap-3 border-t border-hairline/70 pt-4 md:grid-cols-[minmax(0,1.5fr)_minmax(13rem,1fr)] md:items-start">
            <fieldset className="min-w-0">
              <legend className="mb-2 text-xs font-medium text-muted-foreground">
                نوع اطلاعیه
              </legend>
              <div className="flex flex-wrap gap-2">
                {disclosureTypes.map(([value, label]) => {
                  const checked = selectedTypes.includes(value);
                  return (
                    <label
                      key={value}
                      className={`inline-flex cursor-pointer items-center gap-2 rounded-full border px-3 py-2 text-xs transition ${checked ? "border-emerald/35 bg-emerald-soft/60 text-emerald" : "border-hairline bg-background/60 text-muted-foreground hover:border-emerald/25 hover:text-foreground"}`}
                    >
                      <input
                        className="size-3.5 accent-emerald"
                        type="checkbox"
                        checked={checked}
                        onChange={() => toggleType(value)}
                      />
                      {label}
                    </label>
                  );
                })}
              </div>
            </fieldset>
            <fieldset>
              <legend className="mb-2 text-xs font-medium text-muted-foreground">
                تاریخ انتشار
              </legend>
              <div className="grid grid-cols-2 gap-2">
                <DateFilter
                  label="از"
                  ariaLabel="از تاریخ انتشار"
                  value={filters.publishedFrom}
                  onChange={(publishedFrom) => update({ publishedFrom })}
                />
                <DateFilter
                  label="تا"
                  ariaLabel="تا تاریخ انتشار"
                  value={filters.publishedTo}
                  onChange={(publishedTo) => update({ publishedTo })}
                />
              </div>
            </fieldset>
          </div>
        </section>

        {query.isLoading && !query.data && <LoadingTable />}
        {query.isError && (
          <p role="alert" className="rounded border border-rose/40 bg-rose/10 p-4 text-rose">
            {errorMessage}
          </p>
        )}
        {query.data && (
          <section className="space-y-3" aria-busy={query.isFetching}>
            {query.isFetching && (
              <p className="text-xs text-muted-foreground" role="status">
                در حال به‌روزرسانی فهرست…
              </p>
            )}
            {hasStaleData && (
              <Notice>
                بخشی از داده‌ها ممکن است به‌روز نباشند؛ زمان تازگی منبع در نتیجه لحاظ شده است.
              </Notice>
            )}
            {/* The seven-column table needs ~56rem. Its own width (not the viewport) decides the layout, because
              both side panels can be open: below that it renders as cards instead of clipping columns. */}
            <div className="@container">
              <div className="hidden overflow-x-auto rounded-xl border border-hairline @4xl:block">
                <table className="w-full text-right text-sm">
                  <thead className="bg-background">
                    <tr>
                      <Header>نماد</Header>
                      <Header>شرکت</Header>
                      <Header>عنوان اطلاعیه</Header>
                      <Header>نوع</Header>
                      <Header>دوره</Header>
                      <Header>نوع دوره</Header>
                      <Header>دریافت سیستم</Header>
                    </tr>
                  </thead>
                  <tbody>
                    {query.data.items.map((item) => (
                      <tr key={item.disclosureId} className="border-t border-hairline">
                        <Cell>
                          <bdi>{item.symbol ?? "—"}</bdi>
                        </Cell>
                        <Cell>{item.companyName ?? "—"}</Cell>
                        <Cell>{item.title}</Cell>
                        <Cell>{disclosureTypeLabels[item.type]}</Cell>
                        <Cell>{formatDisclosurePeriod(item)}</Cell>
                        <Cell>{formatDisclosurePeriodType(item)}</Cell>
                        <Cell>{formatDisclosureReceiptDate(item.receivedAt)}</Cell>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <ul aria-label="فهرست اطلاعیه‌ها" className="space-y-2 @4xl:hidden">
                {query.data.items.map((item) => (
                  <li
                    key={item.disclosureId}
                    className="rounded-xl border border-hairline bg-surface/60 p-4"
                  >
                    <div className="flex items-start justify-between gap-3">
                      <div className="min-w-0">
                        <p className="text-sm font-bold text-foreground">
                          <bdi>{item.symbol ?? "—"}</bdi>
                        </p>
                        <p className="mt-0.5 truncate text-xs text-muted-foreground">
                          {item.companyName ?? "—"}
                        </p>
                      </div>
                      <span className="shrink-0 rounded-full bg-emerald-soft/60 px-2.5 py-1 text-[11px] font-medium text-emerald">
                        {disclosureTypeLabels[item.type]}
                      </span>
                    </div>
                    <p className="mt-3 text-sm leading-7 text-foreground">{item.title}</p>
                    <dl className="mt-3 grid grid-cols-1 gap-x-4 gap-y-1.5 border-t border-hairline pt-3 text-xs sm:grid-cols-3">
                      <div className="flex gap-1.5">
                        <dt className="text-muted-foreground">دوره:</dt>
                        <dd className="text-foreground">{formatDisclosurePeriod(item)}</dd>
                      </div>
                      <div className="flex gap-1.5">
                        <dt className="text-muted-foreground">نوع دوره:</dt>
                        <dd className="text-foreground">{formatDisclosurePeriodType(item)}</dd>
                      </div>
                      <div className="flex gap-1.5">
                        <dt className="text-muted-foreground">دریافت سیستم:</dt>
                        <dd className="text-foreground">
                          {formatDisclosureReceiptDate(item.receivedAt)}
                        </dd>
                      </div>
                    </dl>
                  </li>
                ))}
              </ul>
            </div>
            {query.data.items.length === 0 && (
              <p className="rounded border border-hairline p-4">
                اطلاعیه‌ای با این فیلترها یافت نشد.
              </p>
            )}
            <nav
              aria-label="صفحه‌بندی اطلاعیه‌ها"
              className="flex items-center justify-between gap-3"
            >
              <button
                type="button"
                disabled={!query.data.hasPreviousPage}
                onClick={() => update({ page: filters.page - 1 }, false)}
                className="rounded border border-hairline px-3 py-2 disabled:opacity-40"
              >
                صفحه قبل
              </button>
              <span aria-live="polite">
                {formatDisclosurePagination(query.data.page, query.data.totalPages)}
              </span>
              <button
                type="button"
                disabled={!query.data.hasNextPage}
                onClick={() => update({ page: filters.page + 1 }, false)}
                className="rounded border border-hairline px-3 py-2 disabled:opacity-40"
              >
                صفحه بعد
              </button>
            </nav>
          </section>
        )}
      </div>
    </main>
  );
}

function DateFilter({
  label,
  ariaLabel,
  value,
  onChange,
}: {
  label: string;
  ariaLabel: string;
  value: string;
  onChange: (value: string) => void;
}) {
  const [open, setOpen] = useState(false);
  const selected = parseDisclosureDateOnly(value);
  return (
    <div className="space-y-1 text-xs text-muted-foreground">
      <span className="block">{label}</span>
      <Popover open={open} onOpenChange={setOpen}>
        <PopoverTrigger asChild>
          <button
            type="button"
            aria-label={ariaLabel}
            aria-haspopup="dialog"
            className="flex w-full items-center justify-between gap-2 rounded-xl border border-hairline bg-background px-3 py-2.5 text-sm text-foreground outline-none transition hover:border-emerald/35 focus-visible:border-emerald/50 focus-visible:ring-2 focus-visible:ring-emerald/10"
          >
            <span className={selected ? "" : "text-muted-foreground"}>
              {selected ? formatDisclosureDateOnly(value) : "انتخاب تاریخ"}
            </span>
            <CalendarDays className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
          </button>
        </PopoverTrigger>
        <PopoverContent align="start" dir="rtl" className="w-auto p-2">
          <Calendar
            mode="single"
            selected={selected}
            onSelect={(date) => {
              onChange(date ? serializeDisclosureDateOnly(date) : "");
              setOpen(false);
            }}
            locale={faIRJalali}
            dateLib={jalaliDateLib}
            numerals="arabext"
            dir="rtl"
            initialFocus
          />
          {selected && (
            <button
              type="button"
              onClick={() => {
                onChange("");
                setOpen(false);
              }}
              className="w-full rounded-md px-2 py-1.5 text-xs text-muted-foreground transition hover:bg-accent hover:text-foreground"
            >
              حذف تاریخ
            </button>
          )}
        </PopoverContent>
      </Popover>
    </div>
  );
}
function Header({ children }: { children: React.ReactNode }) {
  return <th className="p-3 font-medium">{children}</th>;
}
function Cell({ children }: { children: React.ReactNode }) {
  return <td className="p-3 align-top">{children}</td>;
}
function Notice({ children }: { children: React.ReactNode }) {
  return (
    <p role="status" className="rounded border border-amber-400/30 bg-amber-400/10 p-3 text-sm">
      {children}
    </p>
  );
}
function LoadingTable() {
  return (
    <div
      role="status"
      className="h-64 animate-pulse rounded-xl border border-hairline bg-surface/60 p-4 text-muted-foreground"
    >
      در حال بارگذاری اطلاعیه‌ها…
    </div>
  );
}
function isDisclosureType(value: string): value is DisclosureType {
  return disclosureTypes.some(([type]) => type === value);
}
function localizedError(error: Error | null) {
  if (error instanceof FinancialCopilotApiError) {
    if (error.status === 400)
      return "فیلترهای واردشده معتبر نیستند. تاریخ‌ها و مقادیر فیلتر را بررسی کنید.";
    if (error.status === 403)
      return "\u062f\u0633\u062a\u0631\u0633\u06cc \u0628\u0647 \u0627\u06cc\u0646 \u0635\u0641\u062d\u0647 \u062f\u0631 \u0637\u0631\u062d \u0641\u0639\u0644\u06cc \u0634\u0645\u0627 \u0641\u0639\u0627\u0644 \u0646\u06cc\u0633\u062a.";
    if (error.status === 401)
      return "\u0644\u0637\u0641\u0627\u064b \u0628\u0631\u0627\u06cc \u0645\u0634\u0627\u0647\u062f\u0647 \u0627\u0637\u0644\u0627\u0639\u06cc\u0647\u200c\u0647\u0627 \u0648\u0627\u0631\u062f \u0634\u0648\u06cc\u062f.";
  }
  return "دریافت اطلاعیه‌ها ناموفق بود. لطفاً دوباره تلاش کنید.";
}
