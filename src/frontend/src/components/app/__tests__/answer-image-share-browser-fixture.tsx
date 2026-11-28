import { createRoot } from "react-dom/client";
import type { ReactNode } from "react";
import {
  comparisonImageFileName,
  gaugeImageFileName,
  productMixImageFileName,
} from "@/lib/chat/answer-image";
import type { ValuationGaugeCardsResult } from "@/lib/chat/valuation-gauges";
import { AnswerSharePicture } from "../answer-share-frame";
import { AnswerActions } from "../answer-actions";
import { AnswerDensityContext } from "../answer-density";
import { MarkdownMessage } from "../markdown-message";
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

// Feature 125 change «comparison-answer-image-share»: ?fixture=comparison|comparison-four|comparison-large renders an
// own-industry comparison answer; &density=compact is the phone workspace, which shows cards on screen.
const SYMBOLS = ["کچاد", "کگهر", "کنور", "کگل"];
function comparisonMessage(rows: number, sixColumns: boolean): string {
  const members = Array.from({ length: rows }, (_, index) => {
    const symbol = index < SYMBOLS.length ? SYMBOLS[index] : `نماد${index + 1}`;
    const metrics = `| ${symbol} | ${70 + index * 7}٫۳۱٪ | ${40 + index}٫۵٪ | ${45 + index * 2}٫۵۷٪ |`;
    return sixColumns
      ? `${metrics} ${index + 1} از ${rows} | ${3440 + index * 110} ریال (روزانه، ۱۴۰۵/۰۸/۳۰)، تعادلی ${7232 + index * 90} ریال |`
      : metrics;
  });
  return [
    "### مقایسه نماد با هم‌گروهی‌ها",
    "**گروه صنعتی:** استخراج سنگ معدن های فلزی آهنی",
    `**اندازه گروه:** ${rows} نماد`,
    "",
    sixColumns
      ? "| نماد | P/E | P/S | قیمت به تعادلی | رتبه | قیمت و تاریخ معامله |"
      : "| نماد | P/E | P/S | قیمت به تعادلی |",
    sixColumns ? "|---|---:|---:|---:|---:|---|" : "|---|---:|---:|---:|",
    ...members,
    `| میانگین گروه | ۱۱۹٫۹۱٪ | ۵۵٫۲۷٪ | ۶۱٫۹۱٪ |${sixColumns ? " — | — |" : ""}`,
    "این اطلاعات توصیه به سرمایه گذاری نیست",
  ].join("\n");
}
// Feature 075 change «product-mix-answer-image-share»: ?fixture=mix|mix-long renders a product revenue mix answer.
const PRODUCTS = [
  "گندله بورس کالا",
  "گندله",
  "کلوخه سنگ آهن",
  "کنسانتره سنگ آهن",
  "سنگ آهن دانه بندی شده",
  "آهن اسفنجی",
  "کنسانتره بورس کالا",
  "میلگرد",
];
const persian = (value: string) => value.replace(/[0-9]/g, (digit) => "۰۱۲۳۴۵۶۷۸۹"[Number(digit)]);
function productMixMessage(rows: number, longNames: boolean): string {
  const products = Array.from({ length: rows }, (_, index) => {
    const name =
      index < PRODUCTS.length && !longNames
        ? PRODUCTS[index]
        : `${PRODUCTS[index % PRODUCTS.length]} صادراتی با عیار بالا و بسته‌بندی ویژه شماره ${persian(String(index + 1))}`;
    const sales = persian(
      (5_467_502_800_000 / (index + 1)).toLocaleString("en-US", { maximumFractionDigits: 0 }),
    );
    const share = persian((42.3 / (index + 1)).toFixed(1)).replace(".", "٫");
    return `| ${persian(String(index + 1))} | ${name} | ${sales} | ${share}٪ | ${index < 2 ? "✓" : ""} |`;
  });
  return [
    "### ترکیب درآمد محصولات — معدنی و صنعتی گهر زمین (کگهر)",
    "دوره: ۱۴۰۵/۰۷",
    "",
    "| ردیف | محصول | فروش (تومان) | سهم (٪) | غالب |",
    "|------|-------|------------|---------|------|",
    ...products,
  ].join("\n");
}

const comparisons: Record<string, { message: string; width: number; fileName: string }> = {
  comparison: {
    message: comparisonMessage(4, true),
    width: 800,
    fileName: comparisonImageFileName(),
  },
  "comparison-four": {
    message: comparisonMessage(4, false),
    width: 560,
    fileName: comparisonImageFileName(),
  },
  "comparison-large": {
    message: comparisonMessage(40, true),
    width: 800,
    fileName: comparisonImageFileName(),
  },
  mix: {
    message: productMixMessage(8, false),
    width: 640,
    fileName: productMixImageFileName("کگهر"),
  },
  "mix-long": {
    message: productMixMessage(25, true),
    width: 640,
    fileName: productMixImageFileName("کگهر"),
  },
};

const params = new URLSearchParams(location.search);
const fixtureName = params.get("fixture") ?? "single";
const comparison = comparisons[fixtureName];
const data = fixtures[comparison ? "single" : fixtureName];
const referenceWidth = Number(params.get("reference"));
const density = params.get("density") === "compact" ? "compact" : "default";

// The same picture source `message-list.tsx` builds for a gauge answer. The fixture renders the cards and the actions
// directly: the message list itself needs the app's server runtime, and its wiring is covered by the Vitest suite.
const image: { content: ReactNode; width: number; fileName: string } = comparison
  ? {
      content: <MarkdownMessage content={comparison.message} direction="rtl" />,
      width: comparison.width,
      fileName: comparison.fileName,
    }
  : {
      content: <ValuationGaugeCards data={data} />,
      width: data.cards.length > 1 ? 800 : 480,
      fileName: gaugeImageFileName(data.symbol),
    };

// A picture wider than the viewport overflows to the left in a right-to-left page, where it cannot be scrolled to or
// photographed. The reference page is left-to-right; the picture sets its own direction.
if (referenceWidth > 0) document.documentElement.dir = "ltr";

createRoot(document.getElementById("root")!).render(
  referenceWidth > 0 ? (
    <AnswerSharePicture width={referenceWidth}>{image.content}</AnswerSharePicture>
  ) : (
    <div className="flex min-h-dvh bg-background text-foreground">
      <main className="mx-auto w-full min-w-0 max-w-4xl flex-1 px-4 py-5">
        {comparison ? (
          <AnswerDensityContext.Provider value={density}>
            <div className="space-y-5 text-right" dir="rtl">
              <MarkdownMessage content={comparison.message} direction="rtl" />
              <AnswerActions text={comparison.message} image={image} />
              <div
                className="flex flex-wrap gap-2"
                role="group"
                aria-label="پیشنهادهای ادامه گفتگو"
              >
                <button type="button" data-testid="follow-up">
                  کگهر را با صنعت خودش مقایسه کن
                </button>
              </div>
            </div>
          </AnswerDensityContext.Provider>
        ) : (
          <div className="space-y-5">
            <ValuationGaugeCards data={data} />
            <AnswerActions text="P/E: 6.43" image={image} />
          </div>
        )}
      </main>
    </div>
  ),
);
