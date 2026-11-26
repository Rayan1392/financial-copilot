import type { ReactNode } from "react";
import {
  EQUILIBRIUM_POSITION_TEXT,
  KIND_TITLE,
  RATIO_POSITION_TEXT,
  bandPath,
  dialPoint,
  formatExactDecimal,
  formatTehranDateTime,
  isDisplayable,
  quoteSourceText,
  unavailableText,
  type ValuationGaugeCard,
  type ValuationGaugeCardsResult,
  type ValuationGaugeGeometry,
} from "@/lib/chat/valuation-gauges";
import { formatProviderDisplayName } from "@/lib/format/provider-display";
import { toPersianDigits } from "@/lib/format/persian";
import { cn } from "@/lib/utils";

/*
 * Feature 159 change «standalone-valuation-gauge-cards»: the P/E, P/S and equilibrium cards. Every number and angle
 * comes from the server; the dial is decorative for screen readers (role="img" with a one-line summary) and the fact
 * list under it carries every value and status as text, so nothing is conveyed by colour or position alone.
 */

// Tonal ramp from the cheap end (left) to the expensive end (right). Mid tones read on both the dark (default) and
// the `.light` theme; labels on the dial carry a background-coloured halo, so they keep their contrast on any band.
// The app theme is a class, not the OS setting, so no `dark:` variants here.
const RATIO_SECTOR_CLASSES = [
  "fill-emerald-600",
  "fill-emerald-500",
  "fill-emerald-300",
  "fill-rose-300",
  "fill-rose-500",
  "fill-rose-600",
];
const EQUILIBRIUM_SECTOR_CLASSES = ["fill-sky-300", "fill-sky-500"];

const STATE_LABEL: Record<ValuationGaugeCard["state"], string> = {
  Available: "کامل",
  Partial: "ناقص",
  NoData: "بدون داده",
  Stale: "قدیمی",
  TemporarilyUnavailable: "موقتاً در دسترس نیست",
};

function Num({ value, unit }: { value: string; unit?: string }) {
  return (
    <span className="tabular-nums font-fanum">
      <bdi>{formatExactDecimal(value)}</bdi>
      {unit && <span className="text-[11px] font-normal text-muted-foreground"> {unit}</span>}
    </span>
  );
}

function Fact({ label, children }: { label: string; children: ReactNode }) {
  return (
    <div className="flex flex-wrap items-baseline justify-between gap-x-3 gap-y-0.5">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="text-sm font-medium">{children}</dd>
    </div>
  );
}

function Dial({
  geometry,
  sectorClasses,
  label,
  showSectorPercentages,
}: {
  geometry: ValuationGaugeGeometry;
  sectorClasses: string[];
  label: string;
  showSectorPercentages: boolean;
}) {
  const needle = geometry.needle ?? null;
  const tip = needle ? dialPoint(needle.angle, 112) : null;
  const pinned = needle !== null && needle.side !== "Inside";
  return (
    <svg
      viewBox="-20 -16 340 186"
      className="mx-auto w-full max-w-[320px] text-foreground"
      role="img"
      aria-label={label}
      direction="ltr"
      data-testid="valuation-gauge-dial"
    >
      {geometry.sectors.map((sector, index) => {
        const middle = dialPoint((sector.startAngle + sector.endAngle) / 2, 103);
        return (
          <g key={sector.order}>
            <path
              d={bandPath(sector.startAngle, sector.endAngle)}
              className={cn(sectorClasses[index] ?? sectorClasses[0], "stroke-background")}
              strokeWidth="1.5"
            />
            {showSectorPercentages && sector.percentage != null && (
              <text
                x={middle.x}
                y={middle.y + 4}
                textAnchor="middle"
                fontSize="11"
                fontWeight="700"
                className="fill-foreground font-fanum"
                stroke="var(--background)"
                strokeWidth="2.5"
                paintOrder="stroke"
              >
                {formatExactDecimal(sector.percentage)}٪
              </text>
            )}
          </g>
        );
      })}
      {geometry.markers.map((marker) => {
        const outer = dialPoint(marker.angle, 128);
        const inner = dialPoint(marker.angle, 78);
        // The axis ends sit under the dial's feet; the other markers sit just outside the band.
        const atEnd = marker.angle <= 1 || marker.angle >= 179;
        const text = atEnd
          ? { x: dialPoint(marker.angle, 103).x, y: 160 }
          : dialPoint(marker.angle, 140);
        const emphasised = marker.role === "average" || marker.role === "midpoint";
        return (
          <g key={marker.role}>
            <line
              x1={inner.x}
              y1={inner.y}
              x2={outer.x}
              y2={outer.y}
              className="stroke-foreground"
              strokeWidth={emphasised ? 2 : 1}
              strokeDasharray={emphasised ? undefined : "3 2"}
            />
            <text
              x={text.x}
              y={text.y}
              textAnchor="middle"
              fontSize="11"
              fontWeight={emphasised ? 700 : 500}
              className="fill-foreground font-fanum"
              stroke="var(--background)"
              strokeWidth="2.5"
              paintOrder="stroke"
            >
              {formatExactDecimal(marker.value)}
            </text>
          </g>
        );
      })}
      {tip && (
        <line
          x1="150"
          y1="142"
          x2={tip.x}
          y2={tip.y}
          className="stroke-foreground"
          strokeWidth="3"
          strokeLinecap="round"
          strokeDasharray={pinned ? "5 3" : undefined}
          data-testid="valuation-gauge-needle"
        />
      )}
      <circle cx="150" cy="142" r="5" className="fill-foreground" />
    </svg>
  );
}

