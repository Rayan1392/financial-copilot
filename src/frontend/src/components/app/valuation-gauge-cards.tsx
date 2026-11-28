import { useId, useState, type CSSProperties, type KeyboardEvent, type ReactNode } from "react";
import {
  EQUILIBRIUM_POSITION_TEXT,
  KIND_TITLE,
  RATIO_POSITION_TEXT,
  SOURCE_NAME,
  bandPath,
  dialPoint,
  formatExactDecimal,
  formatPercent,
  lowerBoundComparison,
  formatRatio2,
  formatRial0,
  formatTehranDate,
  formatTehranDateTime,
  forwardDetailsText,
  forwardEstimate,
  forwardLabel,
  isDisplayable,
  quoteSourceText,
  unavailableText,
  type ValuationGaugeCard,
  type ValuationGaugeCardsResult,
  type ValuationRatioPosition,
} from "@/lib/chat/valuation-gauges";
import { toPersianDigits } from "@/lib/format/persian";
import { cn } from "@/lib/utils";

/*
 * Feature 159 valuation gauge cards, visual refresh (change «valuation-gauge-cards-visual-refresh»).
 *
 * The server owns every fact, angle and derived figure; this file only formats and draws them. Display rule: P/E and
 * P/S with exactly two decimals, prices in whole rials; the payload keeps the exact provider strings. The dial is one
 * image with a spoken summary; every value and figure is also present as text, so nothing relies on colour or position
 * alone. Colours come from the `--gauge-*` theme tokens in styles.css (the app theme is a class, so no `dark:`).
 */

const CX = 170;
const CY = 168;
const OUTER = 138;
const INNER = 96;
// Wide enough for the «حد پایین P/E» and «حد بالا P/E» labels beside the dial and tall enough for the axis-end labels
// under its feet.
const DIAL_VIEWBOX = "-54 -44 448 240";
const BAND_GAP = 0.9;

const STATE_LABEL: Partial<Record<ValuationGaugeCard["state"], string>> = {
  Partial: "ناقص",
  NoData: "بدون داده",
  Stale: "قدیمی",
  TemporarilyUnavailable: "موقتاً در دسترس نیست",
};

const WARM_POSITIONS: ValuationRatioPosition[] = ["AverageToMax", "AboveMax"];
const COOL_POSITIONS: ValuationRatioPosition[] = ["BelowMin", "MinToAverage"];

const kindName = (card: ValuationGaugeCard) => (card.kind === "Ps" ? "P/S" : "P/E");
const band = (index: number) => `var(--gauge-band-${index + 1})`;
// The two light middle bands carry dark text; the outer bands carry light text.
const bandInk = (index: number) =>
  index === 2 || index === 3 ? "var(--gauge-ink-on-light)" : "var(--gauge-ink-on-dark)";

/** 0-based band of an inside needle, or -1. Geometry only; works for cards stored before the refresh. */
function activeBandIndex(card: ValuationGaugeCard): number {
  if (card.insights?.currentBand) return card.insights.currentBand - 1;
  const needle = card.geometry?.needle;
  if (!needle || needle.side !== "Inside") return -1;
  return Math.min(5, Math.floor(needle.angle / 30));
}

/** Accessible name of the forward needle: the ratio, whose value it is (the label), the value and a pin. */
function forwardNeedleName(name: string, label: string, value: string, side: string): string {
  const pin =
    side === "PinnedBelow"
      ? "؛ روی ابتدای محور نگه داشته شده است"
      : side === "PinnedAbove"
        ? "؛ روی انتهای محور نگه داشته شده است"
        : "";
  return `نشانگر خط‌چین: ${name} ${label} ${formatRatio2(value)}${pin}`;
}

function Num({ children, className }: { children: ReactNode; className?: string }) {
  return <bdi className={cn("font-fanum tabular-nums", className)}>{children}</bdi>;
}

// ---------------------------------------------------------------------------------------------------------------
// Card frame

function CardShell({
  card,
  symbol,
  companyName,
  showCompany,
  children,
}: {
  card: ValuationGaugeCard;
  symbol: string;
  companyName?: string | null;
  showCompany: boolean;
  children: ReactNode;
}) {
  const chip = STATE_LABEL[card.state];
  return (
    <section
      aria-label={`${KIND_TITLE[card.kind]} ${symbol}`}
      data-testid={`valuation-gauge-card-${card.kind}`}
      data-state={card.state}
      className="@container min-w-0 rounded-2xl bg-card p-4 shadow-sm ring-1 ring-hairline sm:p-5"
    >
      <header className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-1">
        <h3 className="flex min-w-0 flex-wrap items-baseline gap-x-2 text-lg font-extrabold leading-7">
          {KIND_TITLE[card.kind]}
          {/* A real space keeps the spoken heading apart; the flex gap alone is visual. */}
          {showCompany && " "}
          {showCompany && (
            <span className="text-sm font-normal text-muted-foreground">
              <bdi>{symbol}</bdi>
              {companyName && <> · {companyName}</>}
            </span>
          )}
        </h3>
        {chip && (
          <span className="rounded-full px-2.5 py-0.5 text-xs text-muted-foreground ring-1 ring-hairline">
            {chip}
          </span>
        )}
      </header>
      <div className="mt-2">{children}</div>
      <p
        className="mt-3 text-xs leading-5 text-muted-foreground"
        data-testid="valuation-gauge-source"
      >
        {SOURCE_NAME}
        {card.fetchedAtUtc && (
          <>
            {" "}
            · دریافت <Num>{formatTehranDate(card.fetchedAtUtc)}</Num>
          </>
        )}
      </p>
    </section>
  );
}

