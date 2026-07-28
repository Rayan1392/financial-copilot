import { useQuery } from "@tanstack/react-query";
import { createFileRoute } from "@tanstack/react-router";
import { FinancialCopilotApiError } from "@/integrations/financial-copilot/api-client";
import {
  getDisclosures,
  type DisclosureScope,
  type DisclosureType,
} from "@/integrations/financial-copilot/disclosures";
import { disclosureTypeLabels, formatDisclosurePeriod, formatDisclosurePeriodType, formatDisclosureReceiptDate } from "@/lib/format/disclosure";
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
    queryFn: () => getDisclosures({
      page: filters.page,
      search: filters.search || undefined,
      types: selectedTypes,
      scope: filters.scope,
      providers: filters.providers.split(",").map((value) => value.trim()).filter(Boolean),
      publishedFrom: filters.publishedFrom || undefined,
      publishedTo: filters.publishedTo || undefined,
      receivedFrom: filters.receivedFrom || undefined,
      receivedTo: filters.receivedTo || undefined,
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

        <section aria-label="فیلتر اطلاعیه‌ها" className="space-y-3 rounded-xl border border-hairline bg-surface/60 p-4">
          <div className="grid gap-2 md:grid-cols-3">
            <input aria-label="جست‌وجوی نماد یا شرکت" value={filters.search} onChange={(event) => update({ search: event.target.value })} placeholder="نماد یا نام شرکت" className="rounded border border-hairline bg-background p-2" />
            <input aria-label="فیلتر ارائه‌دهنده" value={filters.providers} onChange={(event) => update({ providers: event.target.value })} placeholder="ارائه‌دهنده‌ها (با ویرگول)" className="rounded border border-hairline bg-background p-2" />
            <select aria-label="دامنه تلفیق" value={filters.scope} onChange={(event) => update({ scope: event.target.value as DisclosureScope })} className="rounded border border-hairline bg-background p-2">
              {scopes.map(([value, label]) => <option key={value} value={value}>{label}</option>)}
            </select>
          </div>
          <div className="grid gap-2 sm:grid-cols-2 lg:grid-cols-4">
            <DateFilter label="از تاریخ انتشار" value={filters.publishedFrom} onChange={(publishedFrom) => update({ publishedFrom })} />
            <DateFilter label="تا تاریخ انتشار" value={filters.publishedTo} onChange={(publishedTo) => update({ publishedTo })} />
            <DateFilter label="از تاریخ دریافت" value={filters.receivedFrom} onChange={(receivedFrom) => update({ receivedFrom })} />
            <DateFilter label="تا تاریخ دریافت" value={filters.receivedTo} onChange={(receivedTo) => update({ receivedTo })} />
          </div>
          <fieldset className="flex flex-wrap gap-2"><legend className="sr-only">نوع اطلاعیه</legend>{disclosureTypes.map(([value, label]) => <label key={value} className="rounded border border-hairline px-2 py-1 text-xs"><input type="checkbox" checked={selectedTypes.includes(value)} onChange={() => toggleType(value)} /> {label}</label>)}</fieldset>
        </section>

        {query.isLoading && !query.data && <LoadingTable />}
        {query.isError && <p role="alert" className="rounded border border-rose/40 bg-rose/10 p-4 text-rose">{errorMessage}</p>}
        {query.data && <section className="space-y-3" aria-busy={query.isFetching}>
          {query.isFetching && <p className="text-xs text-muted-foreground" role="status">در حال به‌روزرسانی فهرست…</p>}
          {hasStaleData && <Notice>بخشی از داده‌ها ممکن است به‌روز نباشند؛ زمان تازگی منبع در نتیجه لحاظ شده است.</Notice>}
          {query.data.coverageStatus !== "Complete" && <Notice>بخشی از اطلاعیه‌ها هنوز به نماد یا شرکت نگاشت نشده‌اند؛ پوشش فهرست کامل نیست.</Notice>}
          <div className="overflow-x-auto rounded-xl border border-hairline">
            <table className="w-full min-w-[900px] text-right text-sm">
              <thead className="bg-background"><tr><Header>نماد</Header><Header>شرکت</Header><Header>عنوان اطلاعیه</Header><Header>نوع</Header><Header>دوره</Header><Header>نوع دوره</Header><Header>دریافت سیستم</Header></tr></thead>
              <tbody>{query.data.items.map((item) => <tr key={item.disclosureId} className="border-t border-hairline"><Cell><bdi>{item.symbol ?? "—"}</bdi></Cell><Cell>{item.companyName ?? "—"}</Cell><Cell>{item.title}</Cell><Cell>{disclosureTypeLabels[item.type]}</Cell><Cell>{formatDisclosurePeriod(item)}</Cell><Cell>{formatDisclosurePeriodType(item)}</Cell><Cell>{formatDisclosureReceiptDate(item.receivedAt)}</Cell></tr>)}</tbody>
            </table>
          </div>
          {query.data.items.length === 0 && <p className="rounded border border-hairline p-4">اطلاعیه‌ای با این فیلترها یافت نشد.</p>}
          <nav aria-label="صفحه‌بندی اطلاعیه‌ها" className="flex items-center justify-between gap-3">
            <button type="button" disabled={!query.data.hasPreviousPage} onClick={() => update({ page: filters.page - 1 }, false)} className="rounded border border-hairline px-3 py-2 disabled:opacity-40">صفحه قبل</button>
            <span aria-live="polite">صفحه {query.data.page} از {query.data.totalPages || 1}</span>
            <button type="button" disabled={!query.data.hasNextPage} onClick={() => update({ page: filters.page + 1 }, false)} className="rounded border border-hairline px-3 py-2 disabled:opacity-40">صفحه بعد</button>
          </nav>
        </section>}
      </div>
    </main>
  );
}

function DateFilter({ label, value, onChange }: { label: string; value: string; onChange: (value: string) => void }) {
  return <label className="text-xs text-muted-foreground">{label}<input aria-label={label} type="date" value={value} onChange={(event) => onChange(event.target.value)} className="mt-1 block w-full rounded border border-hairline bg-background p-2 text-foreground" /></label>;
}
function Header({ children }: { children: React.ReactNode }) { return <th className="p-3 font-medium">{children}</th>; }
function Cell({ children }: { children: React.ReactNode }) { return <td className="p-3 align-top">{children}</td>; }
function Notice({ children }: { children: React.ReactNode }) { return <p role="status" className="rounded border border-amber-400/30 bg-amber-400/10 p-3 text-sm">{children}</p>; }
function LoadingTable() { return <div role="status" className="h-64 animate-pulse rounded-xl border border-hairline bg-surface/60 p-4 text-muted-foreground">در حال بارگذاری اطلاعیه‌ها…</div>; }
function isDisclosureType(value: string): value is DisclosureType { return disclosureTypes.some(([type]) => type === value); }
function localizedError(error: Error | null) {
  if (error instanceof FinancialCopilotApiError) {
    if (error.status === 400) return "فیلترهای واردشده معتبر نیستند. تاریخ‌ها و مقادیر فیلتر را بررسی کنید.";
    if (error.status === 401 || error.status === 403) return "شما اجازهٔ مشاهدهٔ اطلاعیه‌ها را ندارید.";
  }
  return "دریافت اطلاعیه‌ها ناموفق بود. لطفاً دوباره تلاش کنید.";
}