function CardShell({
  card,
  symbol,
  children,
}: {
  card: ValuationGaugeCard;
  symbol: string;
  children: ReactNode;
}) {
  return (
    <section
      aria-label={`${KIND_TITLE[card.kind]} ${symbol}`}
      data-testid={`valuation-gauge-card-${card.kind}`}
      data-state={card.state}
      className="@container min-w-0 space-y-3 rounded-xl bg-surface/60 p-3 ring-1 ring-hairline sm:p-4"
    >
      <header className="flex flex-wrap items-center justify-between gap-2">
        <h3 className="text-sm font-semibold leading-6">
          {KIND_TITLE[card.kind]}
          <span className="font-normal text-muted-foreground"> · {symbol}</span>
        </h3>
        <span className="rounded-full px-2 py-0.5 text-[11px] text-muted-foreground ring-1 ring-hairline">
          {STATE_LABEL[card.state]}
        </span>
      </header>
      {children}
      <p className="text-[11px] leading-5 text-muted-foreground">
        منبع: CyclicalWaves
        {card.fetchedAtUtc && (
          <>
            {" "}
            · زمان دریافت داده:{" "}
            <bdi className="font-fanum">{formatTehranDateTime(card.fetchedAtUtc)}</bdi>
          </>
        )}
      </p>
    </section>
  );
}

function RatioCard({ card, symbol }: { card: ValuationGaugeCard; symbol: string }) {
  const ratio = card.ratio!;
  const kindName = card.kind === "Pe" ? "P/E" : "P/S";
  const position = RATIO_POSITION_TEXT[ratio.position];
  const pinnedText =
    ratio.currentSide === "PinnedBelow"
      ? "؛ نشانگر روی ابتدای محور نگه داشته شده است"
      : ratio.currentSide === "PinnedAbove"
        ? "؛ نشانگر روی انتهای محور نگه داشته شده است"
        : "";
  const summary = ratio.current
    ? `${kindName} ${symbol}: نسبت فعلی ${formatExactDecimal(ratio.current)}، ${position}${pinnedText}`
    : `${kindName} ${symbol}: نسبت فعلی در دسترس نیست؛ فقط محور تاریخی نمایش داده شده است`;
  return (
    <CardShell card={card} symbol={symbol}>
      <div className="grid gap-3 @xl:grid-cols-[minmax(0,300px)_minmax(0,1fr)] @xl:items-center @xl:gap-6">
        {card.geometry && (
          <Dial
            geometry={card.geometry}
            sectorClasses={RATIO_SECTOR_CLASSES}
            label={summary}
            showSectorPercentages
          />
        )}
        <dl className="space-y-1.5">
          <Fact label="نسبت فعلی">
            {ratio.current ? <Num value={ratio.current} /> : "در دسترس نیست"}
          </Fact>
          <Fact label="جایگاه">
            {position}
            {pinnedText && <span className="text-xs text-muted-foreground">{pinnedText}</span>}
          </Fact>
          <Fact label="حد پایین · میانگین · حد بالا">
            <Num value={ratio.min} /> · <Num value={ratio.average} /> · <Num value={ratio.max} />
          </Fact>
          <Fact label="محور">
            از <Num value={ratio.start} /> تا <Num value={ratio.end} />
          </Fact>
        </dl>
      </div>
      {ratio.dayPercentages && ratio.dayPercentages.length > 0 && (
        <table className="w-full text-xs">
          <caption className="pb-1 text-start text-xs text-muted-foreground">
            سهم روزهای تاریخی در شش بازه (از کم به زیاد)
          </caption>
          <thead className="sr-only">
            <tr>
              <th scope="col">بازه</th>
              <th scope="col">روز</th>
              <th scope="col">درصد</th>
            </tr>
          </thead>
          <tbody className="tabular-nums font-fanum">
            {ratio.dayPercentages.map((percentage, index) => (
              <tr key={index} className="border-t border-hairline">
                <th scope="row" className="py-1 text-start font-normal text-muted-foreground">
                  بازهٔ {toPersianDigits(index + 1)}
                </th>
                <td className="py-1">{toPersianDigits(ratio.dayCounts[index] ?? 0)} روز</td>
                <td className="py-1 text-end">{formatExactDecimal(percentage)}٪</td>
              </tr>
            ))}
          </tbody>
        </table>
      )}
    </CardShell>
  );
}

