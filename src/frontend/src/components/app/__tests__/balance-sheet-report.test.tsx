import { afterEach, beforeAll, describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen, within } from "@testing-library/react";
import fixtures from "./fixtures/balance-sheet-154.json";
import { AnswerDensityContext } from "../answer-density";
import {
  BalanceSheetReport,
  chooseBalanceSheetLayout,
} from "../balance-sheet-report/balance-sheet-report";
import {
  balanceSheetChartData,
  buildBalanceSheetView,
  formatBalanceAmount,
  formatPercentagePoints,
  formatRatio,
  formatSignedPercent,
} from "../balance-sheet-report/balance-sheet-report-model";
import {
  BALANCE_SHEET_PNG_FONT_WEIGHTS,
  balanceSheetPngColumnHeaders,
  downloadBalanceSheetReportImage,
  ensureBalanceSheetPngFonts,
} from "../balance-sheet-report/balance-sheet-report-image";
import type { BalanceSheetReportResult } from "../balance-sheet-report/balance-sheet-report-types";
import { MessageList } from "../message-list";

const webMellat = fixtures.webMellat as unknown as BalanceSheetReportResult;
const fazar = fixtures.fazar as unknown as BalanceSheetReportResult;
const negative = fixtures.negative as unknown as BalanceSheetReportResult;
const single = fixtures.single as unknown as BalanceSheetReportResult;
const gaps = fixtures.gaps as unknown as BalanceSheetReportResult;

/** The وبملت fixture with the selected baseline (or the anchor) marked as a restated statement. */
function withRestated(result: BalanceSheetReportResult, target: "baseline" | "anchor") {
  const clone = structuredClone(result);
  const kind = clone.comparison!.selectedBaselineKind;
  const id =
    target === "anchor"
      ? clone.anchorStatementId!
      : clone.comparison!.candidates.find((candidate) => candidate.kind === kind)!.statementId!;
  clone.snapshots[id].statement.isRepresented = true;
  return clone;
}

class ResizeObserverStub {
  observe() {}
  unobserve() {}
  disconnect() {}
}

beforeAll(() => {
  vi.stubGlobal("ResizeObserver", ResizeObserverStub);
});

afterEach(() => vi.restoreAllMocks());

function setWidth(width: number) {
  Object.defineProperty(window, "innerWidth", { configurable: true, value: width });
  Object.defineProperty(HTMLElement.prototype, "clientWidth", {
    configurable: true,
    get: () => width,
  });
}

