import { Fragment, useCallback, useEffect, useMemo, useState } from "react";
import {
  Activity,
  ArrowLeftRight,
  ChevronLeft,
  CreditCard,
  Download,
  Filter,
  LoaderCircle,
  RefreshCw,
  TrendingDown,
  TrendingUp,
  UserPlus,
  Users,
  X,
} from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  adminApi,
  type AdminUserAnalyticsCube,
} from "@/integrations/financial-copilot/admin-client";
import {
  buildDrillTree,
  buildPivot,
  buildTrend,
  countBy,
  dimensionLabels,
  dimensionValues,
  drillInto,
  filterCells,
  isPaidPlan,
  periodOptions,
  periodRange,
  pivotDisplayValue,
  pivotToCsv,
  previousRange,
  reportingDay,
  share,
  sumUsers,
  toggleSelection,
  type DateRange,
  type DimensionKey,
  type DimensionValue,
  type DrillNode,
  type PeriodKey,
  type PivotMode,
  type Selections,
} from "@/components/admin/user-analytics-model";

type Cells = AdminUserAnalyticsCube["cells"];
type ValuesFor = (dimension: DimensionKey) => DimensionValue[];
type Path = Partial<Record<DimensionKey, string>>;

const ALL_DIMENSIONS: DimensionKey[] = ["acquisitionSource", "plan", "status", "activity"];
const faNumber = new Intl.NumberFormat("fa-IR");
const faPercent = new Intl.NumberFormat("fa-IR", { maximumFractionDigits: 1 });
const number = (value: number) => faNumber.format(value);
const percent = (value: number) => `${faPercent.format(value)}٪`;
const selectClass =
  "h-9 rounded-lg border border-slate-200 bg-white px-2 text-sm text-slate-700 focus:outline-none focus-visible:ring-2 focus-visible:ring-emerald-500/40";
const ghostButtonClass = "text-slate-600 hover:bg-slate-100 hover:text-slate-900";
const outlineButtonClass =
  "border-slate-200 bg-white text-slate-700 hover:bg-slate-50 hover:text-slate-900";

