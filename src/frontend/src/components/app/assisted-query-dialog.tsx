import { useMemo, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useServerFn } from "@tanstack/react-start";
import { Filter } from "lucide-react";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog";
import {
  getMetricMetadata,
  getPeriodMetadata,
  searchIndustryMetadata,
  searchSymbolMetadata,
} from "@/lib/metadata.functions";

interface Props {
  onCompose: (prompt: string) => void;
}

const RESULT_LIMIT = 20;

export function AssistedQueryDialog({ onCompose }: Props) {
  const [open, setOpen] = useState(false);
  const [metricCode, setMetricCode] = useState("");
  const [periodCode, setPeriodCode] = useState("");
  const [operator, setOperator] = useState("<");
  const [threshold, setThreshold] = useState("");
  const [symbolSearch, setSymbolSearch] = useState("");
  const [industrySearch, setIndustrySearch] = useState("");
  const [symbol, setSymbol] = useState("");
  const [industry, setIndustry] = useState("");
  const getMetrics = useServerFn(getMetricMetadata);
  const getPeriods = useServerFn(getPeriodMetadata);
  const searchSymbols = useServerFn(searchSymbolMetadata);
  const searchIndustries = useServerFn(searchIndustryMetadata);
  const queryOptions = { enabled: open, retry: false, refetchOnWindowFocus: false } as const;
  const metricsQuery = useQuery({
    queryKey: ["metadata", "metrics"],
    queryFn: () => getMetrics(),
    ...queryOptions,
  });
  const periodsQuery = useQuery({
    queryKey: ["metadata", "periods"],
    queryFn: () => getPeriods(),
    ...queryOptions,
  });
  const symbolsQuery = useQuery({
    queryKey: ["metadata", "symbols", symbolSearch],
    queryFn: () => searchSymbols({ data: { search: symbolSearch, limit: RESULT_LIMIT } }),
    ...queryOptions,
  });
  const industriesQuery = useQuery({
    queryKey: ["metadata", "industries", industrySearch],
    queryFn: () => searchIndustries({ data: { search: industrySearch, limit: RESULT_LIMIT } }),
    ...queryOptions,
  });
  const selectedMetric = useMemo(
    () => metricsQuery.data?.find((metric) => metric.metricCode === metricCode),
    [metricCode, metricsQuery.data],
  );
  const supportedPeriods = periodsQuery.data?.filter(
    (period) => !selectedMetric || selectedMetric.supportedPeriods.includes(period.code),
  );

  function compose() {
    if (!selectedMetric || !threshold) return;
    const metricLabel =
      selectedMetric.aliases.find((alias) => alias.language === "fa-IR")?.expression ??
      selectedMetric.displayName;
    const period = supportedPeriods?.find((item) => item.code === periodCode);
    const scope = [symbol && `نماد ${symbol}`, industry && `در صنعت ${industry}`]
      .filter(Boolean)
      .join(" ");
    onCompose(
      `نمادهای ${scope ? `${scope} ` : ""}با ${metricLabel} ${operator} ${threshold}${period ? ` در دوره ${period.displayNameFa}` : ""} را پیدا کن.`,
    );
    setOpen(false);
  }

  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <button
          type="button"
          className="flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg text-[11px] font-medium ring-1 ring-hairline bg-background text-muted-foreground hover:text-foreground transition"
        >
          <Filter className="size-3" />
          فیلترنویسی
        </button>
      </DialogTrigger>
      <DialogContent className="max-w-2xl">
        <DialogHeader>
          <DialogTitle>ساخت فیلتر بورسی</DialogTitle>
          <DialogDescription>
            فیلتر را بسازید، سپس متن قابل مشاهده آن را پیش از ارسال بررسی کنید.
          </DialogDescription>
        </DialogHeader>
        <div className="grid gap-4 md:grid-cols-2">
          <Field label="معیار">
            <select
              value={metricCode}
              onChange={(event) => {
                setMetricCode(event.target.value);
                setPeriodCode("");
              }}
              className="input"
            >
              <option value="">انتخاب معیار</option>
              {metricsQuery.data?.map((metric) => (
                <option key={metric.metricCode} value={metric.metricCode}>
                  {metric.displayName}
                </option>
              ))}
            </select>
            <QueryState query={metricsQuery} emptyText="معیاری در دسترس نیست." />
          </Field>
          <Field label="دوره">
            <select
              value={periodCode}
              onChange={(event) => setPeriodCode(event.target.value)}
              className="input"
            >
              <option value="">بدون دوره مشخص</option>
              {supportedPeriods?.map((period) => (
                <option key={period.code} value={period.code}>
                  {period.displayNameFa} ({period.displayName})
                </option>
              ))}
            </select>
            <QueryState query={periodsQuery} emptyText="دوره‌ای در دسترس نیست." />
          </Field>
          <Field label="شرط">
            <div className="flex gap-2">
              <select
                value={operator}
                onChange={(event) => setOperator(event.target.value)}
                className="input w-20"
              >
                <option value="<">&lt;</option>
                <option value=">">&gt;</option>
                <option value="<=">≤</option>
                <option value=">=">≥</option>
              </select>
              <input
                value={threshold}
                onChange={(event) => setThreshold(event.target.value)}
                placeholder="مقدار"
                className="input flex-1"
              />
            </div>
          </Field>
          <Field label="نماد اختیاری">
            <input
              value={symbolSearch}
              onChange={(event) => setSymbolSearch(event.target.value)}
              placeholder="جستجوی نماد یا شرکت"
              className="input"
              maxLength={100}
            />
            <select
              value={symbol}
              onChange={(event) => setSymbol(event.target.value)}
              className="input mt-2"
            >
              <option value="">همه نمادها</option>
              {symbolsQuery.data?.map((item) => (
                <option key={item.symbolCode} value={item.symbolCode}>
                  {item.symbolCode} - {item.companyName}
                </option>
              ))}
            </select>
            <SearchState query={symbolsQuery} />
          </Field>
          <Field label="صنعت اختیاری">
            <input
              value={industrySearch}
              onChange={(event) => setIndustrySearch(event.target.value)}
              placeholder="جستجوی صنعت"
              className="input"
              maxLength={100}
            />
            <select
              value={industry}
              onChange={(event) => setIndustry(event.target.value)}
              className="input mt-2"
            >
              <option value="">همه صنایع</option>
              {industriesQuery.data?.map((item) => (
                <option key={item.industryId} value={item.displayName}>
                  {item.displayName}
                </option>
              ))}
            </select>
            <SearchState query={industriesQuery} />
          </Field>
        </div>
        <button
          type="button"
          onClick={compose}
          disabled={!selectedMetric || !threshold}
          className="rounded-lg bg-emerald px-4 py-2 text-sm text-primary-foreground disabled:opacity-40"
        >
          افزودن به پیام
        </button>
      </DialogContent>
    </Dialog>
  );
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <label className="space-y-1 text-xs text-muted-foreground">
      <span>{label}</span>
      {children}
    </label>
  );
}

function QueryState({
  query,
  emptyText,
}: {
  query: { isLoading: boolean; isError: boolean; data?: unknown[] };
  emptyText: string;
}) {
  if (query.isLoading) return <Hint>در حال بارگذاری...</Hint>;
  if (query.isError) return <Hint>دریافت اطلاعات ناموفق بود.</Hint>;
  if (query.data?.length === 0) return <Hint>{emptyText}</Hint>;
  return null;
}

function SearchState({
  query,
}: {
  query: { isLoading: boolean; isError: boolean; data?: unknown[] };
}) {
  if (query.isLoading) return <Hint>در حال جستجو...</Hint>;
  if (query.isError) return <Hint>جستجو ناموفق بود.</Hint>;
  if (query.data?.length === 0) return <Hint>نتیجه‌ای پیدا نشد.</Hint>;
  if (query.data?.length === RESULT_LIMIT)
    return <Hint>فقط {RESULT_LIMIT} نتیجه اول نمایش داده می‌شود.</Hint>;
  return null;
}

function Hint({ children }: { children: React.ReactNode }) {
  return <span className="block text-[10px] text-muted-foreground">{children}</span>;
}
