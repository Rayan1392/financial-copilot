import { createRoot } from "react-dom/client";
import { StarterCardGrid } from "../starter-card-grid";
import { MobileStarterList } from "@/components/mobile/mobile-starter-list";
import { homeStarterPrompts, starterCoverage } from "@/lib/chat/starter-prompts";
import "../../../styles.css";

// Change «valuation-gauge-starter-card» browser fixture: ?view=desktop|mobile&gauge=1|0. The desktop view repeats the
// new-chat column of `/chat` (max-w-3xl, the coverage badge and sentence); the mobile view repeats `/m`.
const params = new URLSearchParams(location.search);
const prompts = homeStarterPrompts([], {
  valuationGaugeCards: params.get("gauge") !== "0",
}).prompts;
const coverage = starterCoverage(prompts);
const noop = () => {};

const view =
  params.get("view") === "mobile" ? (
    <div className="mx-auto max-w-xl px-4 py-4" data-testid="starter-root">
      <h2 className="mb-3 text-m-head font-bold text-foreground">چه چیزی را بررسی کنیم؟</h2>
      <MobileStarterList prompts={prompts} disabled={false} onSelect={noop} />
    </div>
  ) : (
    <div className="flex p-8">
      <div className="m-auto w-full max-w-3xl py-6 text-center md:py-10" data-testid="starter-root">
        {/* Same classes as `/chat`; its light-theme contrast is BUG-049, measured apart by the browser check. */}
        <div
          className="mb-5 inline-flex items-center rounded-full border border-emerald/20 bg-emerald-soft/60 px-3 py-1 text-xs font-medium text-emerald"
          data-known-defect="BUG-049"
        >
          {coverage.badge}
        </div>
        <p className="mx-auto mb-7 max-w-2xl text-sm leading-7 text-muted-foreground text-pretty">
          {coverage.sentence}
        </p>
        <StarterCardGrid prompts={prompts} disabled={false} onSelect={noop} />
      </div>
    </div>
  );

createRoot(document.getElementById("root")!).render(
  <div className="min-h-dvh bg-background text-foreground">{view}</div>,
);