export function UserAnalyticsPanel() {
  const [cube, setCube] = useState<AdminUserAnalyticsCube | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [period, setPeriod] = useState<PeriodKey>("all");
  const [selections, setSelections] = useState<Selections>({});

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setCube(await adminApi.userAnalytics());
    } catch (reason) {
      setError(reason instanceof Error ? reason.message : "دریافت آمار کاربران ناموفق بود.");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const dimensions = useMemo(
    () =>
      ALL_DIMENSIONS.filter(
        (dimension) => dimension !== "plan" || cube?.subscriptionBreakdownAvailable,
      ),
    [cube?.subscriptionBreakdownAvailable],
  );
  const valuesFor = useMemo<ValuesFor>(() => {
    const cells = cube?.cells ?? [];
    const planNames = Object.fromEntries((cube?.plans ?? []).map((plan) => [plan.code, plan.name]));
    const cache = new Map<DimensionKey, DimensionValue[]>();
    return (dimension) => {
      if (!cache.has(dimension)) cache.set(dimension, dimensionValues(dimension, cells, planNames));
      return cache.get(dimension)!;
    };
  }, [cube]);

  const today = cube ? reportingDay(cube.asOfUtc, cube.reportingTimeZone) : "";
  const range = useMemo(() => (today ? periodRange(period, today) : null), [period, today]);
  const filtered = useMemo(
    () => (cube ? filterCells(cube.cells, selections, range) : []),
    [cube, selections, range],
  );

  const toggle = (dimension: DimensionKey, code: string) =>
    setSelections((current) => toggleSelection(current, dimension, code));
  const drill = (picks: Path) => setSelections((current) => drillInto(current, picks));
  const labelOf = (dimension: DimensionKey, code: string) =>
    valuesFor(dimension).find((value) => value.code === code)?.label ?? code;
  const activeFilters = (
    Object.entries(selections) as [DimensionKey, string[] | undefined][]
  ).flatMap(([dimension, codes]) => (codes ?? []).map((code) => ({ dimension, code })));
  const hasFilters = activeFilters.length > 0 || period !== "all";

  return (
    <div dir="rtl" className="space-y-5">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 className="text-lg font-bold text-slate-900">تحلیل کاربران</h2>
          <p className="mt-1 text-sm text-slate-500">
            روی هر میله، ردیف یا خانهٔ جدول کلیک کنید تا کل گزارش روی همان بخش فیلتر و جزئی‌تر شود.
          </p>
        </div>
        <Button
          variant="outline"
          className={`rounded-xl ${outlineButtonClass}`}
          onClick={() => void refresh()}
          disabled={loading}
        >
          {loading ? <LoaderCircle className="animate-spin" /> : <RefreshCw />}
          به‌روزرسانی
        </Button>
      </div>

      {error && (
        <div
          role="alert"
          className="rounded-xl border border-rose-200 bg-rose-50 px-4 py-3 text-sm text-rose-800"
        >
          {error}
        </div>
      )}
      {loading && !cube && (
        <div
          role="status"
          className="flex items-center gap-2 rounded-xl border border-slate-200 bg-white p-5 text-sm text-slate-500"
        >
          <LoaderCircle className="size-4 animate-spin" /> در حال بارگذاری آمار...
        </div>
      )}

      {cube && (
        <>
          <section
            aria-label="فیلترهای گزارش"
            className="flex flex-col gap-3 rounded-2xl border border-slate-200 bg-white p-3 shadow-sm sm:p-4"
          >
            <div className="flex flex-wrap items-center gap-3">
              <span className="text-xs font-medium text-slate-500">بازهٔ ثبت‌نام</span>
              <SegmentedControl
                label="بازهٔ ثبت‌نام"
                options={periodOptions}
                value={period}
                onChange={setPeriod}
              />
              <span className="text-[11px] text-slate-400">
                داده تا{" "}
                {new Intl.DateTimeFormat("fa-IR", {
                  dateStyle: "medium",
                  timeStyle: "short",
                }).format(new Date(cube.asOfUtc))}
              </span>
            </div>
            <div className="flex flex-wrap items-center gap-2">
              <Filter className="size-4 text-slate-400" aria-hidden />
              {activeFilters.length === 0 ? (
                <span className="text-xs text-slate-400">بدون فیلتر — همهٔ کاربران سازمان</span>
              ) : (
                activeFilters.map(({ dimension, code }) => (
                  <button
                    key={`${dimension}:${code}`}
                    type="button"
                    onClick={() => toggle(dimension, code)}
                    className="inline-flex items-center gap-1.5 rounded-full border border-emerald-200 bg-emerald-50 px-2.5 py-1 text-xs text-emerald-900 hover:bg-emerald-100"
                    aria-label={`حذف فیلتر ${dimensionLabels[dimension]}: ${labelOf(dimension, code)}`}
                  >
                    <span className="text-emerald-700/70">{dimensionLabels[dimension]}:</span>
                    {labelOf(dimension, code)}
                    <X className="size-3" />
                  </button>
                ))
              )}
              {hasFilters && (
                <button
                  type="button"
                  onClick={() => {
                    setSelections({});
                    setPeriod("all");
                  }}
                  className="mr-auto text-xs font-medium text-slate-500 underline-offset-4 hover:text-slate-900 hover:underline"
                >
                  پاک کردن همهٔ فیلترها
                </button>
              )}
            </div>
            {period !== "all" && (
              <p className="text-[11px] text-slate-400">
                با انتخاب بازه، فقط کاربرانی شمرده می‌شوند که در آن بازه ثبت‌نام کرده‌اند و تاریخ
                ثبت‌نامشان ثبت شده است.
              </p>
            )}
          </section>

          <KpiRow
            cube={cube}
            filtered={filtered}
            selections={selections}
            period={period}
            today={today}
          />

          <section aria-label="تفکیک‌ها" className="grid gap-4 md:grid-cols-2 2xl:grid-cols-4">
            {ALL_DIMENSIONS.map((dimension) =>
              dimension === "plan" && !cube.subscriptionBreakdownAvailable ? (
                <Card key={dimension} title="به تفکیک اشتراک">
                  <p className="rounded-xl bg-amber-50 px-3 py-3 text-sm text-amber-800">
                    برای مشاهدهٔ تفکیک اشتراک، مجوز «خواندن اشتراک‌ها» لازم است.
                  </p>
                </Card>
              ) : (
                <SlicerCard
                  key={dimension}
                  dimension={dimension}
                  values={valuesFor(dimension)}
                  counts={countBy(filterCells(cube.cells, selections, range, dimension), dimension)}
                  selected={selections[dimension] ?? []}
                  onToggle={(code) => toggle(dimension, code)}
                />
              ),
            )}
          </section>

          <DrillDownSection
            cells={filtered}
            dimensions={dimensions}
            valuesFor={valuesFor}
            onApply={drill}
          />
          <PivotSection
            cells={filtered}
            dimensions={dimensions}
            valuesFor={valuesFor}
            onDrill={drill}
          />
          <TrendSection
            cells={filtered}
            dimensions={dimensions}
            valuesFor={valuesFor}
            range={range}
            today={today}
          />
        </>
      )}
    </div>
  );
}