// ---------------------------------------------------------------------------------------------------------------
// Ratio dial

function RatioDial({
  card,
  summary,
  activeIndex,
  onSelectBand,
}: {
  card: ValuationGaugeCard;
  summary: string;
  activeIndex: number;
  onSelectBand: (index: number) => void;
}) {
  const geometry = card.geometry!;
  const ratio = card.ratio!;
  const name = kindName(card);
  const needle = geometry.needle ?? null;
  const hero = ratio.current ? formatRatio2(ratio.current) : "—";
  const marker = (role: string) => geometry.markers.find((item) => item.role === role);

  const ticks = [];
  for (let angle = 0; angle <= 180; angle += 5) {
    const major = angle % 30 === 0;
    const a = dialPoint(angle, OUTER + 8, CX, CY);
    const b = dialPoint(angle, OUTER + (major ? 15 : 11), CX, CY);
    ticks.push(
      <line
        key={angle}
        x1={a.x}
        y1={a.y}
        x2={b.x}
        y2={b.y}
        stroke="var(--muted-foreground)"
        strokeOpacity={major ? 0.55 : 0.25}
        strokeWidth={major ? 1.4 : 1}
      />,
    );
  }

  const bound = (
    role: string,
    label: string,
    color: string,
    anchor: "start" | "middle" | "end",
    dy: number,
  ) => {
    const item = marker(role);
    if (!item) return null;
    const stemFrom = dialPoint(item.angle, OUTER + 1, CX, CY);
    const stemTo = dialPoint(item.angle, OUTER + 17, CX, CY);
    const text = dialPoint(item.angle, OUTER + 30, CX, CY);
    return (
      <g key={role} data-testid={`valuation-gauge-marker-${role}`} data-color={color}>
        <line
          x1={stemFrom.x}
          y1={stemFrom.y}
          x2={stemTo.x}
          y2={stemTo.y}
          stroke={color}
          strokeWidth="2.5"
          strokeLinecap="round"
        />
        <circle cx={stemTo.x} cy={stemTo.y} r="3.6" fill={color} />
        <text x={text.x} y={text.y + dy} textAnchor={anchor} fill={color} className="font-fanum">
          <tspan x={text.x} fontSize="12" fontWeight="700">
            {label} {name}
          </tspan>
          <tspan x={text.x} dy="18" fontSize="16" fontWeight="800">
            {formatRatio2(item.value)}
          </tspan>
        </text>
      </g>
    );
  };

  const foot = (angle: number, value: string) => {
    const point = dialPoint(angle, (OUTER + INNER) / 2, CX, CY);
    return (
      <text
        x={point.x}
        y={point.y + 22}
        textAnchor="middle"
        fontSize="11"
        fill="var(--muted-foreground)"
        className="font-fanum"
      >
        {formatRatio2(value)}
      </text>
    );
  };

  let pointer: ReactNode = null;
  if (needle) {
    // Drawn at the final angle and swept in from the left end; motion-safe only.
    const notchFrom = dialPoint(needle.angle, OUTER + 7, CX, CY);
    const notchTo = dialPoint(needle.angle, OUTER + 22, CX, CY);
    // The blade's hub stays outside the hero value in the middle of the dial.
    const bladeFrom = dialPoint(needle.angle, INNER - 14, CX, CY);
    const bladeTo = dialPoint(needle.angle, INNER + 9, CX, CY);
    const dashed = needle.side !== "Inside" ? "5 3" : undefined;
    pointer = (
      <g
        data-testid="valuation-gauge-needle"
        data-side={needle.side}
        className="motion-safe:animate-[valuation-gauge-sweep_700ms_cubic-bezier(0.2,0.8,0.2,1)]"
        style={
          {
            "--gauge-sweep-from": `${-needle.angle}deg`,
            transformOrigin: `${CX}px ${CY}px`,
            transformBox: "view-box",
          } as CSSProperties
        }
      >
        <line
          x1={notchFrom.x}
          y1={notchFrom.y}
          x2={notchTo.x}
          y2={notchTo.y}
          stroke="var(--foreground)"
          strokeWidth="3"
          strokeLinecap="round"
        />
        <line
          x1={bladeFrom.x}
          y1={bladeFrom.y}
          x2={bladeTo.x}
          y2={bladeTo.y}
          stroke="var(--card)"
          strokeWidth="7"
          strokeLinecap="round"
        />
        <line
          x1={bladeFrom.x}
          y1={bladeFrom.y}
          x2={bladeTo.x}
          y2={bladeTo.y}
          stroke="var(--foreground)"
          strokeWidth="3"
          strokeLinecap="round"
          strokeDasharray={dashed}
        />
        <circle
          cx={bladeFrom.x}
          cy={bladeFrom.y}
          r="5"
          fill="var(--card)"
          stroke="var(--foreground)"
          strokeWidth="3"
        />
      </g>
    );
  }

  // Sapio's forward estimate (change «valuation-gauge-forward-ratio»): a thinner, finely dashed needle with a ring at
  // its tip, so it never reads as the solid current needle or as the current needle pinned at an axis end.
  const forward = forwardEstimate(card);
  let forwardPointer: ReactNode = null;
  if (forward) {
    const from = dialPoint(forward.angle, INNER - 10, CX, CY);
    const to = dialPoint(forward.angle, OUTER + 14, CX, CY);
    const ring = dialPoint(forward.angle, OUTER + 21, CX, CY);
    forwardPointer = (
      <g
        data-testid="valuation-gauge-forward-needle"
        data-side={forward.side}
        aria-label={forwardNeedleName(name, forwardLabel(forward), forward.value, forward.side)}
      >
        <title>{forwardNeedleName(name, forwardLabel(forward), forward.value, forward.side)}</title>
        <line
          x1={from.x}
          y1={from.y}
          x2={to.x}
          y2={to.y}
          stroke="var(--card)"
          strokeWidth="5"
          strokeLinecap="round"
        />
        <line
          x1={from.x}
          y1={from.y}
          x2={to.x}
          y2={to.y}
          stroke="var(--foreground)"
          strokeWidth="2"
          strokeLinecap="round"
          strokeDasharray="2 3.5"
        />
        <circle
          cx={ring.x}
          cy={ring.y}
          r="5.5"
          fill="var(--card)"
          stroke="var(--foreground)"
          strokeWidth="2"
        />
      </g>
    );
  }

  return (
    <div className="relative mx-auto w-full max-w-[380px] [container-type:inline-size]">
      <svg
        viewBox={DIAL_VIEWBOX}
        className="block h-auto w-full overflow-visible"
        role="img"
        aria-label={summary}
        direction="ltr"
        data-testid="valuation-gauge-dial"
      >
        {ticks}
        {geometry.sectors.map((sector, index) => {
          const active = index === activeIndex;
          const start = sector.startAngle + (index > 0 ? BAND_GAP : 0);
          const end = sector.endAngle - (index < geometry.sectors.length - 1 ? BAND_GAP : 0);
          const label = dialPoint(
            (sector.startAngle + sector.endAngle) / 2,
            (OUTER + INNER) / 2,
            CX,
            CY,
          );
          return (
            <g
              key={sector.order}
              onClick={() => onSelectBand(index)}
              className="cursor-pointer"
              data-testid="valuation-gauge-band"
              data-active={active || undefined}
            >
              <path
                d={bandPath(
                  start,
                  end,
                  active ? OUTER + 4 : OUTER,
                  active ? INNER - 4 : INNER,
                  CX,
                  CY,
                )}
                fill={band(index)}
                stroke={active ? "var(--foreground)" : undefined}
                strokeWidth={active ? 1.5 : undefined}
              />
              {sector.percentage != null && (
                <text
                  x={label.x}
                  y={label.y + 4.5}
                  textAnchor="middle"
                  fontSize={active ? 13 : 12.5}
                  fontWeight={active ? 800 : 700}
                  fill={bandInk(index)}
                  className="font-fanum"
                >
                  {formatExactDecimal(sector.percentage)}٪
                </text>
              )}
            </g>
          );
        })}
        {bound("min", "حد پایین", "var(--gauge-low)", "end", -8)}
        {bound("average", "میانگین", "var(--foreground)", "middle", -24)}
        {bound("max", "حد بالا", "var(--gauge-high)", "start", -8)}
        {foot(0, ratio.start)}
        {foot(180, ratio.end)}
        {forwardPointer}
        {pointer}
      </svg>
      <div className="pointer-events-none absolute inset-x-0 bottom-[1%] grid justify-items-center">
        <strong
          className="font-fanum font-extrabold leading-none tabular-nums"
          // Long values (a pinned P/E of ۱۴٬۲۵۰٫۷۵) shrink to stay clearly apart from a pointer pinned at either foot:
          // about 28 % of the dial width at 0.6 em per glyph. Values up to five glyphs keep the full size.
          style={{
            fontSize: `clamp(12px, min(8.5cqi, ${(46 / hero.length).toFixed(2)}cqi), 38px)`,
          }}
          data-testid="valuation-gauge-hero"
        >
          <bdi>{hero}</bdi>
        </strong>
        <small className="mt-1 text-[clamp(11px,3.3cqi,13px)] leading-4 text-muted-foreground">
          {ratio.current ? `${name} امروز` : `${name} امروز ثبت نشده`}
        </small>
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------------------------------------------
// Ratio card

function Tile({
  label,
  value,
  note,
  tone,
}: {
  label: string;
  value: ReactNode;
  note?: string;
  tone?: "warm" | "cool";
}) {
  return (
    <div className="grid min-w-0 content-start gap-0.5 px-2 py-2.5 @xs:px-3">
      <span className="text-xs text-muted-foreground">{label}</span>
      <span
        className={cn(
          "text-base font-extrabold leading-7 @xs:text-lg @md:text-xl",
          tone === "warm" && "text-[var(--gauge-warm)]",
          tone === "cool" && "text-[var(--gauge-cool)]",
        )}
      >
        {value}
      </span>
      {note && <span className="text-xs leading-5 text-muted-foreground">{note}</span>}
    </div>
  );
}

function Tiles({ children }: { children: ReactNode[] }) {
  const items = children.filter(Boolean);
  return (
    <div
      className="grid divide-x divide-hairline rounded-xl bg-surface ring-1 ring-hairline"
      style={{ gridTemplateColumns: `repeat(${items.length}, minmax(0, 1fr))` }}
    >
      {items}
    </div>
  );
}

function HistoryStrip({
  card,
  activeIndex,
  onSelectBand,
}: {
  card: ValuationGaugeCard;
  activeIndex: number;
  onSelectBand: (index: number) => void;
}) {
  const ratio = card.ratio!;
  const shares = (ratio.dayPercentages ?? []).map((value) => Number(value));
  if (shares.length !== 6) return null;
  const cumulative = shares.reduce<number[]>(
    (acc, value) => [...acc, (acc.at(-1) ?? 0) + value],
    [],
  );
  const totalDays = ratio.dayCounts.reduce((sum, value) => sum + value, 0);
  const labels: Array<{ at: number; value: string; color: string }> = [];
  for (const [at, value, color] of [
    [cumulative[0], ratio.min, "var(--gauge-low)"],
    [cumulative[2], ratio.average, "var(--foreground)"],
    [cumulative[4], ratio.max, "var(--gauge-high)"],
  ] as const) {
    // Keep labels apart when neighbouring bands are thin; the dial and the details still show every bound.
    if (labels.length === 0 || at - labels[labels.length - 1].at >= 14)
      labels.push({ at, value, color });
  }
  const insights = card.insights;
  const name = kindName(card);
  return (
    <div className="grid gap-2" data-testid="valuation-gauge-history">
      <div className="flex items-baseline justify-between gap-2 text-sm">
        <span className="font-bold">تاریخچه در یک نگاه</span>
        <span className="text-xs text-muted-foreground">
          <Num>{toPersianDigits(totalDays)}</Num> روز معاملاتی
        </span>
      </div>
      <div dir="ltr" className="relative pt-2.5">
        {activeIndex >= 0 && (
          <span
            aria-hidden
            className="absolute top-0 size-0 -translate-x-1/2 border-x-[6px] border-t-[8px] border-x-transparent border-t-foreground"
            style={{ left: `${cumulative[activeIndex] - shares[activeIndex] / 2}%` }}
          />
        )}
        <div className="flex h-3 gap-0.5 overflow-hidden rounded-full">
          {shares.map((share, index) => (
            <button
              type="button"
              key={index}
              onClick={() => onSelectBand(index)}
              aria-label={`بازهٔ ${toPersianDigits(index + 1)}: ${formatExactDecimal(ratio.dayPercentages![index])}٪ روزها`}
              className="relative h-full min-w-0 cursor-pointer before:absolute before:inset-x-0 before:-inset-y-4 before:content-['']"
              style={{ width: `${share}%`, background: band(index) }}
            />
          ))}
        </div>
        <div className="relative h-5 text-xs font-bold">
          {labels.map((label) => (
            <span
              key={label.at}
              className="absolute top-1 whitespace-nowrap"
              // Shift each label by its own position: flush left at 0 %, centred at 50 %, flush right at 100 %.
              style={{
                left: `${Math.min(100, Math.max(0, label.at))}%`,
                transform: `translateX(-${Math.min(100, Math.max(0, label.at))}%)`,
                color: label.color,
              }}
            >
              <Num>{formatRatio2(label.value)}</Num>
            </span>
          ))}
        </div>
      </div>
      {insights?.daysBelowBandPercent && insights.daysInBandPercent && (
        <p className="text-sm leading-7" data-testid="valuation-gauge-history-sentence">
          در <Num className="font-bold">{formatPercent(insights.daysBelowBandPercent)}</Num> روزها{" "}
          {name} پایین‌تر از بازهٔ امروز بوده و{" "}
          <Num className="font-bold">{formatPercent(insights.daysInBandPercent)}</Num> روزها در همین
          بازه.
        </p>
      )}
    </div>
  );
}

function RatioDetails({ card }: { card: ValuationGaugeCard }) {
  const ratio = card.ratio!;
  const name = kindName(card);
  const sectors = card.geometry?.sectors ?? [];
  const hasRanges = sectors.length === 6 && sectors.every((sector) => sector.from && sector.to);
  const detailsForward = forwardEstimate(card);
  return (
    <details data-share-hide className="group mt-3 border-t border-hairline pt-2">
      <summary className="flex min-h-11 cursor-pointer list-none items-center gap-2 text-sm text-muted-foreground [&::-webkit-details-marker]:hidden">
        <span aria-hidden className="inline-block transition-transform group-open:rotate-90">
          ‹
        </span>
        راهنمای خواندن گیج و جزئیات عددی
      </summary>
      <div className="grid gap-3 pb-1 text-sm leading-7">
        <p className="text-muted-foreground">
          گیج {name} شش بازه دارد: حد پایین، میانگین و حد بالای تاریخی مرز بازه‌ها هستند و میانگین
          همیشه بالای گیج است. درصد هر بازه سهم روزهایی است که {name} در آن بازه بوده است. نشانگر
          جای {name} امروز را نشان می‌دهد.
        </p>
        {detailsForward && (
          <p
            data-testid="valuation-gauge-forward-method"
            data-source={detailsForward.source ?? "sapio"}
          >
            {forwardDetailsText(name, card.kind, detailsForward)}
          </p>
        )}
        {ratio.dayPercentages && ratio.dayPercentages.length === 6 && (
          <table className="w-full text-xs">
            <thead>
              <tr className="text-muted-foreground">
                <th scope="col" className="py-1 text-start font-normal">
                  بازه
                </th>
                {hasRanges && (
                  <th scope="col" className="py-1 text-start font-normal">
                    محدوده
                  </th>
                )}
                <th scope="col" className="py-1 text-start font-normal">
                  روز
                </th>
                <th scope="col" className="py-1 text-end font-normal">
                  سهم
                </th>
              </tr>
            </thead>
            <tbody className="font-fanum tabular-nums">
              {ratio.dayPercentages.map((percentage, index) => (
                <tr key={index} className="border-t border-hairline">
                  <th scope="row" className="py-1.5 text-start font-normal">
                    <span
                      aria-hidden
                      className="me-1.5 inline-block size-2.5 rounded-full align-middle"
                      style={{ background: band(index) }}
                    />
                    بازهٔ {toPersianDigits(index + 1)}
                  </th>
                  {hasRanges && (
                    <td className="py-1.5">
                      <bdi>{formatRatio2(sectors[index].from!)}</bdi> تا{" "}
                      <bdi>{formatRatio2(sectors[index].to!)}</bdi>
                    </td>
                  )}
                  <td className="py-1.5">{toPersianDigits(ratio.dayCounts[index] ?? 0)}</td>
                  <td className="py-1.5 text-end">{formatExactDecimal(percentage)}٪</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
        <p className="text-xs text-muted-foreground">
          محور گیج از <Num>{formatRatio2(ratio.start)}</Num> تا <Num>{formatRatio2(ratio.end)}</Num>
        </p>
      </div>
    </details>
  );
}

function RatioCard({ card, symbol, companyName, showCompany }: CardProps) {
  const ratio = card.ratio!;
  const name = kindName(card);
  const [selected, setSelected] = useState<number | null>(null);
  const activeIndex = activeBandIndex(card);
  const position = RATIO_POSITION_TEXT[ratio.position];
  const pinnedText =
    ratio.currentSide === "PinnedBelow"
      ? "نشانگر روی ابتدای محور نگه داشته شده است"
      : ratio.currentSide === "PinnedAbove"
        ? "نشانگر روی انتهای محور نگه داشته شده است"
        : null;
  const forward = forwardEstimate(card);
  const forwardSummary = forward
    ? `؛ ${name} ${forwardLabel(forward)} ${formatRatio2(forward.value)}`
    : "";
  // A card can have a forward value without a current one (the provider's forward P/E of a loss-making company).
  const summary = ratio.current
    ? `${name} ${symbol}: ${formatRatio2(ratio.current)}، ${position}${pinnedText ? `؛ ${pinnedText}` : ""}${forwardSummary}`
    : `${name} ${symbol}: نسبت امروز در دسترس نیست؛ فقط محور تاریخی نمایش داده شده است${forwardSummary}`;
  const tone = WARM_POSITIONS.includes(ratio.position)
    ? "warm"
    : COOL_POSITIONS.includes(ratio.position)
      ? "cool"
      : null;
  const insights = card.insights;
  const distance = insights?.distanceToAveragePercent ?? null;
  const selectedSector = selected !== null ? card.geometry?.sectors[selected] : undefined;

  return (
    <CardShell card={card} symbol={symbol} companyName={companyName} showCompany={showCompany}>
      <div className="grid gap-4 @[620px]:grid-cols-[minmax(300px,1.05fr)_minmax(0,1fr)] @[620px]:items-center @[620px]:gap-6">
        <div className="grid justify-items-center gap-2.5">
          {card.geometry ? (
            <RatioDial
              card={card}
              summary={summary}
              activeIndex={activeIndex}
              onSelectBand={setSelected}
            />
          ) : (
            <p className="text-sm text-muted-foreground">
              مرزهای محور برای رسم گیج معتبر نیست؛ ارقام در جزئیات آمده‌اند.
            </p>
          )}
          {ratio.current && (
            <span
              className={cn(
                "inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-sm font-bold",
                tone === "warm" && "bg-[var(--gauge-warm-soft)] text-[var(--gauge-warm)]",
                tone === "cool" && "bg-[var(--gauge-cool-soft)] text-[var(--gauge-cool)]",
                tone === null && "bg-surface text-muted-foreground",
              )}
              data-testid="valuation-gauge-position"
            >
              <span aria-hidden className="size-1.5 rounded-full bg-current" />
              {position}
            </span>
          )}
          {forward && (
            <span
              className="inline-flex items-center gap-1.5 rounded-full bg-surface px-3 py-1 text-sm font-bold ring-1 ring-hairline"
              data-testid="valuation-gauge-forward"
              data-side={forward.side}
              data-source={forward.source ?? "sapio"}
            >
              <svg width="18" height="10" viewBox="0 0 18 10" aria-hidden className="shrink-0">
                <line
                  x1="1"
                  y1="5"
                  x2="11"
                  y2="5"
                  stroke="currentColor"
                  strokeWidth="2"
                  strokeLinecap="round"
                  strokeDasharray="2 3"
                />
                <circle cx="14" cy="5" r="3" fill="none" stroke="currentColor" strokeWidth="1.6" />
              </svg>
              <span>
                {name} {forwardLabel(forward)} <Num>{formatRatio2(forward.value)}</Num>
              </span>
            </span>
          )}
          {pinnedText && <span className="text-xs text-muted-foreground">{pinnedText}</span>}
          <p aria-live="polite" className="min-h-0 text-center text-xs text-muted-foreground">
            {selected !== null && selectedSector && (
              <>
                بازهٔ {toPersianDigits(selected + 1)}
                {selectedSector.from && selectedSector.to && (
                  <>
                    {" "}
                    · <Num>{formatRatio2(selectedSector.from)}</Num> تا{" "}
                    <Num>{formatRatio2(selectedSector.to)}</Num>
                  </>
                )}
                {selectedSector.percentage != null && (
                  <>
                    {" "}
                    · <Num>{formatExactDecimal(selectedSector.percentage)}٪</Num> روزها
                  </>
                )}
              </>
            )}
          </p>
        </div>
        <div className="grid min-w-0 gap-4">
          <Tiles>
            {[
              <Tile
                key="avg"
                label="میانگین تاریخی"
                value={<Num>{formatRatio2(ratio.average)}</Num>}
                note="مرکز گیج"
              />,
              distance ? (
                <Tile
                  key="distance"
                  label="فاصله تا میانگین"
                  value={<Num>{formatPercent(distance, true)}</Num>}
                  note={distance.startsWith("-") ? "ارزان‌تر از میانگین" : "گران‌تر از میانگین"}
                  tone={distance.startsWith("-") ? "cool" : "warm"}
                />
              ) : null,
              insights?.daysBelowBandPercent ? (
                <Tile
                  key="below"
                  label="روزهای ارزان‌تر"
                  value={<Num>{formatPercent(insights.daysBelowBandPercent)}</Num>}
                  note="زیر بازهٔ امروز"
                />
              ) : null,
            ]}
          </Tiles>
          <HistoryStrip card={card} activeIndex={activeIndex} onSelectBand={setSelected} />
        </div>
      </div>
      <RatioDetails card={card} />
      <p className="mt-2 flex items-center gap-1.5 text-xs leading-5 text-muted-foreground">
        <svg
          width="14"
          height="14"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          aria-hidden
        >
          <path d="M12 3l7 3v5c0 4.5-3 8.5-7 10-4-1.5-7-5.5-7-10V6z" />
          <path d="M9 12l2 2 4-4" />
        </svg>
        {name} یک نسبت بنیادی است، نه توصیهٔ خرید یا فروش.
      </p>
    </CardShell>
  );
}

// ---------------------------------------------------------------------------------------------------------------
// Equilibrium price band

function EquilibriumCard({ card, symbol, companyName, showCompany }: CardProps) {
  const range = card.equilibrium!;
  const quote = range.quote ?? null;
  const position = range.position ? EQUILIBRIUM_POSITION_TEXT[range.position] : null;
  const needle = card.geometry?.needle ?? null;
  // The server's piecewise angle (lower 0°, midpoint 90°, upper 180°) maps straight onto the track.
  const at = needle ? Math.min(1, Math.max(0, needle.angle / 180)) : null;
  const pinned = needle !== null && needle.side !== "Inside";
  const insights = card.insights;
  const summary =
    quote && position
      ? `بازهٔ قیمت تعادلی ${symbol}: قیمت بازار ${formatRial0(quote.price)} ریال، ${position}`
      : `بازهٔ قیمت تعادلی ${symbol}: از ${formatRial0(range.lower)} تا ${formatRial0(range.upper)} ریال؛ قیمت بازار در دسترس نیست`;
  const bubbleShift = at === null ? "-50%" : at < 0.15 ? "-12%" : at > 0.85 ? "-88%" : "-50%";

  return (
    <CardShell card={card} symbol={symbol} companyName={companyName} showCompany={showCompany}>
      <div className="grid gap-4">
        {card.geometry ? (
          <div
            dir="ltr"
            role="img"
            aria-label={summary}
            className="relative px-3 pb-12 pt-14"
            data-testid="valuation-gauge-band-track"
          >
            {at !== null && quote && (
              <div
                className="absolute top-1.5 grid justify-items-center gap-1"
                style={{
                  left: `calc(0.75rem + (100% - 1.5rem) * ${at})`,
                  transform: "translateX(-50%)",
                }}
                data-testid="valuation-gauge-needle"
                data-side={needle!.side}
              >
                <span
                  dir="rtl"
                  className="whitespace-nowrap rounded-lg bg-foreground px-2.5 py-1 text-sm font-extrabold text-card"
                  style={{ transform: `translateX(calc(${bubbleShift} + 50%))` }}
                >
                  <Num>{formatRial0(quote.price)}</Num> ریال
                </span>
                <span
                  className={cn(
                    "h-5 w-0.5 bg-foreground",
                    pinned && "bg-transparent border-s-2 border-dashed border-foreground",
                  )}
                />
                <span className="-mt-1 size-4 rounded-full border-[3px] border-foreground bg-card" />
              </div>
            )}
            <div
              className="relative h-4 rounded-full"
              style={{
                background: "linear-gradient(90deg, var(--gauge-eq-low), var(--gauge-eq-high))",
              }}
            >
              <span className="absolute -inset-y-2 left-1/2 w-0.5 -translate-x-1/2 rounded bg-foreground" />
            </div>
            <div className="absolute inset-x-3 bottom-1 h-9 text-xs text-muted-foreground">
              {(
                [
                  [0, "حد پایین", range.lower, "translate-x-0 items-start"],
                  [50, "میانه", range.midpoint, "-translate-x-1/2 items-center"],
                  [100, "حد بالا", range.upper, "-translate-x-full items-end"],
                ] as const
              ).map(([left, label, value, align]) => (
                <span
                  key={label}
                  dir="rtl"
                  className={cn("absolute grid leading-4", align)}
                  style={{ left: `${left}%` }}
                >
                  {label}
                  <Num className="text-sm font-bold text-foreground">{formatRial0(value)}</Num>
                </span>
              ))}
            </div>
          </div>
        ) : (
          <p className="text-sm">
            حد پایین <Num>{formatRial0(range.lower)}</Num>، میانه{" "}
            <Num>{formatRial0(range.midpoint)}</Num>، حد بالا <Num>{formatRial0(range.upper)}</Num>{" "}
            ریال
          </p>
        )}
        {quote ? (
          <Tiles>
            {[
              position ? <Tile key="position" label="جایگاه" value={position} /> : null,
              insights?.priceVsMidpointPercent ? (
                <Tile
                  key="mid"
                  label="تا میانهٔ بازه"
                  value={<Num>{formatPercent(insights.priceVsMidpointPercent, true)}</Num>}
                  note={`نسبت به ${formatRial0(range.midpoint)}`}
                  tone={insights.priceVsMidpointPercent.startsWith("-") ? "cool" : "warm"}
                />
              ) : null,
              insights?.priceVsLowerPercent ? (
                <LowerBoundTile
                  key="low"
                  value={insights.priceVsLowerPercent}
                  lower={range.lower}
                />
              ) : null,
            ]}
          </Tiles>
        ) : (
          <p className="text-sm text-muted-foreground">
            قیمت بازار در دسترس نیست؛ فقط بازه نمایش داده شده است
          </p>
        )}
        {quote && (
          <p
            className="text-xs leading-5 text-muted-foreground"
            data-testid="valuation-gauge-quote-source"
          >
            {/* The owner asked (2026-11-26) for no provider name here; the payload keeps it. */}
            قیمت: {quoteSourceText(quote.sourceLabel)} ·{" "}
            <Num>{formatTehranDateTime(quote.asOf)}</Num>
          </p>
        )}
      </div>
    </CardShell>
  );
}

function LowerBoundTile({ value, lower }: { value: string; lower: string }) {
  const { label, percent } = lowerBoundComparison(value);
  return <Tile label={label} value={<Num>{percent}</Num>} note={`نسبت به ${formatRial0(lower)}`} />;
}

function UnavailableCard({ card, symbol, companyName, showCompany }: CardProps) {
  return (
    <CardShell card={card} symbol={symbol} companyName={companyName} showCompany={showCompany}>
      <p className="text-sm leading-7 text-muted-foreground">{unavailableText(card)}</p>
    </CardShell>
  );
}

interface CardProps {
  card: ValuationGaugeCard;
  symbol: string;
  companyName?: string | null;
  showCompany: boolean;
}

function GaugeCard(props: CardProps) {
  const { card } = props;
  if (!isDisplayable(card)) return <UnavailableCard {...props} />;
  if (card.kind === "Equilibrium" && card.equilibrium) return <EquilibriumCard {...props} />;
  if (card.ratio) return <RatioCard {...props} />;
  return <UnavailableCard {...props} />;
}

// ---------------------------------------------------------------------------------------------------------------
// Answer

const TAB_LABEL: Record<ValuationGaugeCard["kind"], string> = {
  Pe: "P/E",
  Ps: "P/S",
  Equilibrium: "قیمت تعادلی",
};

/**
 * The cards of one answer in the requested order (P/E, P/S, equilibrium). A single card stands alone. Several cards
 * share one heading: on a wide message P/E and P/S sit side by side with the equilibrium band below; on a narrow one
 * an iOS-style segmented control shows one card at a time.
 */
export function ValuationGaugeCards({ data }: { data: ValuationGaugeCardsResult }) {
  const multiple = data.cards.length > 1;
  const [selected, setSelected] = useState(0);
  const baseId = useId();

  if (!multiple) {
    const card = data.cards[0];
    return (
      <div className="min-w-0" dir="rtl" data-testid="valuation-gauge-cards">
        {card && (
          <GaugeCard card={card} symbol={data.symbol} companyName={data.companyName} showCompany />
        )}
      </div>
    );
  }

  const onKeyDown = (event: KeyboardEvent<HTMLDivElement>) => {
    // Right-to-left: the left arrow moves to the next tab.
    const step = event.key === "ArrowLeft" ? 1 : event.key === "ArrowRight" ? -1 : 0;
    if (step === 0) return;
    event.preventDefault();
    const next = (selected + step + data.cards.length) % data.cards.length;
    setSelected(next);
    document.getElementById(`${baseId}-tab-${next}`)?.focus();
  };

  return (
    <div className="@container min-w-0 space-y-3" dir="rtl" data-testid="valuation-gauge-cards">
      <h2 className="flex flex-wrap items-baseline gap-x-2 text-base font-bold leading-7">
        گیج‌های ارزش‌گذاری <bdi>{data.symbol}</bdi>
        {data.companyName && " "}
        {data.companyName && (
          <span className="text-sm font-normal text-muted-foreground">{data.companyName}</span>
        )}
      </h2>
      <div
        role="tablist"
        aria-label="نوع گیج"
        onKeyDown={onKeyDown}
        className="relative grid h-11 rounded-xl bg-surface-2 p-[3px] @[720px]:hidden"
        style={{ gridTemplateColumns: `repeat(${data.cards.length}, minmax(0, 1fr))` }}
        data-testid="valuation-gauge-tabs"
      >
        <span
          aria-hidden
          className="absolute inset-y-[3px] rounded-[9px] bg-card shadow-sm ring-1 ring-hairline transition-[inset-inline-start] duration-200 motion-reduce:transition-none"
          style={{
            width: `calc((100% - 6px) / ${data.cards.length})`,
            insetInlineStart: `calc(3px + (100% - 6px) / ${data.cards.length} * ${selected})`,
          }}
        />
        {data.cards.map((card, index) => (
          <button
            key={card.kind}
            id={`${baseId}-tab-${index}`}
            type="button"
            role="tab"
            aria-selected={index === selected}
            aria-controls={`${baseId}-panel-${index}`}
            tabIndex={index === selected ? 0 : -1}
            onClick={() => setSelected(index)}
            className="relative z-10 rounded-[9px] text-sm font-semibold focus-visible:outline-2 focus-visible:outline-ring"
          >
            {TAB_LABEL[card.kind]}
          </button>
        ))}
      </div>
      <div className="grid gap-3 @[720px]:grid-cols-2">
        {data.cards.map((card, index) => (
          <div
            key={card.kind}
            id={`${baseId}-panel-${index}`}
            role="tabpanel"
            aria-labelledby={`${baseId}-tab-${index}`}
            className={cn(
              "min-w-0",
              index !== selected && "hidden @[720px]:block",
              card.kind === "Equilibrium" && "@[720px]:col-span-2",
            )}
          >
            <GaugeCard
              card={card}
              symbol={data.symbol}
              companyName={data.companyName}
              showCompany={false}
            />
          </div>
        ))}
      </div>
    </div>
  );
}
