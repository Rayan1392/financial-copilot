import type { ValuationGaugeCard, ValuationGaugeCardsResult } from "@/lib/chat/valuation-gauges";

/* Wire-shaped fixtures (camelCase JSON from the API) shared by the unit tests and the browser evidence page. */

const fetchedAtUtc = "2026-11-26T08:10:00Z";
const ratioSectors = [0, 1, 2, 3, 4, 5].map((index) => ({
  order: index + 1,
  startAngle: index * 30,
  endAngle: index * 30 + 30,
}));
// Exact sector edges: start, min, mid(min, average), average, mid(average, max), max, end.
const ratioEdges = ["2", "4", "5", "6", "7", "8", "10"];

export function ratioCard(
  kind: "Pe" | "Ps",
  overrides: {
    current?: string | null;
    needleAngle?: number | null;
    side?: "Inside" | "PinnedBelow" | "PinnedAbove";
    /** A card as stored by v1.29.2: no insights and no sector ranges. */
    legacy?: boolean;
  } = {},
): ValuationGaugeCard {
  const current = overrides.current === undefined ? "6.4291" : overrides.current;
  const needleAngle = overrides.needleAngle === undefined ? 102.873 : overrides.needleAngle;
  const side = overrides.side ?? "Inside";
  const counts = [40, 120, 300, 260, 90, 25];
  const percentages = ["4.8", "14.5", "36.1", "31.3", "10.8", "3.0"];
  const legacy = overrides.legacy ?? false;
  const inside = current !== null && side === "Inside";
  // Server figures for this fixture: (6.4291 − 6) / 6 = 7.2٪, band 4, 4.8 + 14.5 + 36.1 = 55.4٪ below, 31.3٪ within.
  const insights =
    legacy || current === null
      ? undefined
      : {
          distanceToAveragePercent:
            side === "PinnedAbove" ? "86.7" : side === "PinnedBelow" ? "-50.0" : "7.2",
          currentBand: inside ? 4 : null,
          daysBelowBandPercent: inside ? "55.4" : null,
          daysInBandPercent: inside ? "31.3" : null,
        };
  return {
    kind,
    state: current === null ? "Partial" : "Available",
    reasonCode: current === null ? "no_current_value" : "none",
    source: "CyclicalWavesV2",
    fetchedAtUtc,
    ratio: {
      current,
      start: "2",
      min: "4",
      average: "6",
      max: "8",
      end: "10",
      dayCounts: counts,
      dayPercentages: percentages,
      providerStatus: current === null ? null : 3,
      position:
        current === null
          ? "Unranked"
          : side === "PinnedBelow"
            ? "BelowMin"
            : side === "PinnedAbove"
              ? "AboveMax"
              : "AverageToMax",
      currentSide: current === null ? null : side,
    },
    equilibrium: null,
    geometry: {
      sectors: ratioSectors.map((sector, index) => ({
        ...sector,
        dayCount: counts[index],
        percentage: percentages[index],
        ...(legacy ? {} : { from: ratioEdges[index], to: ratioEdges[index + 1] }),
      })),
      markers: [
        { role: "start", angle: 0, value: "2" },
        { role: "min", angle: 30, value: "4" },
        { role: "average", angle: 90, value: "6" },
        { role: "max", angle: 150, value: "8" },
        { role: "end", angle: 180, value: "10" },
      ],
      needle:
        current === null || needleAngle === null
          ? null
          : { angle: needleAngle, value: current, side },
    },
    ...(insights ? { insights } : {}),
  };
}

export function equilibriumCard(withQuote = true): ValuationGaugeCard {
  return {
    kind: "Equilibrium",
    state: withQuote ? "Available" : "Partial",
    reasonCode: withQuote ? "none" : "quote_unavailable",
    source: "CyclicalWavesV2",
    fetchedAtUtc,
    ratio: null,
    equilibrium: {
      lower: "3800",
      midpoint: "4400",
      upper: "5000",
      quoteState: withQuote ? "Available" : "Unavailable",
      quote: withQuote
        ? {
            price: "4120",
            providerName: "NoavaranAmin",
            sourceLabel: "LatestDailyFallback",
            asOf: "2026-11-25T09:00:00Z",
            tradingDate: "2026-11-25",
          }
        : null,
      position: withQuote ? "LowerHalf" : null,
    },
    geometry: {
      sectors: [
        { order: 1, startAngle: 0, endAngle: 90, dayCount: null, percentage: null },
        { order: 2, startAngle: 90, endAngle: 180, dayCount: null, percentage: null },
      ],
      markers: [
        { role: "lower", angle: 0, value: "3800" },
        { role: "midpoint", angle: 90, value: "4400" },
        { role: "upper", angle: 180, value: "5000" },
      ],
      needle: withQuote ? { angle: 48, value: "4120", side: "Inside" } : null,
    },
    // (4120 − 4400) / 4400 = −6.4٪ and (4120 − 3800) / 3800 = 8.4٪.
    ...(withQuote
      ? { insights: { priceVsMidpointPercent: "-6.4", priceVsLowerPercent: "8.4" } }
      : {}),
  };
}

export function unavailableCard(
  kind: "Pe" | "Ps" | "Equilibrium",
  state: "NoData" | "Stale" | "TemporarilyUnavailable" = "NoData",
  reasonCode = "snapshot_missing",
): ValuationGaugeCard {
  return {
    kind,
    state,
    reasonCode,
    source: "CyclicalWavesV2",
    fetchedAtUtc: state === "Stale" ? "2026-11-20T08:10:00Z" : null,
    ratio: null,
    equilibrium: null,
    geometry: null,
  };
}

export function gaugeResult(
  cards: ValuationGaugeCard[],
  symbol = "شاراک",
  companyName = symbol === "اخابر"
    ? "مخابرات ایران"
    : symbol === "کگهر"
      ? "سنگ آهن گهرزمین"
      : "پتروشیمی شازند",
): ValuationGaugeCardsResult {
  return {
    version: 1,
    symbol,
    companyName,
    vendorTicker: symbol,
    requestedKinds: cards.map((card) => card.kind),
    cards,
    generatedAtUtc: "2026-11-26T09:00:00Z",
    maxFetchAgeHours: 48,
  };
}
