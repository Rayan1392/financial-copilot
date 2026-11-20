import { useState } from "react";
import { createRoot } from "react-dom/client";
import type { GroundedAnalysisHint } from "@/lib/chat.functions";
import type { AnalysisChoice } from "@/lib/chat/analysis-choice";
import { AnalysisOfferCard, AnalysisSettingsBar } from "../analysis-settings";
import { MarkdownMessage } from "../markdown-message";
import { withInlineSources } from "@/lib/chat/external-sources";
import "../../../styles.css";

// Change «sol-deep-analysis-web-search» browser fixture: ?theme=light|dark&compact=1. Prices are illustrative.
const params = new URLSearchParams(window.location.search);
if (params.get("theme") === "light") document.documentElement.classList.add("light");
const compact = params.get("compact") === "1";

const hint = (sufficientBalance: boolean): GroundedAnalysisHint => ({
  available: true,
  pricingVersion: "v1",
  prices: [
    { depth: "simple", webSearch: false, credits: 8 },
    { depth: "simple", webSearch: true, credits: 12 },
    { depth: "deep", webSearch: false, credits: 16 },
    { depth: "deep", webSearch: true, credits: 25 },
  ],
  defaultDepth: "deep",
  defaultWebSearch: true,
  unlimited: false,
  offer: {
    offerId: "o1",
    symbol: "فولاد",
    expiresAt: "2026-11-20T10:00:00Z",
    sufficientBalance,
    confirmActionId: "ga1:o1:confirm",
    cancelActionId: "ga1:o1:cancel",
  },
});

function Fixture() {
  const [choice, setChoice] = useState<AnalysisChoice>({ depth: "deep", webSearch: true });
  return (
    <main className="min-h-screen bg-background p-4 text-foreground" dir="rtl">
      <div className="mx-auto max-w-3xl space-y-6">
        <div data-testid="offer">
          <AnalysisOfferCard
            hint={hint(true)}
            choice={choice}
            onChange={setChoice}
            onConfirm={() => {}}
            onCancel={() => {}}
            creditHref="/profile"
            compact={compact}
          />
        </div>
        <div data-testid="offer-short">
          <AnalysisOfferCard
            hint={hint(false)}
            choice={choice}
            onChange={setChoice}
            onConfirm={() => {}}
            onCancel={() => {}}
            creditHref="/profile"
            compact={compact}
          />
        </div>
        <div data-testid="sources">
          <MarkdownMessage
            content={withInlineSources(
              "- فروش شرکت در فصل اخیر افزایش یافته است [W1].\n" +
                "- قیمت جهانی ورق فولادی در همین دوره کاهش یافته است [W2] [W1].\n\n**منابع**\n- [W1] ...",
              [
                {
                  id: "W1",
                  title: "گزارش فعالیت ماهانه فولاد مبارکه اصفهان در کدال",
                  url: "https://www.codal.ir/Reports/Decision.aspx?LetterSerial=abcdefghijklmnopqrstuvwxyz0123456789",
                  publishedAt: "1405/08/01",
                },
                {
                  id: "W2",
                  title: "Reuters",
                  url: "https://www.reuters.com/markets/commodities/steel",
                  publishedAt: "1405/08/02",
                },
              ],
            )}
          />
        </div>
        <div data-testid="bar" className="rounded-2xl bg-surface ring-1 ring-hairline">
          <AnalysisSettingsBar
            hint={hint(true)}
            choice={choice}
            onChange={setChoice}
            compact={compact}
          />
          <div className="h-12 px-4 py-3 text-sm text-muted-foreground">پرسش خود را بنویسید</div>
        </div>
      </div>
    </main>
  );
}

createRoot(document.getElementById("root")!).render(<Fixture />);