function SegmentedControl<T extends string>({
  label,
  options,
  value,
  onChange,
}: {
  label: string;
  options: { value: T; label: string }[];
  value: T;
  onChange: (value: T) => void;
}) {
  return (
    <div
      role="radiogroup"
      aria-label={label}
      className="flex flex-wrap gap-1 rounded-xl bg-slate-100 p-1"
    >
      {options.map((option) => (
        <button
          key={option.value}
          type="button"
          role="radio"
          aria-checked={value === option.value}
          onClick={() => onChange(option.value)}
          className={`rounded-lg px-2.5 py-1.5 text-xs font-medium transition-colors ${
            value === option.value
              ? "bg-white text-slate-900 shadow-sm"
              : "text-slate-500 hover:text-slate-800"
          }`}
        >
          {option.label}
        </button>
      ))}
    </div>
  );
}

function DimensionSelect({
  label,
  dimensions,
  value,
  onChange,
  allowEmpty = false,
}: {
  label: string;
  dimensions: DimensionKey[];
  value: DimensionKey | "";
  onChange: (value: DimensionKey | "") => void;
  allowEmpty?: boolean;
}) {
  return (
    <select
      aria-label={label}
      className={selectClass}
      value={value}
      onChange={(event) => onChange(event.target.value as DimensionKey | "")}
    >
      {allowEmpty && <option value="">—</option>}
      {dimensions.map((dimension) => (
        <option key={dimension} value={dimension}>
          {dimensionLabels[dimension]}
        </option>
      ))}
    </select>
  );
}

function Card({
  title,
  subtitle,
  actions,
  children,
}: {
  title: string;
  subtitle?: string;
  actions?: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <section className="min-w-0 rounded-2xl border border-slate-200 bg-white p-4 shadow-sm sm:p-5">
      <div className="mb-4 flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <h3 className="font-bold text-slate-900">{title}</h3>
          {subtitle && <p className="mt-1 text-xs text-slate-500">{subtitle}</p>}
        </div>
        {actions && <div className="flex flex-wrap items-center gap-2">{actions}</div>}
      </div>
      {children}
    </section>
  );
}

function EmptyState({
  children = "داده‌ای برای فیلتر فعلی وجود ندارد.",
}: {
  children?: React.ReactNode;
}) {
  return <p className="text-sm text-slate-400">{children}</p>;
}

// ---- KPIs ----

function KpiRow({
  cube,
  filtered,
  selections,
  period,
  today,
}: {
  cube: AdminUserAnalyticsCube;
  filtered: Cells;
  selections: Selections;
  period: PeriodKey;
  today: string;
}) {
  const total = sumUsers(filtered);
  const paid = sumUsers(filtered.filter((cell) => isPaidPlan(cell.plan)));
  const recentlyActive = sumUsers(
    filtered.filter((cell) => cell.activity === "active7d" || cell.activity === "active30d"),
  );

  // New sign-ups: the selected period, or the last 30 days when no period is chosen.
  const signupPeriod: PeriodKey = period === "all" ? "30d" : period;
  const signups = sumUsers(filterCells(cube.cells, selections, periodRange(signupPeriod, today)));
  const comparison = previousRange(signupPeriod, today);
  const previous = comparison ? sumUsers(filterCells(cube.cells, selections, comparison)) : null;
  const growth = previous !== null && previous > 0 ? ((signups - previous) / previous) * 100 : null;
  const signupLabel = periodOptions.find((option) => option.value === signupPeriod)?.label ?? "";

  return (
    <section aria-label="شاخص‌های کلیدی" className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
      <Kpi
        icon={Users}
        label="کاربران (با فیلتر فعلی)"
        value={number(total)}
        hint={`از ${number(sumUsers(cube.cells))} کاربر سازمان`}
      />
      <Kpi
        icon={CreditCard}
        label="کاربران با اشتراک پولی"
        value={cube.subscriptionBreakdownAvailable ? number(paid) : "—"}
        hint={
          cube.subscriptionBreakdownAvailable
            ? `نرخ تبدیل ${percent(share(paid, total))}`
            : "نیازمند مجوز خواندن اشتراک‌ها"
        }
      />
      <Kpi
        icon={Activity}
        label="فعال در ۳۰ روز اخیر"
        value={number(recentlyActive)}
        hint={`${percent(share(recentlyActive, total))} از کاربران`}
      />
      <Kpi
        icon={UserPlus}
        label={`ثبت‌نام جدید (${signupLabel})`}
        value={number(signups)}
        hint={previous === null ? "بدون دورهٔ مقایسه" : `دورهٔ قبل: ${number(previous)}`}
        trend={growth === null ? undefined : { value: growth, previous: previous! }}
      />
    </section>
  );
}

