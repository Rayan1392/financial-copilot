import { useAnswerDensity } from "@/components/app/answer-density";
import { useIsAnswerPicture } from "@/components/app/answer-picture";
import {
  METRIC_LABELS,
  type IndustryComparisonModel,
  type IndustryComparisonRow,
  type MetricStatus,
  type PriceEvidence,
} from "./industry-comparison-model";

// Feature 125 change «responsive-comparison-table». The comparison never scrolls sideways: the layout follows
// the width of the answer itself (CSS container queries), not the viewport, so an open sidebar or browser zoom
// picks the layout that fits.
//   ≥ 40rem  six-column table (price evidence answers)
//   ≥ 26rem  four-column table, rank and trade details on a full-width line under each symbol
//   narrower cards, also the only layout in the phone workspace (compact density)
// A shared answer picture (change «comparison-answer-image-share») has a fixed width, so it renders the one table that
// fits its content and none of the alternatives: the capture cost grows with every node, hidden ones included.
// Colours come from the theme tokens, so both site themes stay readable; every colour cue also carries a ▼/▲
// marker and screen-reader text.

const STATUS_TEXT: Record<Exclude<MetricStatus, "neutral">, string> = {
  below: "کمتر از میانگین گروه",
  above: "بیشتر از میانگین گروه",
};

const STATUS_CELL: Record<MetricStatus, string> = {
  below: "bg-emerald-soft",
  above: "bg-rose/12",
  neutral: "",
};

const WRAP = "[overflow-wrap:anywhere]";

function MetricValue({ value, status }: { value: string; status: MetricStatus }) {
  return (
    <span className="inline-flex flex-wrap items-baseline gap-x-1 tabular-nums font-fanum">
      <span>{value}</span>
      {status !== "neutral" && (
        <>
          <span
            aria-hidden="true"
            className={`text-[10px] ${status === "below" ? "text-emerald" : "text-rose"}`}
          >
            {status === "below" ? "▼" : "▲"}
          </span>
          <span className="sr-only">{STATUS_TEXT[status]}</span>
        </>
      )}
    </span>
  );
}

function EvidenceLines({ evidence }: { evidence?: PriceEvidence }) {
  if (!evidence) return <span className="text-muted-foreground">—</span>;
  if (!evidence.price) return <span className={WRAP}>{evidence.raw}</span>;
  return (
    <span className={`flex flex-col gap-0.5 font-fanum ${WRAP}`}>
      <span className="text-foreground">{evidence.price}</span>
      <span className="text-xs text-muted-foreground">
        {evidence.source}، {evidence.date}
      </span>
      <span className="text-xs text-muted-foreground">تعادلی {evidence.equilibrium}</span>
    </span>
  );
}

function DetailItems({ row, showRank = true }: { row: IndustryComparisonRow; showRank?: boolean }) {
  const evidence = row.evidence;
  const items: Array<[string, string]> = showRank ? [["رتبه", row.rank ?? "—"]] : [];
  if (evidence?.price) {
    items.push(["قیمت معامله", evidence.price]);
    items.push(["تاریخ و منبع", `${evidence.date}، ${evidence.source}`]);
    items.push(["قیمت تعادلی", evidence.equilibrium ?? "—"]);
  } else {
    items.push(["قیمت و تاریخ معامله", evidence?.raw ?? "—"]);
  }
  return (
    <dl className="flex flex-wrap gap-x-4 gap-y-1 text-xs text-muted-foreground">
      {items.map(([label, value]) => (
        <div key={label} className={`flex min-w-0 gap-1 ${WRAP}`}>
          <dt>{label}:</dt>
          <dd className="font-fanum text-foreground">{value}</dd>
        </div>
      ))}
    </dl>
  );
}

const TH = "px-3 py-2 text-right align-bottom text-xs font-medium text-muted-foreground";
const TD = `px-3 py-2 align-top ${WRAP}`;

