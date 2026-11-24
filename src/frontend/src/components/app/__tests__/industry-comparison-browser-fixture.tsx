// Feature 125 change «responsive-comparison-table»: the own-industry comparison in the chat answer column, with the
// three follow-up actions, for width, theme, sidebar and long-value checks. The wrappers and the action group copy
// the assistant block markup of `message-list.tsx` (MessageList itself needs the TanStack Start server runtime).
import { createRoot } from "react-dom/client";
import { AnswerDensityContext } from "../answer-density";
import { MarkdownMessage } from "../markdown-message";
import "../../../styles.css";

const params = new URLSearchParams(location.search);
const density = params.get("density") === "compact" ? "compact" : "default";
const theme = params.get("theme") === "light" ? "light" : "dark";
const sidebar = params.get("sidebar") === "open";
const long = params.get("long") === "1";
document.documentElement.classList.toggle("light", theme === "light");
document.documentElement.classList.toggle("dark", theme === "dark");
document.documentElement.style.colorScheme = theme;

const first = long ? "وبملت‌پارس‌گسترش‌صنایع‌معدنی" : "کچاد";
const firstPrice = long
  ? "۱۲۳۴۵۶۷۸۹۰۱۲ ریال (درون‌روزی، ۱۴۰۵/۰۹/۰۳)، تعادلی ۹۸۷۶۵۴۳۲۱۰۹٫۱۷ ریال"
  : "۳۴۴۰ ریال (روزانه، ۱۴۰۵/۰۸/۳۰)، تعادلی ۷۲۳۲٫۱۷ ریال";
const message = [
  "### مقایسه نماد با هم‌گروهی‌ها",
  "**گروه صنعتی:** استخراج سنگ معدن های فلزی آهنی",
  "**اندازه گروه:** ۴ نماد",
  "",
  "| نماد | P/E | P/S | قیمت به تعادلی | رتبه | قیمت و تاریخ معامله |",
  "|---|---:|---:|---:|---:|---|",
  `| ${first} | ${long ? "۱۲۳۴۵۶۷٫۳۱٪" : "۱۱۲٫۳۱٪"} | ۵۳٫۵٪ | ۴۷٫۵۷٪ | ۱ از ۴ | ${firstPrice} |`,
  "| کگهر | ۷۱٫۱۶٪ | ۴۱٫۶٪ | ۶۹٫۹٪ | ۲ از ۴ | ۱۰۵۰۰ ریال (روزانه، ۱۴۰۵/۰۸/۳۰)، تعادلی ۱۵۰۲۱٫۱۳ ریال |",
  "| کنور | ۷۹٫۶٪ | ۶۷٫۸۴٪ | ۷۴٫۳۹٪ | ۳ از ۴ | ۵۶۳۰ ریال (روزانه، ۱۴۰۵/۰۸/۳۰)، تعادلی ۷۵۶۸٫۴۲ ریال |",
  "| کگل | ۲۱۶٫۵۷٪ | ۵۸٫۱۵٪ | ۵۵٫۷۷٪ | ۴ از ۴ | ۴۱۲۰ ریال (روزانه، ۱۴۰۵/۰۸/۳۰)، تعادلی ۷۳۸۶٫۹۷ ریال |",
  "| میانگین گروه | ۱۱۹٫۹۱٪ | ۵۵٫۲۷٪ | ۶۱٫۹۱٪ | — | — |",
  "این اطلاعات توصیه به سرمایه گذاری نیست",
].join("\n");

const actions = [first, "کگهر", "کنور"].map((symbol) => `${symbol} را با صنعت خودش مقایسه کن`);

createRoot(document.getElementById("root")!).render(
  <AnswerDensityContext.Provider value={density}>
    <div className="flex min-h-screen bg-background text-foreground" dir="rtl">
      {sidebar && (
        <aside
          className="w-72 shrink-0 border-e border-hairline bg-surface"
          data-testid="sidebar"
        />
      )}
      <main className="min-w-0 flex-1" data-testid="chat">
        <div
          className={
            density === "compact"
              ? "px-4 py-5 space-y-6 max-w-4xl mx-auto w-full min-w-0"
              : "p-6 md:p-8 space-y-10 max-w-4xl mx-auto w-full"
          }
        >
          <div className="flex gap-3" dir="rtl">
            <span className="size-8 shrink-0 rounded-full bg-emerald-soft" />
            <div className="flex-1 space-y-5 min-w-0 text-right" dir="rtl">
              <div className="space-y-1">
                <MarkdownMessage content={message} direction="rtl" />
              </div>
              <div
                className="flex flex-wrap gap-2 pt-1"
                role="group"
                aria-label="پیشنهادهای ادامه گفتگو"
              >
                {actions.map((label) => (
                  <button
                    key={label}
                    type="button"
                    className="px-3 py-1.5 rounded-full ring-1 ring-emerald/30 bg-emerald-soft text-[11px] text-emerald hover:bg-emerald/15 focus-visible:outline-none focus-visible:ring-2 transition"
                  >
                    {label}
                  </button>
                ))}
              </div>
            </div>
          </div>
        </div>
      </main>
    </div>
  </AnswerDensityContext.Provider>,
);