function EquilibriumCard({ card, symbol }: { card: ValuationGaugeCard; symbol: string }) {
  const range = card.equilibrium!;
  const quote = range.quote ?? null;
  const position = range.position ? EQUILIBRIUM_POSITION_TEXT[range.position] : null;
  const summary =
    quote && position
      ? `بازهٔ قیمت تعادلی ${symbol}: قیمت بازار ${formatExactDecimal(quote.price)} ریال، ${position}`
      : `بازهٔ قیمت تعادلی ${symbol}: از ${formatExactDecimal(range.lower)} تا ${formatExactDecimal(range.upper)} ریال؛ قیمت بازار در دسترس نیست`;
  return (
    <CardShell card={card} symbol={symbol}>
      <div className="grid gap-3 @xl:grid-cols-[minmax(0,300px)_minmax(0,1fr)] @xl:items-center @xl:gap-6">
        {card.geometry && (
          <Dial
            geometry={card.geometry}
            sectorClasses={EQUILIBRIUM_SECTOR_CLASSES}
            label={summary}
            showSectorPercentages={false}
          />
        )}
        <dl className="space-y-1.5">
          <Fact label="حد پایین · میانه · حد بالا">
            <Num value={range.lower} /> · <Num value={range.midpoint} /> ·{" "}
            <Num value={range.upper} unit="ریال" />
          </Fact>
          <Fact label="قیمت بازار">
            {quote ? (
              <Num value={quote.price} unit="ریال" />
            ) : (
              <span className="text-xs font-normal text-muted-foreground">
                در دسترس نیست؛ فقط بازه نمایش داده شده است
              </span>
            )}
          </Fact>
          {position && <Fact label="جایگاه">{position}</Fact>}
        </dl>
      </div>
      {quote && (
        <p
          className="text-[11px] leading-5 text-muted-foreground"
          data-testid="valuation-gauge-quote-source"
        >
          قیمت: {quoteSourceText(quote.sourceLabel)} ·{" "}
          {formatProviderDisplayName(quote.providerName)} ·{" "}
          <bdi className="font-fanum">{formatTehranDateTime(quote.asOf)}</bdi>
        </p>
      )}
    </CardShell>
  );
}

function UnavailableCard({ card, symbol }: { card: ValuationGaugeCard; symbol: string }) {
  return (
    <CardShell card={card} symbol={symbol}>
      <p className="text-xs leading-6 text-muted-foreground">{unavailableText(card)}</p>
    </CardShell>
  );
}

/** The cards of one answer in the requested order (P/E, P/S, equilibrium). */
export function ValuationGaugeCards({ data }: { data: ValuationGaugeCardsResult }) {
  const all = data.cards.length > 1;
  return (
    <div className="min-w-0 space-y-2" dir="rtl" data-testid="valuation-gauge-cards">
      <h2 className="text-sm font-semibold leading-7">
        {all ? "گیج‌های ارزش‌گذاری" : KIND_TITLE[data.cards[0]?.kind ?? "Pe"]}{" "}
        <bdi>{data.symbol}</bdi>
        {data.companyName && (
          <span className="font-normal text-muted-foreground"> · {data.companyName}</span>
        )}
      </h2>
      <div className="grid gap-3">
        {data.cards.map((card) => {
          if (!isDisplayable(card))
            return <UnavailableCard key={card.kind} card={card} symbol={data.symbol} />;
          if (card.kind === "Equilibrium" && card.equilibrium)
            return <EquilibriumCard key={card.kind} card={card} symbol={data.symbol} />;
          if (card.ratio) return <RatioCard key={card.kind} card={card} symbol={data.symbol} />;
          return <UnavailableCard key={card.kind} card={card} symbol={data.symbol} />;
        })}
      </div>
    </div>
  );
}