function Kpi({
  icon: Icon,
  label,
  value,
  hint,
  trend,
}: {
  icon: typeof Users;
  label: string;
  value: string;
  hint?: string;
  trend?: { value: number; previous: number };
}) {
  const up = (trend?.value ?? 0) >= 0;
  return (
    <div className="rounded-2xl border border-slate-200 bg-white p-4 shadow-sm">
      <div className="flex items-center justify-between gap-3">
        <span className="text-sm text-slate-500">{label}</span>
        <span className="flex size-9 items-center justify-center rounded-xl bg-slate-100 text-slate-600">
          <Icon className="size-4" />
        </span>
      </div>
      <p className="mt-2 text-2xl font-bold tabular-nums text-slate-900">{value}</p>
      {trend ? (
        <p
          className={`mt-1 flex flex-wrap items-center gap-1 text-xs ${up ? "text-emerald-700" : "text-rose-700"}`}
        >
          {up ? <TrendingUp className="size-3.5" /> : <TrendingDown className="size-3.5" />}
          {up ? "افزایش" : "کاهش"} {percent(Math.abs(trend.value))}
          <span className="text-slate-400">نسبت به دورهٔ قبل ({number(trend.previous)})</span>
        </p>
      ) : (
        hint && <p className="mt-1 text-xs text-slate-500">{hint}</p>
      )}
    </div>
  );
}

// ---- Slicers (cross-filtering breakdowns) ----

function SlicerCard({
  dimension,
  values,
  counts,
  selected,
  onToggle,
}: {
  dimension: DimensionKey;
  values: DimensionValue[];
  counts: Map<string, number>;
  selected: string[];
  onToggle: (code: string) => void;
}) {
  const total = [...counts.values()].reduce((sum, value) => sum + value, 0);
  const visible = values.filter(
    (value) => (counts.get(value.code) ?? 0) > 0 || selected.includes(value.code),
  );
  return (
    <Card
      title={`به تفکیک ${dimensionLabels[dimension]}`}
      subtitle="کلیک برای فیلتر؛ چند انتخاب مجاز است"
    >
      {visible.length === 0 ? (
        <EmptyState />
      ) : (
        <ul className="space-y-1">
          {visible.map((value) => {
            const count = counts.get(value.code) ?? 0;
            const isSelected = selected.includes(value.code);
            const valueShare = share(count, total);
            return (
              <li key={value.code}>
                <button
                  type="button"
                  aria-pressed={isSelected}
                  onClick={() => onToggle(value.code)}
                  className={`w-full rounded-xl px-2.5 py-2 text-right transition-colors ${
                    isSelected ? "bg-emerald-50 ring-1 ring-emerald-200" : "hover:bg-slate-50"
                  } ${selected.length > 0 && !isSelected ? "opacity-55" : ""}`}
                >
                  <span className="mb-1.5 flex items-center justify-between gap-3 text-sm">
                    <span className="flex min-w-0 items-center gap-2 text-slate-700">
                      <span
                        className="size-2.5 shrink-0 rounded-full"
                        style={{ backgroundColor: value.color }}
                      />
                      <span className="truncate">{value.label}</span>
                    </span>
                    <span className="shrink-0 tabular-nums text-slate-700">
                      {number(count)}{" "}
                      <span className="text-xs text-slate-400">({percent(valueShare)})</span>
                    </span>
                  </span>
                  <span className="block h-1.5 overflow-hidden rounded-full bg-slate-100">
                    <span
                      className="block h-full rounded-full"
                      style={{ width: `${valueShare}%`, backgroundColor: value.color }}
                    />
                  </span>
                </button>
              </li>
            );
          })}
        </ul>
      )}
    </Card>
  );
}

// ---- Drill-down tree ----

