import { describe, it, expect } from "vitest";
import { fireEvent, render, waitFor } from "@testing-library/react";
import { MarkdownMessage } from "../markdown-message";

describe("MarkdownMessage", () => {
  it("renders bold text as <strong>", () => {
    const { container } = render(<MarkdownMessage content="**bold text**" />);
    const strong = container.querySelector("strong");
    expect(strong).not.toBeNull();
    expect(strong?.textContent).toBe("bold text");
  });

  it("renders italic text as <em>", () => {
    const { container } = render(<MarkdownMessage content="*italic text*" />);
    const em = container.querySelector("em");
    expect(em).not.toBeNull();
    expect(em?.textContent).toBe("italic text");
  });

  it("renders inline code as <code>", () => {
    const { container } = render(<MarkdownMessage content="`inline code`" />);
    const code = container.querySelector("code");
    expect(code).not.toBeNull();
    expect(code?.textContent).toBe("inline code");
  });

  it("renders fenced code block inside <pre>", () => {
    const { container } = render(<MarkdownMessage content={"```\nblock code here\n```"} />);
    const pre = container.querySelector("pre");
    expect(pre).not.toBeNull();
    expect(pre?.textContent).toContain("block code here");
  });

  it("renders GFM table with <table>, <th>, <td>", () => {
    const tableMarkdown = `| Symbol | PE |\n|--------|----|\n| SHBNDR | 7  |`;
    const { container } = render(<MarkdownMessage content={tableMarkdown} />);
    expect(container.querySelector("table")).not.toBeNull();
    expect(container.querySelector("th")?.textContent).toContain("Symbol");
    expect(container.querySelector("td")?.textContent).toContain("SHBNDR");
  });

  it("renders bullet list as <ul><li>", () => {
    const { container } = render(<MarkdownMessage content={"- item one\n- item two"} />);
    const ul = container.querySelector("ul");
    const items = container.querySelectorAll("li");
    expect(ul).not.toBeNull();
    expect(items).toHaveLength(2);
    expect(items[0].textContent).toBe("item one");
    expect(items[1].textContent).toBe("item two");
  });

  it("renders numbered list as <ol><li>", () => {
    const { container } = render(<MarkdownMessage content={"1. first\n2. second"} />);
    const ol = container.querySelector("ol");
    expect(ol).not.toBeNull();
    expect(container.querySelectorAll("li")).toHaveLength(2);
  });

  it("renders Persian + English mixed text with bold phrases", () => {
    const content = "نسبت **P/E نماد شبندر** برابر است با **7.88**.";
    const { container } = render(<MarkdownMessage content={content} />);
    const bolds = container.querySelectorAll("strong");
    expect(bolds).toHaveLength(2);
    expect(bolds[0].textContent).toBe("P/E نماد شبندر");
    expect(bolds[1].textContent).toBe("7.88");
  });

  it("keeps a Persian response RTL when its first sentence is English", () => {
    const content = "Found metric data for 0 symbol(s). 1 unresolved.\n\nتحلیل بنیادی فولاژ";
    const { container } = render(<MarkdownMessage content={content} direction="rtl" />);
    const wrapper = container.firstElementChild;
    const paragraphs = container.querySelectorAll("p");

    expect(wrapper?.getAttribute("dir")).toBe("rtl");
    expect(wrapper?.className).toContain("text-right");
    expect(paragraphs).toHaveLength(2);
    expect(paragraphs[0].className).toContain("[unicode-bidi:plaintext]");
  });

  it("formats financial-statement sentences as separate RTL, right-aligned blocks", () => {
    const content =
      "درآمد عملیاتی ۳ ماهه نسبت به دوره مشابه ۱۰٪ تغییر کرده است. ✅ سود/زیان ناخالص به ۲۰۰ میلیون ریال رسیده است.";
    const { container } = render(<MarkdownMessage content={content} />);
    const wrapper = container.firstElementChild;
    const paragraphs = container.querySelectorAll("p");

    expect(wrapper?.getAttribute("dir")).toBe("rtl");
    expect(wrapper?.className).toContain("text-right");
    expect(paragraphs).toHaveLength(2);
    expect(paragraphs[0].textContent).toContain("تغییر کرده است.");
    expect(paragraphs[1].textContent).toContain("سود/زیان ناخالص");
  });

  it("renders a quarterly financial table with 16 rows inside an RTL message", () => {
    const labels = [
      "فروش",
      "بهای تمام‌شده کالای فروش‌رفته",
      "سود ناخالص",
      "حاشیه سود ناخالص",
      "هزینه‌های عمومی و اداری",
      "سایر درآمدهای عملیاتی",
      "سود عملیاتی",
      "هزینه‌های مالی",
      "سایر درآمدها و هزینه‌ها",
      "سود پیش از مالیات",
      "مالیات",
      "سود عملیات مستمر",
      "عملیات متوقف‌شده",
      "سود خالص",
      "سود هر سهم",
      "تعداد سهام",
    ];
    const rows = labels.map(
      (label, index) =>
        `| ${label} | ${index === 7 ? "-" : "1,234"} | ${index === 1 ? "(−4,751)" : "—"} |`,
    );
    const content = [
      "### جدول گزارش فصلی چخزر",
      "واحد مبالغ: میلیون ریال؛ EPS: ریال به‌ازای هر سهم؛ تعداد سهام: هزار سهم",
      "",
      "| شرح | سال مالی 1405، فصل 2 | درصد تغییر به فصل قبل |",
      "|---|---:|---:|",
      ...rows,
    ].join("\n");

    const { container } = render(<MarkdownMessage content={content} direction="rtl" />);
    const table = container.querySelector("table");

    expect(container.firstElementChild?.getAttribute("dir")).toBe("rtl");
    expect(table?.querySelectorAll("tbody tr")).toHaveLength(16);
    expect(
      Array.from(table?.querySelectorAll<HTMLTableRowElement>("tbody tr") ?? []).map(
        (row) => row.cells[0]?.textContent,
      ),
    ).toEqual(labels);
    expect(table?.textContent).toContain("سال مالی 1405، فصل 2");
    expect(table?.textContent).toContain("(−4,751)");
    expect(table?.textContent).toContain("-");
  });

  it("renders the redesigned quarterly table oldest-to-newest with a computed change column and image export", () => {
    const content = [
      "### گزارش مالی فصلی شرکت نمونه (Symbol)",
      "مبالغ: میلیون ریال؛ EPS: ریال به‌ازای هر سهم؛ تعداد سهام: هزار سهم",
      "",
      "| شرح | سال مالی 1405، فصل 2 | سال مالی 1405، فصل 1 | سال مالی 1404، فصل 4 | سال مالی 1404، فصل 3 | سال مالی 1404، فصل 2 | درصد تغییر به فصل قبل |",
      "|---|---:|---:|---:|---:|---:|---:|",
      "| فروش | -42 | -38 | -30 | -20 | -10 | 99% |",
      "| حاشیه سود ناخالص | 25 | 22 | 20 | 18 | 15 | 0% |",
      "| مالیات | 0 | -5 | 0 | 0 | 0 | -100% |",

      "| \u062e\u0627\u0644\u0635 \u0633\u0627\u06cc\u0631 \u062f\u0631\u0622\u0645\u062f\u0647\u0627 | 10 | -10 | 5 | 4 | 3 | 0% |",
      "| \u062a\u0639\u062f\u0627\u062f \u0633\u0647\u0627\u0645 \u0634\u0631\u06a9\u062a (\u0647\u0632\u0627\u0631 \u0633\u0647\u0627\u0645) | 200 | 200 | 200 | 200 | 200 | 0% |",
      "| \u0645\u0627\u0644\u06cc\u0627\u062a | 25 | 25 | 25 | 25 | 25 | 0% |",
      "| کاهش فروش | 80 | 100 | 90 | 88 | 86 | 4% |",
    ].join("\n");

    const { container } = render(<MarkdownMessage content={content} />);
    const table = container.querySelector("table");
    const headers = Array.from(table?.querySelectorAll<HTMLTableCellElement>("thead th") ?? []).map(
      (cell) => cell.textContent?.replace(/\s+/g, " ").trim(),
    );
    const rows = Array.from(table?.querySelectorAll<HTMLTableRowElement>("tbody tr") ?? []);

    expect(container.firstElementChild?.getAttribute("dir")).toBe("rtl");
    expect(container.firstElementChild?.className).toContain("max-w-none");
    expect(table?.parentElement?.className).not.toContain("overflow-x-auto");
    expect(container.querySelector("section > header")?.className).toContain("from-[#164e63]");
    expect(
      headers.map((header) =>
        header?.replace(/[\u06f0-\u06f9]/g, (digit) => String(digit.charCodeAt(0) - 0x06f0)),
      ),
    ).toEqual([
      "شرح",
      "سال مالی 1404فصل 2",
      "سال مالی 1404فصل 3",
      "سال مالی 1404فصل 4",
      "سال مالی 1405فصل 1",
      "سال مالی 1405فصل 2",
      "درصد تغییر به فصل قبل",
    ]);
    expect(table?.querySelector("thead th:last-child")?.className).toContain("bg-[#c7e5e1]");
    expect(rows[0]?.cells[1]?.textContent).toBe("(\u06f1\u06f0)");
    expect(rows[0]?.cells[5]?.textContent).toBe("(\u06f4\u06f2)");
    expect(rows[0]?.cells[6]?.textContent).toBe("\u06f1\u06f0\u066b\u06f5\u066a");
    expect(rows[0]?.cells[5]?.className).toContain("bg-rose-100");
    expect(rows[1]?.cells[5]?.textContent).toBe("\u06f2\u06f5\u066b\u06f0\u066a");
    expect(rows[1]?.cells[6]?.textContent).toBe("\u06f1\u06f3\u066b\u06f6\u066a");
    expect(rows[2]?.cells[5]?.textContent).toBe("-");
    expect(rows[2]?.cells[2]?.textContent).toBe("-");
    expect(rows[3]?.cells[6]?.textContent).toBe("");
    expect(rows[3]?.cells[6]?.className).toContain("bg-white");
    expect(rows[4]?.cells[6]?.textContent).toBe("");
    expect(rows[4]?.cells[6]?.className).toContain("bg-white");
    expect(rows[4]?.cells[6]?.className).toContain("bg-white");
    expect(rows[5]?.cells[6]?.textContent).toBe("\u06f0");
    expect(rows[6]?.cells[6]?.textContent).toBe("\u200e\u2212\u06f2\u06f0\u066b\u06f0\u066a");
    expect(rows[6]?.cells[6]?.className).toContain("bg-rose-100");
    expect(rows[1]?.cells[5]?.textContent).toBe("\u06f2\u06f5\u066b\u06f0\u066a");
    expect(table?.querySelector("thead")?.textContent).not.toContain(
      "\u062a\u0639\u062f\u0627\u062f \u0633\u0647\u0627\u0645",
    );
    expect(container.textContent).toContain("(Symbol)");
    expect(container.querySelector('button[aria-label="دانلود تصویر جدول"]')).not.toBeNull();
  });

  it("renders empty content without throwing", () => {
    const { container } = render(<MarkdownMessage content="" />);
    expect(container).not.toBeNull();
  });

  it("renders malformed markdown gracefully as text", () => {
    // Unclosed bold — react-markdown renders ** as literal text
    const { container } = render(<MarkdownMessage content="**unclosed bold" />);
    expect(container.textContent).toContain("unclosed bold");
    expect(container.querySelector("strong")).toBeNull();
  });

  it("XSS: does not render <script> tags from markdown input", () => {
    const { container } = render(<MarkdownMessage content="<script>alert('xss')</script>" />);
    expect(container.querySelector("script")).toBeNull();
    expect(container.innerHTML).not.toContain("<script>");
  });

  it("XSS: does not render <img onerror> payloads", () => {
    const { container } = render(<MarkdownMessage content='<img src="x" onerror="alert(1)">' />);
    const img = container.querySelector("img");
    if (img) {
      expect(img.getAttribute("onerror")).toBeNull();
    }
  });

  it("renders the monthly sales quality ranking responsively with expandable details", async () => {
    const rankingTableMarkdown = [
      "براساس آخرین گزارش های تولید و فروش موجود، رتبه بندی زیر فقط کیفیت داده را نشان می دهد.",
      "",
      "| رتبه | نماد | شرکت | صنعت | امتیاز کیفیت | برچسب | دلیل اصلی | اطمینان |",
      "|---:|---|---|---|---:|---|---|---:|",
      "| ۱ | فولاد | فولاد مبارکه اصفهان | فلزات اساسی | ۹۱ | بسیار قوی | فروش بالاتر از میانگین ۱۲ ماهه، رشد مقدار و ثبات نرخ فروش | ۸۸٪ |",
    ].join("\n");

    const { container } = render(<MarkdownMessage content={rankingTableMarkdown} />);

    const outerWrapper = container.firstElementChild;
    const tableWrapper = container.querySelector("table")?.parentElement;
    const table = container.querySelector("table");
    const reasonHeader = Array.from(container.querySelectorAll("th")).find((cell) =>
      cell.textContent?.includes("دلیل اصلی"),
    );
    const reasonCell = Array.from(container.querySelectorAll("td")).find((cell) =>
      cell.textContent?.includes("فروش بالاتر از میانگین ۱۲ ماهه"),
    );

    expect(outerWrapper?.className).toContain("max-w-none");
    expect(tableWrapper?.getAttribute("dir")).toBe("rtl");
    expect(table?.className).toContain("table-fixed");
    expect(table?.querySelectorAll("colgroup col")).toHaveLength(6);
    expect(container.querySelectorAll('button[aria-expanded="false"]')).toHaveLength(2);
    expect(reasonHeader).toBeFalsy();
    expect(reasonCell).toBeFalsy();
    fireEvent.click(container.querySelector('button[aria-expanded="false"]') as HTMLButtonElement);
    await waitFor(() => expect(container.textContent).toContain("فروش بالاتر از میانگین ۱۲ ماهه"));
  });

  it("XSS: strips javascript: links from anchor hrefs", () => {
    const { container } = render(<MarkdownMessage content="[click me](javascript:alert('xss'))" />);
    const link = container.querySelector("a");
    if (link) {
      // rehype-sanitize removes the attribute entirely (null) or rewrites it — neither case is a javascript: URI
      const href = link.getAttribute("href") ?? "";
      expect(href).not.toContain("javascript:");
    }
  });

  it("renders links with target=_blank and rel=noopener", () => {
    const { container } = render(<MarkdownMessage content="[example](https://example.com)" />);
    const link = container.querySelector("a");
    expect(link).not.toBeNull();
    expect(link?.getAttribute("target")).toBe("_blank");
    expect(link?.getAttribute("rel")).toContain("noopener");
  });
});