function MetricHeaders() {
  return (
    <>
      <th scope="col" className={TH}>
        نماد
      </th>
      {METRIC_LABELS.map((label) => (
        <th key={label} scope="col" className={TH}>
          {label}
        </th>
      ))}
    </>
  );
}

function MetricCells({ row }: { row: IndustryComparisonRow }) {
  return (
    <>
      <th scope="row" className={`${TD} text-right font-semibold text-foreground`}>
        {row.symbol}
      </th>
      {row.metrics.map((value, index) => (
        <td key={index} className={`${TD} ${STATUS_CELL[row.statuses[index]]}`}>
          <MetricValue value={value} status={row.statuses[index]} />
        </td>
      ))}
    </>
  );
}

function BenchmarkRow({ model, trailing }: { model: IndustryComparisonModel; trailing: number }) {
  if (!model.benchmark) return null;
  return (
    <tr className="border-t-2 border-foreground/15 bg-surface-2 font-semibold text-foreground">
      <th scope="row" className={`${TD} text-right`}>
        {model.benchmark.label}
      </th>
      {model.benchmark.metrics.map((value, index) => (
        <td key={index} className={`${TD} tabular-nums font-fanum`}>
          {value}
        </td>
      ))}
      {trailing > 0 && <td colSpan={trailing} />}
    </tr>
  );
}

function WideTable({ model }: { model: IndustryComparisonModel }) {
  return (
    <table className="w-full table-fixed border-collapse text-sm" data-layout="wide">
      <caption className="sr-only">مقایسه نماد با میانگین گروه صنعت</caption>
      <colgroup>
        <col className="w-[15%]" />
        <col className="w-[14%]" />
        <col className="w-[14%]" />
        <col className="w-[15%]" />
        <col className="w-[11%]" />
        <col className="w-[31%]" />
      </colgroup>
      <thead className="bg-surface">
        <tr>
          <MetricHeaders />
          <th scope="col" className={TH}>
            رتبه
          </th>
          <th scope="col" className={TH}>
            قیمت و تاریخ معامله
          </th>
        </tr>
      </thead>
      <tbody>
        {model.members.map((row, index) => (
          <tr key={`${row.symbol}:${index}`} className="border-t border-hairline text-foreground">
            <MetricCells row={row} />
            <td className={`${TD} font-fanum`}>{row.rank ?? "—"}</td>
            <td className={`${TD} text-sm`}>
              <EvidenceLines evidence={row.evidence} />
            </td>
          </tr>
        ))}
        <BenchmarkRow model={model} trailing={2} />
      </tbody>
    </table>
  );
}

function CompactTable({ model }: { model: IndustryComparisonModel }) {
  return (
    <table className="w-full table-fixed border-collapse text-sm" data-layout="compact-table">
      <caption className="sr-only">مقایسه نماد با میانگین گروه صنعت</caption>
      <colgroup>
        <col className="w-[28%]" />
        <col className="w-[24%]" />
        <col className="w-[24%]" />
        <col className="w-[24%]" />
      </colgroup>
      <thead className="bg-surface">
        <tr>
          <MetricHeaders />
        </tr>
      </thead>
      <tbody>
        {model.members.map((row, index) => (
          <FragmentRows
            key={`${row.symbol}:${index}`}
            row={row}
            withDetails={model.hasPriceEvidence}
          />
        ))}
        <BenchmarkRow model={model} trailing={0} />
      </tbody>
    </table>
  );
}

function FragmentRows({ row, withDetails }: { row: IndustryComparisonRow; withDetails: boolean }) {
  return (
    <>
      <tr className="border-t border-hairline text-foreground">
        <MetricCells row={row} />
      </tr>
      {withDetails && (
        <tr className="text-foreground">
          <td colSpan={4} className="px-3 pb-2 pt-0">
            <DetailItems row={row} />
          </td>
        </tr>
      )}
    </>
  );
}

