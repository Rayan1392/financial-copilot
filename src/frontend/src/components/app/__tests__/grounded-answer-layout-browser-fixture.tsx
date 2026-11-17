// Feature 163 change «answer-readability»: visual fixture for the Markdown answer typography (OQ-102: every answer).
import { createRoot } from "react-dom/client";
import { MarkdownMessage } from "../markdown-message";
import "../../../styles.css";

const params = new URLSearchParams(location.search);
if (params.get("theme") === "light") document.documentElement.classList.add("light");

// The layout the new prompt asks for, after the server-side safety net (thousands separators).
const groundedAnswer = [
  "بر اساس صورت سود و زیان فصلی فولاد، **سودآوری در فصل ۲ ۱۴۰۵ نسبت به فصل ۱ ۱۴۰۵ به‌طور چشمگیری بهتر شده است.**",
  "",
  "- **فروش:** به ۱٬۶۷۵٬۲۵۵٬۳۸۵ میلیون ریال رسید؛ **رشد ۱۰۰٫۸ درصد**.",
  "- **حاشیه‌ها:** حاشیه سود ناخالص از ۲۷٫۹ به **۳۰٫۹ درصد** و حاشیه عملیاتی از ۲۳٫۷ به **۲۸٫۸ درصد** رسید.",
  "- **سود خالص:** ۴۷۱٬۲۷۲٬۷۹۳ میلیون ریال؛ **رشد ۲۹۱٫۶ درصد**. سود هر سهم از ۶۲ به **۲۴۴ ریال** رسید.",
  "- **اقلام غیرعملیاتی:** از منفی ۵٬۸۱۲٬۷۶۸ به ۱۲۴٬۵۱۹٬۴۷۹ میلیون ریال مثبت رسید و در رشد سود خالص سهم داشت.",
  "",
  "این بررسی، ارزش‌گذاری یا توصیه سرمایه‌گذاری نیست.",
  "",
  "می‌خواهید روند فروش ماهانه، صورت وضعیت مالی یا صورت جریان وجوه نقد فولاد را هم بررسی کنم؟",
].join("\n");

// Another answer type, to check that the shared typography does not break it.
const tableAnswer = [
  "نتیجه جستجو برای **P/E زیر ۵**:",
  "",
  "| نماد | P/E | P/S |",
  "|---|---|---|",
  "| فولاد | ۴٫۲ | ۱٫۱ |",
  "| فملی | ۴٫۸ | ۱٫۴ |",
].join("\n");

function Bubble({ testId, content }: { testId: string; content: string }) {
  return (
    <section data-testid={testId} className="rounded-2xl bg-surface/40 p-4 ring-1 ring-hairline">
      <MarkdownMessage content={content} direction="rtl" />
    </section>
  );
}

createRoot(document.getElementById("root")!).render(
  <main className="mx-auto max-w-3xl space-y-6 px-4 py-6" dir="rtl">
    <Bubble testId="grounded-answer" content={groundedAnswer} />
    <Bubble testId="table-answer" content={tableAnswer} />
  </main>,
);