function DrillDownSection({
  cells,
  dimensions,
  valuesFor,
  onApply,
}: {
  cells: Cells;
  dimensions: DimensionKey[];
  valuesFor: ValuesFor;
  onApply: (path: Path) => void;
}) {
  const [levels, setLevels] = useState<DimensionKey[]>(() => dimensions.slice(0, 3));
  const [expanded, setExpanded] = useState<Set<string>>(() => new Set());
  useEffect(() => {
    setLevels((current) =>
      current.length > 0 && current.every((dimension) => dimensions.includes(dimension))
        ? current
        : dimensions.slice(0, 3),
    );
  }, [dimensions]);

  const tree = useMemo(() => buildDrillTree(cells, levels, valuesFor), [cells, levels, valuesFor]);

  const setLevel = (index: number, dimension: DimensionKey | "") => {
    setExpanded(new Set());
    setLevels((current) => [...current.slice(0, index), ...(dimension ? [dimension] : [])]);
  };
  const toggleNode = (id: string) =>
    setExpanded((current) => {
      const next = new Set(current);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  const expandAll = () => {
    const ids = new Set<string>();
    const walk = (nodes: DrillNode[]) =>
      nodes.forEach((node) => {
        if (node.children.length > 0) ids.add(node.id);
        walk(node.children);
      });
    walk(tree);
    setExpanded(ids);
  };

  return (
    <Card
      title="تحلیل سلسله‌مراتبی (Drill-down)"
      subtitle="ترتیب سطح‌ها را انتخاب کنید و هر ردیف را باز کنید تا سهم زیرگروه‌ها از والدشان را ببینید."
      actions={
        <>
          {[0, 1, 2].map((index) =>
            index > levels.length ? null : (
              <Fragment key={index}>
                {index > 0 && <ChevronLeft className="size-4 text-slate-300" aria-hidden />}
                <DimensionSelect
                  label={`سطح ${index + 1}`}
                  dimensions={dimensions.filter(
                    (dimension) => !levels.slice(0, index).includes(dimension),
                  )}
                  value={levels[index] ?? ""}
                  onChange={(dimension) => setLevel(index, dimension)}
                  allowEmpty={index > 0}
                />
              </Fragment>
            ),
          )}
          <Button variant="ghost" size="sm" className={ghostButtonClass} onClick={expandAll}>
            باز کردن همه
          </Button>
          <Button
            variant="ghost"
            size="sm"
            className={ghostButtonClass}
            onClick={() => setExpanded(new Set())}
          >
            بستن همه
          </Button>
        </>
      }
    >
      {tree.length === 0 ? (
        <EmptyState />
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full min-w-[560px] text-sm">
            <thead>
              <tr className="border-b border-slate-100 text-xs text-slate-500">
                <th className="py-2 text-right font-medium">گروه</th>
                <th className="w-24 py-2 text-left font-medium">کاربران</th>
                <th className="w-56 py-2 pr-4 text-right font-medium">سهم از سطح بالاتر</th>
                <th className="w-16 py-2">
                  <span className="sr-only">عملیات</span>
                </th>
              </tr>
            </thead>
            <tbody>
              <tr className="border-b border-slate-100 bg-slate-50/60 font-semibold text-slate-800">
                <td className="py-2 pr-2">همهٔ کاربران (با فیلتر فعلی)</td>
                <td className="py-2 text-left tabular-nums">{number(sumUsers(cells))}</td>
                <td className="py-2 pr-4 text-xs text-slate-400">۱۰۰٪</td>
                <td />
              </tr>
              <DrillRows
                nodes={tree}
                depth={0}
                expanded={expanded}
                onToggle={toggleNode}
                onApply={onApply}
              />
            </tbody>
          </table>
        </div>
      )}
    </Card>
  );
}

function DrillRows({
  nodes,
  depth,
  expanded,
  onToggle,
  onApply,
}: {
  nodes: DrillNode[];
  depth: number;
  expanded: Set<string>;
  onToggle: (id: string) => void;
  onApply: (path: Path) => void;
}) {
  return (
    <>
      {nodes.map((node) => {
        const isOpen = expanded.has(node.id);
        const canOpen = node.children.length > 0;
        return (
          <Fragment key={node.id}>
            <tr className="group border-b border-slate-50 hover:bg-slate-50/80">
              <td className="py-1.5" style={{ paddingRight: `${depth * 22 + 4}px` }}>
                <button
                  type="button"
                  disabled={!canOpen}
                  aria-expanded={canOpen ? isOpen : undefined}
                  onClick={() => onToggle(node.id)}
                  className="flex items-center gap-2 rounded-lg px-1 py-0.5 text-right text-slate-700 enabled:hover:text-slate-950"
                >
                  <ChevronLeft
                    className={`size-4 shrink-0 text-slate-400 transition-transform ${canOpen ? "" : "invisible"} ${
                      isOpen ? "-rotate-90" : ""
                    }`}
                    aria-hidden
                  />
                  <span
                    className="size-2.5 shrink-0 rounded-full"
                    style={{ backgroundColor: node.value.color }}
                  />
                  <span className="text-[11px] text-slate-400">
                    {dimensionLabels[node.dimension]}:
                  </span>
                  <span className={depth === 0 ? "font-semibold" : ""}>{node.value.label}</span>
                </button>
              </td>
              <td className="py-1.5 text-left tabular-nums text-slate-800">{number(node.users)}</td>
              <td className="py-1.5 pr-4">
                <span className="flex items-center gap-2">
                  <span className="h-1.5 flex-1 overflow-hidden rounded-full bg-slate-100">
                    <span
                      className="block h-full rounded-full"
                      style={{ width: `${node.shareOfParent}%`, backgroundColor: node.value.color }}
                    />
                  </span>
                  <span className="w-12 shrink-0 text-left text-xs tabular-nums text-slate-500">
                    {percent(node.shareOfParent)}
                  </span>
                </span>
              </td>
              <td className="py-1.5 text-left">
                <button
                  type="button"
                  onClick={() => onApply(node.path)}
                  className="rounded-lg px-2 py-1 text-xs text-emerald-700 opacity-70 hover:bg-emerald-50 hover:opacity-100 focus-visible:opacity-100 group-hover:opacity-100"
                  title="اعمال این مسیر به‌عنوان فیلتر کل صفحه"
                >
                  فیلتر
                </button>
              </td>
            </tr>
            {isOpen && (
              <DrillRows
                nodes={node.children}
                depth={depth + 1}
                expanded={expanded}
                onToggle={onToggle}
                onApply={onApply}
              />
            )}
          </Fragment>
        );
      })}
    </>
  );
}

// ---- Pivot ----

const pivotModes: { value: PivotMode; label: string }[] = [
  { value: "count", label: "تعداد" },
  { value: "row", label: "٪ سطر" },
  { value: "column", label: "٪ ستون" },
  { value: "total", label: "٪ کل" },
];

function PivotSection({
  cells,
  dimensions,
  valuesFor,
  onDrill,
}: {
  cells: Cells;
  dimensions: DimensionKey[];
  valuesFor: ValuesFor;
  onDrill: (picks: Path) => void;
}) {
  const [rowDimension, setRowDimension] = useState<DimensionKey>("acquisitionSource");
  const [columnDimension, setColumnDimension] = useState<DimensionKey>(
    dimensions.includes("plan") ? "plan" : "status",
  );
  const [mode, setMode] = useState<PivotMode>("count");
  useEffect(() => {
    if (!dimensions.includes(rowDimension))
      setRowDimension(dimensions.find((d) => d !== columnDimension)!);
    if (!dimensions.includes(columnDimension))
      setColumnDimension(dimensions.find((d) => d !== rowDimension)!);
  }, [dimensions, rowDimension, columnDimension]);

  const pivot = useMemo(
    () =>
      buildPivot(
        cells,
        rowDimension,
        valuesFor(rowDimension),
        columnDimension,
        valuesFor(columnDimension),
      ),
    [cells, rowDimension, columnDimension, valuesFor],
  );
  const maxCell = Math.max(1, ...pivot.values.flat());

  const choose = (axis: "row" | "column", dimension: DimensionKey | "") => {
    if (!dimension) return;
    const [current, other, setCurrent, setOther] =
      axis === "row"
        ? [rowDimension, columnDimension, setRowDimension, setColumnDimension]
        : [columnDimension, rowDimension, setColumnDimension, setRowDimension];
    if (dimension === other) setOther(current);
    setCurrent(dimension);
  };
  const exportCsv = () => {
    const blob = new Blob([pivotToCsv(pivot, rowDimension, columnDimension)], {
      type: "text/csv;charset=utf-8",
    });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `user-analytics-${rowDimension}-by-${columnDimension}.csv`;
    link.click();
    URL.revokeObjectURL(url);
  };
  const cellText = (row: number, column: number) => {
    const value = pivotDisplayValue(pivot, mode, row, column);
    return mode === "count" ? number(value) : percent(value);
  };
  const rowTotalText = (row: number) =>
    mode === "count"
      ? number(pivot.rowTotals[row])
      : mode === "row"
        ? "۱۰۰٪"
        : percent(share(pivot.rowTotals[row], pivot.total));
  const columnTotalText = (column: number) =>
    mode === "count"
      ? number(pivot.columnTotals[column])
      : mode === "column"
        ? "۱۰۰٪"
        : percent(share(pivot.columnTotals[column], pivot.total));

  return (
    <Card
      title="جدول متقاطع (Pivot)"
      subtitle="ابعاد سطر و ستون را انتخاب کنید؛ کلیک روی هر خانه، کل گزارش را روی همان ترکیب فیلتر می‌کند."
      actions={
        <>
          <label className="flex items-center gap-1.5 text-xs text-slate-500">
            سطر
            <DimensionSelect
              label="بعد سطر"
              dimensions={dimensions}
              value={rowDimension}
              onChange={(d) => choose("row", d)}
            />
          </label>
          <Button
            variant="ghost"
            size="icon"
            className={ghostButtonClass}
            aria-label="جابه‌جایی سطر و ستون"
            onClick={() => {
              setRowDimension(columnDimension);
              setColumnDimension(rowDimension);
            }}
          >
            <ArrowLeftRight />
          </Button>
          <label className="flex items-center gap-1.5 text-xs text-slate-500">
            ستون
            <DimensionSelect
              label="بعد ستون"
              dimensions={dimensions}
              value={columnDimension}
              onChange={(d) => choose("column", d)}
            />
          </label>
          <SegmentedControl
            label="نوع مقدار"
            options={pivotModes}
            value={mode}
            onChange={setMode}
          />
          <Button
            variant="outline"
            size="sm"
            className={`rounded-lg ${outlineButtonClass}`}
            onClick={exportCsv}
            disabled={pivot.rows.length === 0}
          >
            <Download />
            خروجی CSV
          </Button>
        </>
      }
    >
      {pivot.rows.length === 0 ? (
        <EmptyState />
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full min-w-[520px] border-separate border-spacing-0 text-sm">
            <thead>
              <tr className="text-xs text-slate-500">
                <th className="sticky right-0 bg-white py-2 pl-3 text-right font-medium">
                  {dimensionLabels[rowDimension]} / {dimensionLabels[columnDimension]}
                </th>
                {pivot.columns.map((column) => (
                  <th key={column.code} className="px-2 py-2 text-center font-medium">
                    <span className="inline-flex items-center gap-1.5">
                      <span
                        className="size-2 rounded-full"
                        style={{ backgroundColor: column.color }}
                      />
                      {column.label}
                    </span>
                  </th>
                ))}
                <th className="px-2 py-2 text-center font-semibold text-slate-700">جمع</th>
              </tr>
            </thead>
            <tbody>
              {pivot.rows.map((row, rowIndex) => (
                <tr key={row.code}>
                  <th
                    scope="row"
                    className="sticky right-0 border-t border-slate-100 bg-white py-2 pl-3 text-right font-medium text-slate-700"
                  >
                    <button
                      type="button"
                      onClick={() => onDrill({ [rowDimension]: row.code })}
                      className="inline-flex items-center gap-2 hover:text-emerald-700"
                    >
                      <span
                        className="size-2.5 rounded-full"
                        style={{ backgroundColor: row.color }}
                      />
                      {row.label}
                    </button>
                  </th>
                  {pivot.columns.map((column, columnIndex) => {
                    const value = pivot.values[rowIndex][columnIndex];
                    const intensity = value / maxCell;
                    return (
                      <td key={column.code} className="border-t border-slate-100 p-0.5">
                        <button
                          type="button"
                          disabled={value === 0}
                          onClick={() =>
                            onDrill({ [rowDimension]: row.code, [columnDimension]: column.code })
                          }
                          title={`${row.label} × ${column.label}: ${number(value)} کاربر`}
                          className={`w-full rounded-lg px-2 py-2 text-center tabular-nums transition-shadow enabled:hover:ring-2 enabled:hover:ring-emerald-400 ${
                            intensity > 0.55
                              ? "text-white"
                              : value === 0
                                ? "text-slate-300"
                                : "text-slate-800"
                          }`}
                          style={{
                            backgroundColor:
                              value === 0
                                ? "transparent"
                                : `rgba(42, 120, 214, ${0.08 + intensity * 0.82})`,
                          }}
                        >
                          {cellText(rowIndex, columnIndex)}
                        </button>
                      </td>
                    );
                  })}
                  <td className="border-t border-slate-100 px-2 py-2 text-center font-semibold tabular-nums text-slate-800">
                    {rowTotalText(rowIndex)}
                  </td>
                </tr>
              ))}
              <tr className="font-semibold text-slate-800">
                <th
                  scope="row"
                  className="sticky right-0 border-t border-slate-200 bg-slate-50 py-2 pl-3 text-right"
                >
                  جمع
                </th>
                {pivot.columns.map((column, index) => (
                  <td
                    key={column.code}
                    className="border-t border-slate-200 bg-slate-50 px-2 py-2 text-center tabular-nums"
                  >
                    {columnTotalText(index)}
                  </td>
                ))}
                <td className="border-t border-slate-200 bg-slate-50 px-2 py-2 text-center tabular-nums">
                  {mode === "count" ? number(pivot.total) : "۱۰۰٪"}
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      )}
    </Card>
  );
}

// ---- Trend ----

function TrendSection({
  cells,
  dimensions,
  valuesFor,
  range,
  today,
}: {
  cells: Cells;
  dimensions: DimensionKey[];
  valuesFor: ValuesFor;
  range: DateRange | null;
  today: string;
}) {
  const [stack, setStack] = useState<DimensionKey>("acquisitionSource");
  const [hovered, setHovered] = useState<number | null>(null);
  useEffect(() => {
    if (!dimensions.includes(stack)) setStack("acquisitionSource");
  }, [dimensions, stack]);

  const { buckets, granularity, truncated } = useMemo(
    () => buildTrend(cells, stack, range, today),
    [cells, stack, range, today],
  );
  const series = valuesFor(stack).filter((value) =>
    buckets.some((bucket) => (bucket.counts.get(value.code) ?? 0) > 0),
  );
  const max = Math.max(1, ...buckets.map((bucket) => bucket.total));
  const focus = hovered !== null ? buckets[hovered] : buckets[buckets.length - 1];
  const undated = sumUsers(cells.filter((cell) => cell.registeredOn === null));
  const labelStep = Math.ceil(buckets.length / 12);
  const minWidth = `${buckets.length * 22}px`;

  return (
    <Card
      title="روند ثبت‌نام"
      subtitle={`${granularity === "day" ? "روزانه" : "ماهانه (شمسی)"} به تفکیک ${dimensionLabels[stack]}${
        truncated ? " — ۲۴ ماه اخیر" : ""
      }`}
      actions={
        <label className="flex items-center gap-1.5 text-xs text-slate-500">
          تفکیک
          <DimensionSelect
            label="تفکیک روند"
            dimensions={dimensions}
            value={stack}
            onChange={(dimension) => dimension && setStack(dimension)}
          />
        </label>
      }
    >
      {buckets.length === 0 || series.length === 0 ? (
        <EmptyState>در این بازه ثبت‌نامی با تاریخ مشخص وجود ندارد.</EmptyState>
      ) : (
        <>
          <div className="mb-3 flex flex-wrap items-center gap-x-4 gap-y-2 text-xs text-slate-600">
            {series.map((value) => (
              <span key={value.code} className="inline-flex items-center gap-1.5">
                <span className="size-2.5 rounded-sm" style={{ backgroundColor: value.color }} />
                {value.label}
              </span>
            ))}
          </div>
          {focus && (
            <div
              className="mb-3 flex min-h-10 flex-wrap items-center gap-x-4 gap-y-1 rounded-xl bg-slate-50 px-3 py-2 text-xs text-slate-600"
              aria-live="polite"
            >
              <span className="font-semibold text-slate-800">{focus.fullLabel}</span>
              <span className="tabular-nums">جمع: {number(focus.total)}</span>
              {series.map((value) => {
                const count = focus.counts.get(value.code) ?? 0;
                return count > 0 ? (
                  <span key={value.code} className="inline-flex items-center gap-1 tabular-nums">
                    <span className="size-2 rounded-sm" style={{ backgroundColor: value.color }} />
                    {value.label}: {number(count)}
                  </span>
                ) : null;
              })}
            </div>
          )}
          <div className="overflow-x-auto" dir="ltr">
            <div
              className="flex h-56 items-end gap-1 border-b border-slate-200 pb-1"
              style={{ minWidth }}
              onMouseLeave={() => setHovered(null)}
            >
              {buckets.map((bucket, index) => {
                const height = bucket.total === 0 ? 0 : Math.max(3, (bucket.total / max) * 100);
                const present = series.filter((value) => (bucket.counts.get(value.code) ?? 0) > 0);
                return (
                  <button
                    key={bucket.key}
                    type="button"
                    className={`flex h-full min-w-0 flex-1 flex-col items-center justify-end rounded-t-md focus:outline-none focus-visible:ring-2 focus-visible:ring-emerald-400 ${
                      hovered === index ? "bg-slate-100" : ""
                    }`}
                    onMouseEnter={() => setHovered(index)}
                    onFocus={() => setHovered(index)}
                    aria-label={`${bucket.fullLabel}: ${number(bucket.total)} ثبت‌نام`}
                  >
                    <span className="mb-1 text-[10px] tabular-nums text-slate-500">
                      {bucket.total > 0 && (hovered === index || buckets.length <= 14)
                        ? number(bucket.total)
                        : ""}
                    </span>
                    <span
                      className="flex w-full max-w-8 flex-col-reverse gap-[2px]"
                      style={{ height: `${height}%` }}
                    >
                      {present.map((value, seriesIndex) => (
                        <span
                          key={value.code}
                          className={seriesIndex === present.length - 1 ? "rounded-t-[4px]" : ""}
                          style={{
                            flexGrow: bucket.counts.get(value.code),
                            flexBasis: 0,
                            minHeight: 2,
                            backgroundColor: value.color,
                          }}
                        />
                      ))}
                    </span>
                  </button>
                );
              })}
            </div>
            <div className="flex gap-1 pt-1" style={{ minWidth }}>
              {buckets.map((bucket, index) => (
                <span
                  key={bucket.key}
                  className="min-w-0 flex-1 truncate text-center text-[10px] text-slate-500"
                >
                  {index % labelStep === 0 || index === buckets.length - 1 ? bucket.label : ""}
                </span>
              ))}
            </div>
          </div>
          {undated > 0 && (
            <p className="mt-3 text-[11px] text-slate-400">
              {number(undated)} کاربر بدون تاریخ ثبت‌نام (حساب‌های قدیمی) در این نمودار شمرده
              نشده‌اند.
            </p>
          )}
        </>
      )}
    </Card>
  );
}