function Cards({ model }: { model: IndustryComparisonModel }) {
  return (
    <ul className="space-y-2" data-layout="cards" aria-label="مقایسه نماد با میانگین گروه صنعت">
      {model.members.map((row, index) => (
        <li
          key={`${row.symbol}:${index}`}
          className="rounded-lg border border-hairline bg-surface p-3 text-foreground"
        >
          <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
            <span className={`text-base font-semibold ${WRAP}`}>{row.symbol}</span>
            {row.rank && (
              <span className="font-fanum text-xs text-muted-foreground">رتبه {row.rank}</span>
            )}
          </div>
          <dl className="mt-2 grid grid-cols-3 gap-1.5">
            {row.metrics.map((value, metricIndex) => (
              <div
                key={metricIndex}
                className={`min-w-0 rounded-md px-2 py-1.5 ${STATUS_CELL[row.statuses[metricIndex]] || "bg-surface-2"}`}
              >
                <dt className="text-[11px] leading-5 text-foreground/75">
                  {METRIC_LABELS[metricIndex]}
                </dt>
                <dd className={`text-sm ${WRAP}`}>
                  <MetricValue value={value} status={row.statuses[metricIndex]} />
                </dd>
              </div>
            ))}
          </dl>
          {model.hasPriceEvidence && (
            <div className="mt-2 border-t border-hairline pt-2">
              <DetailItems row={row} showRank={false} />
            </div>
          )}
        </li>
      ))}
      {model.benchmark && (
        <li className="rounded-lg border border-foreground/15 bg-surface-2 p-3 text-foreground">
          <div className="text-sm font-semibold">{model.benchmark.label}</div>
          <dl className="mt-2 grid grid-cols-3 gap-1.5">
            {model.benchmark.metrics.map((value, metricIndex) => (
              <div key={metricIndex} className="min-w-0">
                <dt className="text-[11px] leading-5 text-foreground/75">
                  {METRIC_LABELS[metricIndex]}
                </dt>
                <dd className={`text-sm font-semibold tabular-nums font-fanum ${WRAP}`}>{value}</dd>
              </div>
            ))}
          </dl>
        </li>
      )}
    </ul>
  );
}

function Legend() {
  return (
    <p
      className="mt-1.5 flex flex-wrap gap-x-4 gap-y-1 text-[11px] text-muted-foreground"
      aria-hidden="true"
    >
      <span>
        <span className="text-emerald">▼</span> {STATUS_TEXT.below}
      </span>
      <span>
        <span className="text-rose">▲</span> {STATUS_TEXT.above}
      </span>
    </p>
  );
}

export function IndustryComparison({ model }: { model: IndustryComparisonModel }) {
  const compact = useAnswerDensity() === "compact";
  const picture = useIsAnswerPicture();
  const tableFrame = "overflow-hidden rounded-lg ring-1 ring-hairline";

  return (
    <div className="@container my-2 min-w-0" dir="rtl" data-testid="industry-comparison">
      {picture ? (
        <div className={tableFrame}>
          {model.hasPriceEvidence ? <WideTable model={model} /> : <CompactTable model={model} />}
        </div>
      ) : compact ? (
        <Cards model={model} />
      ) : (
        <>
          <div className="@min-[26rem]:hidden">
            <Cards model={model} />
          </div>
          {model.hasPriceEvidence ? (
            <>
              <div className={`hidden @min-[26rem]:block @min-[40rem]:hidden ${tableFrame}`}>
                <CompactTable model={model} />
              </div>
              <div className={`hidden @min-[40rem]:block ${tableFrame}`}>
                <WideTable model={model} />
              </div>
            </>
          ) : (
            <div className={`hidden @min-[26rem]:block ${tableFrame}`}>
              <CompactTable model={model} />
            </div>
          )}
        </>
      )}
      <Legend />
    </div>
  );
}
