import { createRoot } from "react-dom/client";
import type { ValuationGaugeCardsResult } from "@/lib/chat/valuation-gauges";
import { ValuationGaugeCards } from "../valuation-gauge-cards";
import {
  equilibriumCard,
  gaugeResult,
  ratioCard,
  unavailableCard,
} from "./valuation-gauge-fixtures";
import "../../../styles.css";

// Feature 159 change «standalone-valuation-gauge-cards» browser fixture:
// ?fixture=single|all|range-only|stale|missing|pinned. The numbers are illustrative.
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
};

const params = new URLSearchParams(location.search);
const data = fixtures[params.get("fixture") ?? "single"];
const card = (
  <main className="mx-auto min-w-0 max-w-3xl flex-1 p-2 sm:p-4">
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
