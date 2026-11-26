import { createRoot } from "react-dom/client";
import type { ValuationGaugeCard, ValuationGaugeCardsResult } from "@/lib/chat/valuation-gauges";
import { ValuationGaugeCards } from "../valuation-gauge-cards";
import {
  equilibriumCard,
  gaugeResult,
  ratioCard,
  unavailableCard,
} from "./valuation-gauge-fixtures";
import "../../../styles.css";

// Feature 159 changes «standalone-valuation-gauge-cards» and «valuation-gauge-cards-visual-refresh» browser fixture:
// ?fixture=single|all|range-only|stale|missing|pinned|sharak|legacy. The numbers are illustrative except «sharak»,
// which carries the شاراک P/E values of the first production card (2026-11-26) and the figures the server derives.
const sharakEdges = [
  "2.1059510085",
  "3.3487316736139543",
  "4.614798895536109",
  "5.880866117458263",
  "7.787239223166494",
  "9.693612328874726",
  "59.613521843",
];
const sharakPercentages = ["7.2", "25.7", "24.5", "20.1", "11.0", "11.5"];
const sharakCounts = [414, 1483, 1412, 1161, 634, 662];
const sharak: ValuationGaugeCard = {
  ...ratioCard("Pe"),
  ratio: {
    current: "6.999044944",
    start: sharakEdges[0],
    min: sharakEdges[1],
    average: sharakEdges[3],
    max: sharakEdges[5],
    end: sharakEdges[6],
    dayCounts: sharakCounts,
    dayPercentages: sharakPercentages,
    providerStatus: 3,
    position: "AverageToMax",
    currentSide: "Inside",
  },
  geometry: {
    sectors: sharakPercentages.map((percentage, index) => ({
      order: index + 1,
      startAngle: index * 30,
      endAngle: index * 30 + 30,
      dayCount: sharakCounts[index],
      percentage,
      from: sharakEdges[index],
      to: sharakEdges[index + 1],
    })),
    markers: [
      { role: "start", angle: 0, value: sharakEdges[0] },
      { role: "min", angle: 30, value: sharakEdges[1] },
      { role: "average", angle: 90, value: sharakEdges[3] },
      { role: "max", angle: 150, value: sharakEdges[5] },
      { role: "end", angle: 180, value: sharakEdges[6] },
    ],
    needle: { angle: 107.597, value: "6.999044944", side: "Inside" },
  },
  insights: {
    distanceToAveragePercent: "19.0",
    currentBand: 4,
    daysBelowBandPercent: "57.4",
    daysInBandPercent: "20.1",
  },
};

const fixtures: Record<string, ValuationGaugeCardsResult> = {
  single: gaugeResult([ratioCard("Pe")]),
  all: gaugeResult([
    ratioCard("Pe"),
    ratioCard("Ps", { current: "1.8312", needleAngle: 0, side: "PinnedBelow" }),
    equilibriumCard(),
  ]),
  "range-only": gaugeResult([equilibriumCard(false)], "اخابر"),
  stale: gaugeResult([
    ratioCard("Pe", { current: null }),
    unavailableCard("Ps", "Stale", "stale_snapshot"),
    unavailableCard("Equilibrium"),
  ]),
  missing: gaugeResult([ratioCard("Ps", { current: null })], "کگهر"),
  pinned: gaugeResult([
    ratioCard("Pe", { current: "14250.75", needleAngle: 180, side: "PinnedAbove" }),
  ]),
  sharak: gaugeResult([sharak]),
  legacy: gaugeResult([ratioCard("Pe", { legacy: true })]),
};

const params = new URLSearchParams(location.search);
const data = fixtures[params.get("fixture") ?? "single"];
const card = (
  <main className="mx-auto w-full min-w-0 max-w-4xl flex-1 px-4 py-5">
    <ValuationGaugeCards data={data} />
  </main>
);

// ?sidebar=1 reproduces the desktop chat column: a 288 px sidebar and a 320 px context panel beside the answer.
createRoot(document.getElementById("root")!).render(
  <div className="flex min-h-dvh bg-background text-foreground">
    {params.get("sidebar") === "1" && <aside className="w-72 shrink-0 border-e border-hairline" />}
    {card}
    {params.get("sidebar") === "1" && <aside className="w-80 shrink-0 border-s border-hairline" />}
  </div>,
);
