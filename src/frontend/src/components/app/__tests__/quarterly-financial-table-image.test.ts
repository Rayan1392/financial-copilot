import { afterEach, describe, expect, it, vi } from "vitest";
import { downloadQuarterlyFinancialTableImage } from "../quarterly-financial-table-image";
import type { QuarterlyFinancialTableModel } from "../quarterly-financial-table";

describe("quarterly financial table image export", () => {
  afterEach(() => vi.restoreAllMocks());

  it("uses shared cost-aware colors and preserves QoQ percentage text", async () => {
    const fillColors: string[] = [];
    const drawnText: Array<{ text: string; color: string }> = [];
    let currentFillColor = "";
    const context = {
      set fillStyle(value: string) {
        currentFillColor = value;
      },
      scale: vi.fn(),
      fillRect: vi.fn(() => fillColors.push(currentFillColor)),
      strokeRect: vi.fn(),
      fillText: vi.fn((text: string) => drawnText.push({ text, color: currentFillColor })),
      measureText: vi.fn(() => ({ width: 10 })),
    } as unknown as CanvasRenderingContext2D;

    vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockReturnValue(context);
    vi.spyOn(HTMLCanvasElement.prototype, "toBlob").mockImplementation((callback) =>
      callback(new Blob()),
    );
    vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:quarterly-table");
    vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(() => undefined);

    const model: QuarterlyFinancialTableModel = {
      title: "Quarterly report",
      symbol: "چخزر",
      summary: "Unit summary",
      quarters: [],
      rows: [
        { label: "Cost increase", sourceItemId: 1, values: ["۳۰٫۱٪"], grossMargin: false },
        {
          label: "Finance cost decrease",
          sourceItemId: 12,
          values: ["−۱۵٫۰٪"],
          grossMargin: false,
        },
        { label: "Revenue increase", sourceItemId: 15, values: ["۲۷٫۵٪"], grossMargin: false },
        { label: "Profit decrease", sourceItemId: 143, values: ["−۱۰٫۰٪"], grossMargin: false },
      ],
    };

    await downloadQuarterlyFinancialTableImage(model);

    const rowCellColors = fillColors.slice(-8);
    expect(rowCellColors.filter((_, index) => index % 2 === 1)).toEqual([
      "#ffe4e6",
      "#d1fae5",
      "#d1fae5",
      "#ffe4e6",
    ]);
    expect(drawnText).toContainEqual({ text: "۳۰٫۱٪", color: "#881337" });
    expect(drawnText).toContainEqual({ text: "−۱۵٫۰٪", color: "#064e3b" });
    expect(drawnText).toContainEqual({ text: "۲۷٫۵٪", color: "#064e3b" });
    expect(drawnText).toContainEqual({ text: "−۱۰٫۰٪", color: "#881337" });
  });

  it("includes the ticker in a safe Persian PNG filename", async () => {
    const fillColors: string[] = [];
    const drawnText: Array<{ text: string; color: string }> = [];
    const drawnFonts: Array<{ text: string; font: string }> = [];
    let currentFillColor = "";
    let currentFont = "";
    const context = {
      set fillStyle(value: string) {
        currentFillColor = value;
      },
      set font(value: string) {
        currentFont = value;
      },
      scale: vi.fn(),
      fillRect: vi.fn(() => fillColors.push(currentFillColor)),
      strokeRect: vi.fn(),
      fillText: vi.fn((text: string) => {
        drawnText.push({ text, color: currentFillColor });
        drawnFonts.push({ text, font: currentFont });
      }),
      measureText: vi.fn(() => ({ width: 10 })),
    } as unknown as CanvasRenderingContext2D;
    let downloadedFilename = "";

    vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockReturnValue(context);
    vi.spyOn(HTMLCanvasElement.prototype, "toBlob").mockImplementation((callback) =>
      callback(new Blob()),
    );
    vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:quarterly-table");
    vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function () {
      downloadedFilename = this.download;
    });

    const model: QuarterlyFinancialTableModel = {
      title: "Quarterly report",
      symbol: "چخزر",
      summary: "Unit summary",
      provenanceNotes: ["مبنای EPS مقایسه‌ای: FY1405 Q2، گزارش 553578؛ سهام پایان دوره."],
      quarters: [{
        label: "فصل سوم<br>منتهی به<br>۳۱ شهریور ۱۴۰۵",
        quarterName: "فصل سوم",
        dateLabel: "۳۱ شهریور ۱۴۰۵",
        sourceIndex: 0,
      }],
      rows: [
        {
          label: "Gross margin",
          sourceItemId: null,
          values: ["\u06f1\u06f7\u066b\u06f7", "\u06f1\u0660\u066b\u0660\u066a"],
          grossMargin: true,
        },
        {
          label: "Net loss",
          sourceItemId: null,
          values: ["(\u06f1\u0660)", "\u200e\u2212\u06f1\u0660\u066b\u0660\u066a"],
          grossMargin: false,
        },
      ],
    };

    await downloadQuarterlyFinancialTableImage(model);

    expect(drawnText).toContainEqual({ text: "فصل سوم", color: "#164e63" });
    expect(drawnText).toContainEqual({ text: "منتهی به", color: "#164e63" });
    expect(drawnText).toContainEqual({ text: "۳۱ شهریور ۱۴۰۵", color: "#164e63" });
    expect(context.fillText).toHaveBeenCalledWith(
      "\u06f1\u06f7\u066b\u06f7\u066a",
      expect.any(Number),
      expect.any(Number),
      expect.any(Number),
    );
    expect(drawnText.map(({ text }) => text)).toContain("شرح");
    expect(drawnText.map(({ text }) => text)).toContain("درصد تغییر به فصل قبل");
    expect(drawnText.map(({ text }) => text)).toContain("روش و مبنای EPS مقایسه‌ای");
    expect(drawnText.map(({ text }) => text)).toContain("مبنای EPS مقایسه‌ای: FY1405 Q2، گزارش 553578؛ سهام پایان دوره.");
    expect(drawnText).toContainEqual({ text: "شرح", color: "#164e63" });
    expect(drawnText).toContainEqual({ text: "درصد تغییر به فصل قبل", color: "#164e63" });
    expect(fillColors).toContain("#c7e5e1");
    expect(fillColors.slice(-6)).toEqual([
      "#fef08a",
      "#fef08a",
      "#fef08a",
      "#ffedd5",
      "#ffe4e6",
      "#ffe4e6",
    ]);
    expect(drawnFonts).toContainEqual({
      text: "\u06f1\u06f7\u066b\u06f7\u066a",
      font: '700 20px Vazirmatn, "Noto Sans Arabic", Tahoma, sans-serif',
    });
    expect(drawnText).toContainEqual({
      text: "\u200e\u2212\u06f1\u0660\u066b\u0660\u066a",
      color: "#881337",
    });
    expect(downloadedFilename).toMatch(
      /^\u062c\u062f\u0648\u0644-\u0645\u0627\u0644\u06cc-\u0641\u0635\u0644\u06cc-\u0686\u062e\u0632\u0631-\u06f1\u06f4[\u06f0-\u06f9]{2}-[\u06f0-\u06f9]{2}-[\u06f0-\u06f9]{2}\.png$/,
    );
    expect(downloadedFilename).not.toMatch(/--|[<>:"/\\|?*]/);
  });
});
