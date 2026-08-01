import { describe, it, expect } from "vitest";
import { render } from "@testing-library/react";
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

  it("widens the monthly sales quality ranking table and keeps it RTL-friendly", () => {
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
    expect(table?.className).toContain("monthly-sales-quality-ranking-table");
    expect(table?.querySelectorAll("colgroup col")).toHaveLength(8);
    expect(reasonHeader).not.toBeNull();
    expect(reasonCell).not.toBeNull();
  });

  it("XSS: strips javascript: links from anchor hrefs", () => {
    const { container } = render(
      // eslint-disable-next-line no-script-url
      <MarkdownMessage content="[click me](javascript:alert('xss'))" />,
    );
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