describe("Feature 154 view model", () => {
  it("formats zero, missing, negatives, ratios and percentage points without arithmetic", () => {
    expect(formatBalanceAmount(0)).toBe("۰");
    expect(formatBalanceAmount(null)).toBe("—");
    expect(formatBalanceAmount(-181354538)).toBe("(۱۸۱٬۳۵۴٬۵۳۸)");
    expect(formatBalanceAmount(59110304681)).toBe("۵۹٬۱۱۰٬۳۰۴٬۶۸۱");
    expect(formatRatio(10.965457637174786)).toBe("۱۰٫۹۷٪");
    expect(formatRatio(0)).toBe("۰٫۰۰٪");
    expect(formatRatio(-5)).toBe("−۵٫۰۰٪");
    expect(formatSignedPercent(18.220609362)).toBe("+۱۸٫۲۲٪");
    expect(formatPercentagePoints(0.0114576)).toBe("+۰٫۰۱ واحد درصد");
  });

  it("builds the banking default view from the anchor snapshot", () => {
    const view = buildBalanceSheetView(webMellat, null)!;
    expect(view.title).toBe("ترازنامه (صورت وضعیت مالی)");
    expect(view.metadata).toEqual(
      expect.arrayContaining([
        "تاریخ: ۱۴۰۵/۰۶/۳۱",
        "گزارش ۶ ماهه",
        "حسابرسی‌نشده",
        "تاریخ انتشار: ۱۴۰۵/۰۷/۳۰",
        "میلیون ریال",
      ]),
    );
    expect(view.metadata).not.toContain("تجدید ارائه‌شده");
    // The standalone basis is not labelled in the header; only a consolidated statement says «تلفیقی».
    expect(view.metadata.join(" | ")).not.toMatch(/جداگانه|غیرتلفیقی|تلفیقی/);
    const consolidated = structuredClone(webMellat);
    for (const snapshot of Object.values(consolidated.snapshots))
      snapshot.statement.isComposing = true;
    expect(buildBalanceSheetView(consolidated, null)!.metadata).toContain("تلفیقی");
    expect(view.assets.rows.find((row) => row.rowId === "cash")?.value).toBe("۴۰۹٬۲۸۷٬۶۲۱");
    expect(view.equity.rows.find((row) => row.rowId === "bank_treasury_shares")?.value).toBe(
      "(۱۸۱٬۳۵۴٬۵۳۸)",
    );
    expect(view.finalAssets.value).toBe("۵۹٬۱۱۰٬۳۰۴٬۶۸۱");
    expect(view.ratio.anchor).toBe("۱۰٫۹۷٪");
    expect(view.assets.caption).toBe("اقلام منتخب");
  });

  it("selects server-computed comparison values for every standard baseline", () => {
    const previous = buildBalanceSheetView(webMellat, "previous_report")!;
    expect(previous.comparison?.label).toBe("گزارش قبلی (۱۴۰۵/۰۳/۳۱)");
    expect(previous.finalAssets.percent).toBe("+۱۸٫۲۲٪");
    expect(previous.finalAssets.amountChange).toBe("+۹٬۱۱۰٬۳۰۴٬۶۸۱");
    expect(previous.ratio.pp).toBe("+۰٫۰۱ واحد درصد");
    const fiscal = buildBalanceSheetView(webMellat, "fiscal_year_end")!;
    expect(fiscal.comparison?.label).toBe("پایان سال مالی قبل (۱۴۰۴/۱۲/۲۹)");
    expect(fiscal.comparison?.baseline?.audit).toBe("حسابرسی‌شده");
  });

  it("keeps the general template placement and missing cash", () => {
    const view = buildBalanceSheetView(fazar, null)!;
    expect(view.assets.rows.find((row) => row.rowId === "cash")?.value).toBe("—");
    expect(view.liabilities.rows.find((row) => row.rowId === "long_term_borrowings")?.value).toBe(
      "۰",
    );
    expect(view.ratio.anchor).toBe("۵۸٫۷۵٪");
  });

  it("reads chart points from snapshots, oldest first, with negative equity and reconciliation warnings", () => {
    const data = balanceSheetChartData(webMellat);
    expect(data.length).toBeLessThanOrEqual(5);
    expect(data.at(-1)?.label).toBe("۱۴۰۵/۰۶/۳۱");
    expect(data.at(-1)?.liabilities).toBe(52628589262);
    expect(data.at(-1)?.equity).toBe(6481715419);
    const negativeData = balanceSheetChartData(negative);
    expect(negativeData.find((point) => point.label === "۱۴۰۵/۰۶/۳۱")?.negativeEquity).toBe(true);
    expect(negativeData.find((point) => point.label === "۱۴۰۵/۰۶/۳۱")?.stackRepresentsAssets).toBe(
      false,
    );
    expect(negativeData.find((point) => point.label === "۱۴۰۵/۰۳/۳۱")?.warning).toBe(true);
    const view = buildBalanceSheetView(negative, null)!;
    expect(view.ratio.anchor).toBe("−۵٫۰۰٪");
  });

  it("chooses the layout by density and container width", () => {
    expect(chooseBalanceSheetLayout(true, 1600, false)).toBe("compact");
    expect(chooseBalanceSheetLayout(false, 767, false)).toBe("compact");
    expect(chooseBalanceSheetLayout(false, 900, false)).toBe("wide");
    expect(chooseBalanceSheetLayout(false, 900, true)).toBe("wide-comparison-stacked");
    expect(chooseBalanceSheetLayout(false, 1100, true)).toBe("wide-comparison-two-sided");
  });
});

describe("Feature 154 chart cases", () => {
  it("G04: a single snapshot gives one point with a ratio", () => {
    const data = balanceSheetChartData(single);
    expect(data).toHaveLength(1);
    expect(data[0].ratio).toBe(40);
  });

  it("G05/G08: missing equity and a version conflict are gaps, never zero", () => {
    const data = balanceSheetChartData(gaps);
    const missingEquity = data.find((point) => point.label === "۱۴۰۵/۰۳/۳۱")!;
    expect(missingEquity.equity).toBeNull();
    expect(missingEquity.ratio).toBeNull();
    expect(missingEquity.liabilities).toBe(58000);
    const conflict = data.find((point) => point.label === "۱۴۰۴/۱۲/۲۹")!;
    expect(conflict.gapReason).toBe("conflicting_report_revision");
    expect([conflict.liabilities, conflict.equity, conflict.ratio, conflict.totalAssets]).toEqual([
      null,
      null,
      null,
      null,
    ]);
  });

  it("G08: the conflict gap carries an accessible note", () => {
    setWidth(1280);
    render(<BalanceSheetReport result={gaps} />);
    expect(screen.getByTestId("balance-sheet-funding-chart").textContent).toContain(
      "نسخه‌های هم‌رتبه متناقض‌اند",
    );
  });

  it("AC-P09: the Feature 154 chart is shown with ShowChart true and false and never reads the UI config", () => {
    for (const showChart of [true, false]) {
      const fetchMock = vi.fn().mockResolvedValue({
        ok: true,
        json: async () => ({ quarterlyFinancialTable: { showChart } }),
      });
      vi.stubGlobal("fetch", fetchMock);
      setWidth(1280);
      const view = render(<BalanceSheetReport result={webMellat} />);
      expect(view.getByTestId("balance-sheet-funding-chart")).toBeTruthy();
      expect(fetchMock).not.toHaveBeenCalled();
      view.unmount();
      vi.unstubAllGlobals();
      vi.stubGlobal("ResizeObserver", ResizeObserverStub);
    }
  });
});

