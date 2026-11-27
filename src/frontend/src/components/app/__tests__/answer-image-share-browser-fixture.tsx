import { createRoot } from "react-dom/client";
import { gaugeImageFileName } from "@/lib/chat/answer-image";
import type { ValuationGaugeCardsResult } from "@/lib/chat/valuation-gauges";
import { AnswerSharePicture } from "../answer-share-frame";
import { AnswerActions } from "../answer-actions";
import { ValuationGaugeCards } from "../valuation-gauge-cards";
import {
  equilibriumCard,
  forwardFixture,
  gaugeResult,
  ratioCard,
} from "./valuation-gauge-fixtures";
import "../../../styles.css";

// Feature 159 change «valuation-gauge-answer-image-share» browser fixture.
// ?fixture=single|all|forward renders a gauge answer with its actions.
// &reference=<width> renders the picture itself on screen instead, so a browser screenshot of it can be compared with
// the picture the app produces.
const fixtures: Record<string, ValuationGaugeCardsResult> = {
  single: gaugeResult([ratioCard("Pe")]),
  all: gaugeResult([
    ratioCard("Pe"),
    ratioCard("Ps", { current: "1.8312", needleAngle: 0, side: "PinnedBelow" }),
    equilibriumCard(),
  ]),
  forward: gaugeResult([
    ratioCard("Pe", { forward: forwardFixture() }),
    ratioCard("Ps", { forward: forwardFixture("pinnedBelow", "Ps") }),
    equilibriumCard(),
  ]),
};

const params = new URLSearchParams(location.search);
const data = fixtures[params.get("fixture") ?? "single"];
const referenceWidth = Number(params.get("reference"));

// The same picture source `message-list.tsx` builds for a gauge answer. The fixture renders the cards and the actions
// directly: the message list itself needs the app's server runtime, and its wiring is covered by the Vitest suite.
const image = {
  content: <ValuationGaugeCards data={data} />,
  width: data.cards.length > 1 ? 800 : 480,
  fileName: gaugeImageFileName(data.symbol),
};

// A picture wider than the viewport overflows to the left in a right-to-left page, where it cannot be scrolled to or
// photographed. The reference page is left-to-right; the picture sets its own direction.
if (referenceWidth > 0) document.documentElement.dir = "ltr";

createRoot(document.getElementById("root")!).render(
  referenceWidth > 0 ? (
    <AnswerSharePicture width={referenceWidth}>
      <ValuationGaugeCards data={data} />
    </AnswerSharePicture>
  ) : (
    <div className="flex min-h-dvh bg-background text-foreground">
      <main className="mx-auto w-full min-w-0 max-w-4xl flex-1 px-4 py-5">
        <div className="space-y-5">
          <ValuationGaugeCards data={data} />
          <AnswerActions text="P/E: 6.43" image={image} />
        </div>
      </main>
    </div>
  ),
);