describe("Feature 154 report card", () => {
  it("renders the wide two-sided default with chart above and opens «مقایسه» without any network request", () => {
    setWidth(1280);
    const fetchSpy = vi.spyOn(globalThis, "fetch");
    const { container } = render(<BalanceSheetReport result={webMellat} />);
    const report = screen.getByTestId("balance-sheet-report");
    expect(report.getAttribute("data-layout")).toBe("wide");
    const chart = screen.getByTestId("balance-sheet-funding-chart");
    const table = container.querySelector("[data-layout='two-sided']")!;
    expect(chart.compareDocumentPosition(table) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(container.querySelector("[data-side='assets'] [data-section='assets']")).not.toBeNull();
    expect(
      container.querySelector("[data-side='liabilities-and-equity'] [data-section='equity']"),
    ).not.toBeNull();
    expect(screen.getByTestId("balance-sheet-final-totals").textContent).toContain(
      "جمع بدهی‌ها و حقوق صاحبان سهام",
    );
    expect(screen.getByTestId("balance-sheet-ratio-row").className).toContain("bg-yellow-200");

    fireEvent.click(screen.getByRole("button", { name: "مقایسه" }));
    expect(fetchSpy).not.toHaveBeenCalled();
    expect(report.getAttribute("data-layout")).toBe("wide-comparison-two-sided");
    expect(screen.getByText(/مبنای مقایسه: پایان سال مالی قبل/)).toBeTruthy();
    const assetsTable = container.querySelector("[data-section='assets']") as HTMLElement;
    const headers = within(assetsTable)
      .getAllByRole("columnheader")
      .map((cell) => cell.textContent ?? "");
    expect(headers[0]).toBe("شرح");
    expect(headers[1]).toContain("مانده در ۱۴۰۵/۰۶/۳۱");
    expect(headers[2]).toContain("مانده در ۱۴۰۴/۱۲/۲۹");
    expect(headers[3]).toBe("درصد تغییر");
    expect(container.textContent).not.toContain("به فصل قبل");

    fireEvent.click(screen.getByRole("radio", { name: /گزارش قبلی/ }));
    expect(screen.getByText("مبنای مقایسه: گزارش قبلی (۱۴۰۵/۰۳/۳۱)")).toBeTruthy();
    const change = screen.getAllByRole("button", { name: /درصد تغییر جمع کل دارایی‌ها/ })[0];
    expect(change.getAttribute("aria-label")).toContain("تغییر مبلغ: +۹٬۱۱۰٬۳۰۴٬۶۸۱");
    fireEvent.click(change);
    expect(change.getAttribute("aria-expanded")).toBe("true");
    expect(fetchSpy).not.toHaveBeenCalled();
  });

  it("stacks the comparison between 768 and 1099 px", () => {
    setWidth(900);
    render(<BalanceSheetReport result={{ ...webMellat, requestedView: "comparison" }} />);
    expect(screen.getByTestId("balance-sheet-report").getAttribute("data-layout")).toBe(
      "wide-comparison-stacked",
    );
  });

  it("renders the compact layout with accessible collapsible sections and the chart below", () => {
    setWidth(360);
    const { container } = render(
      <AnswerDensityContext.Provider value="compact">
        <BalanceSheetReport result={webMellat} />
      </AnswerDensityContext.Provider>,
    );
    const toggles = container.querySelectorAll("[data-layout='compact'] button[aria-expanded]");
    expect(toggles.length).toBe(3);
    const assets = toggles[0] as HTMLButtonElement;
    expect(assets.getAttribute("aria-expanded")).toBe("false");
    expect(assets.className).toContain("min-h-11");
    expect(assets.textContent).toContain("۵۹٬۱۱۰٬۳۰۴٬۶۸۱");
    const panel = document.getElementById(assets.getAttribute("aria-controls")!)!;
    expect(panel.hidden).toBe(true);
    fireEvent.click(assets);
    expect(assets.getAttribute("aria-expanded")).toBe("true");
    expect(panel.hidden).toBe(false);
    expect(screen.getByTestId("balance-sheet-ratio-row").textContent).toContain("۱۰٫۹۷٪");
    const chart = screen.getByTestId("balance-sheet-funding-chart");
    const ratio = screen.getByTestId("balance-sheet-ratio-row");
    expect(ratio.compareDocumentPosition(chart) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();

    fireEvent.click(screen.getByRole("button", { name: "مقایسه" }));
    expect(container.textContent).toContain("مانده در ۱۴۰۴/۱۲/۲۹:");
    expect(container.textContent).toContain("درصد تغییر:");
  });

  it("gives each dated balance its own audit and restatement badge on the compact comparison (F154-V-06)", () => {
    setWidth(360);
    const { container } = render(
      <AnswerDensityContext.Provider value="compact">
        <BalanceSheetReport result={withRestated(webMellat, "baseline")} />
      </AnswerDensityContext.Provider>,
    );
    // Default compact header: the anchor's own status is in the metadata line.
    expect(container.textContent).toContain("حسابرسی‌نشده");
    expect(screen.queryByTestId("balance-sheet-baseline-badge")).toBeNull();
    fireEvent.click(screen.getByRole("button", { name: "مقایسه" }));
    expect(screen.getByTestId("balance-sheet-anchor-badge").textContent).toBe("حسابرسی‌نشده");
    const baseline = screen.getByTestId("balance-sheet-baseline-badge");
    expect(baseline.textContent).toContain("مانده در ۱۴۰۴/۱۲/۲۹ (مبنا)");
    expect(baseline.textContent).toContain("حسابرسی‌شده · تجدید ارائه‌شده");
    // Not restated: no restatement badge; a different baseline shows its own status.
    fireEvent.click(screen.getByRole("radio", { name: /گزارش قبلی/ }));
    expect(screen.getByTestId("balance-sheet-baseline-badge").textContent).toContain(
      "مانده در ۱۴۰۵/۰۳/۳۱ (مبنا)",
    );
    expect(screen.getByTestId("balance-sheet-baseline-badge").textContent).toContain(
      "حسابرسی‌نشده",
    );
    expect(screen.getByTestId("balance-sheet-baseline-badge").textContent).not.toContain(
      "تجدید ارائه‌شده",
    );
  });

  it("shows «تجدید ارائه‌شده» for a restated anchor in the compact header and its comparison badge", () => {
    setWidth(360);
    const { container } = render(
      <AnswerDensityContext.Provider value="compact">
        <BalanceSheetReport result={withRestated(webMellat, "anchor")} />
      </AnswerDensityContext.Provider>,
    );
    expect(container.textContent).toContain("تجدید ارائه‌شده");
    fireEvent.click(screen.getByRole("button", { name: "مقایسه" }));
    expect(screen.getByTestId("balance-sheet-anchor-badge").textContent).toBe(
      "حسابرسی‌نشده · تجدید ارائه‌شده",
    );
    expect(screen.getByTestId("balance-sheet-baseline-badge").textContent).not.toContain(
      "تجدید ارائه‌شده",
    );
  });

  it("puts each dated balance's own status on the PNG comparison column headers (F154-V-06)", () => {
    const view = buildBalanceSheetView(withRestated(webMellat, "baseline"), "fiscal_year_end")!;
    const headers = balanceSheetPngColumnHeaders(view, true);
    expect(headers.map((header) => header.title)).toEqual([
      "شرح",
      "مانده در ۱۴۰۵/۰۶/۳۱",
      "مانده در ۱۴۰۴/۱۲/۲۹",
      "درصد تغییر",
    ]);
    expect(headers[1].badge).toBe("حسابرسی‌نشده");
    expect(headers[2].badge).toBe("حسابرسی‌شده · تجدید ارائه‌شده");
    const plain = balanceSheetPngColumnHeaders(
      buildBalanceSheetView(webMellat, "fiscal_year_end")!,
      true,
    );
    expect(plain[2].badge).toBe("حسابرسی‌شده");
    // Single-date PNG: two columns, the anchor status stays in the header metadata.
    const single = buildBalanceSheetView(withRestated(webMellat, "anchor"), null)!;
    expect(balanceSheetPngColumnHeaders(single, false).map((header) => header.title)).toEqual([
      "شرح",
      "مبلغ",
    ]);
    expect(single.metadata).toContain("حسابرسی‌نشده");
    expect(single.metadata).toContain("تجدید ارائه‌شده");
  });

  it("shows a typed outcome message when the report is unavailable", () => {
    render(
      <BalanceSheetReport
        result={{
          ...webMellat,
          outcome: "no_snapshot_at_date",
          anchorStatementId: null,
          snapshots: {},
          outcomeMessage: "ترازنامه جداگانه وبملت با تاریخ دقیق ۱۴۰۵/۰۴/۳۱ موجود نیست.",
        }}
      />,
    );
    expect(screen.getByTestId("balance-sheet-outcome").textContent).toContain("۱۴۰۵/۰۴/۳۱");
  });

  it("is branched from the message list and replaces the text rendering", () => {
    setWidth(1280);
    render(
      <MessageList
        loading={false}
        streaming={false}
        onSuggested={vi.fn()}
        messages={
          [
            {
              id: "a",
              role: "assistant",
              created_at: "2026-11-04T00:00:00Z",
              content: {
                message: "### ترازنامه (صورت وضعیت مالی) — متن",
                intent: "FinancialStatementTableLookup",
                replyLanguage: "fa",
                creditsUsed: 1,
                suggestedQuestions: [],
                suggestedActions: [],
                filters: [],
                citations: [],
                financialStatementTableResult: { balanceSheetReport: webMellat },
              },
            },
          ] as never
        }
      />,
    );
    expect(screen.getByTestId("balance-sheet-report")).toBeTruthy();
    expect(screen.queryByText("### ترازنامه (صورت وضعیت مالی) — متن")).toBeNull();
  });
});

describe("Feature 154 PNG font loading (F154-V-08)", () => {
  function stubFonts(
    load: (font: string) => Promise<unknown>,
    ready: Promise<unknown> = Promise.resolve(),
  ) {
    const fonts = { load: vi.fn(load), ready };
    Object.defineProperty(document, "fonts", { configurable: true, value: fonts });
    return fonts;
  }

  afterEach(() => {
    vi.useRealTimers();
    delete (document as unknown as { fonts?: unknown }).fonts;
  });

  it("loads every Vazirmatn weight the canvas uses and waits for document.fonts.ready", async () => {
    let readyResolved = false;
    const fonts = stubFonts(
      () => Promise.resolve([]),
      new Promise((resolve) => setTimeout(() => ((readyResolved = true), resolve(undefined)), 5)),
    );
    await expect(ensureBalanceSheetPngFonts()).resolves.toBe(true);
    expect(readyResolved).toBe(true);
    expect(fonts.load.mock.calls.map(([font]) => font)).toEqual(
      BALANCE_SHEET_PNG_FONT_WEIGHTS.map((weight) => `${weight} 16px Vazirmatn`),
    );
  });

  it("gives up after the bounded timeout instead of hanging, and tolerates failures or no FontFaceSet", async () => {
    vi.useFakeTimers();
    stubFonts(() => new Promise(() => {}));
    const pending = ensureBalanceSheetPngFonts(3000);
    await vi.advanceTimersByTimeAsync(3000);
    await expect(pending).resolves.toBe(false);
    vi.useRealTimers();
    stubFonts(() => Promise.reject(new Error("network")));
    await expect(ensureBalanceSheetPngFonts()).resolves.toBe(false);
    delete (document as unknown as { fonts?: unknown }).fonts;
    await expect(ensureBalanceSheetPngFonts()).resolves.toBe(false);
  });

  it("export renders the canvas only after the fonts have loaded", async () => {
    const order: string[] = [];
    const pending: Array<() => void> = [];
    stubFonts(() => new Promise((resolve) => pending.push(() => resolve([]))));
    const release = () => {
      order.push("fonts loaded");
      pending.splice(0).forEach((resolve) => resolve());
    };
    vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockImplementation(() => {
      order.push("canvas rendered");
      return null;
    });
    const started = Date.now();
    const exporting = downloadBalanceSheetReportImage(webMellat, null);
    await new Promise((resolve) => setTimeout(resolve, 10));
    expect(order).toEqual([]);
    expect(pending.length).toBe(BALANCE_SHEET_PNG_FONT_WEIGHTS.length);
    release();
    // jsdom has no 2D context, so rendering stops with an error after the font wait.
    await expect(exporting).rejects.toThrow("Canvas is unavailable.");
    expect(order[0]).toBe("fonts loaded");
    expect(order).toContain("canvas rendered");
    // Released fonts end the wait at once (well below the 3 s bound).
    expect(Date.now() - started).toBeLessThan(2000);
  });
});
