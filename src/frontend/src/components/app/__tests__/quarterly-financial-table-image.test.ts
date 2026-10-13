import { afterEach, describe, expect, it, vi } from "vitest";
import { downloadQuarterlyFinancialTableImage } from "../quarterly-financial-table-image";
import type { QuarterlyFinancialTableModel } from "../quarterly-financial-table";

describe("quarterly financial table image export", () => {
  afterEach(() => vi.restoreAllMocks());

  it("includes the ticker in a safe Persian PNG filename", async () => {
    const fillColors: string[] = [];
    let currentFillColor = "";
    const context = {
      set fillStyle(value: string) {
        currentFillColor = value;
      },
      scale: vi.fn(),
      fillRect: vi.fn(() => fillColors.push(currentFillColor)),
      strokeRect: vi.fn(),
      fillText: vi.fn(),
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
      quarters: [{ label: "1405 Q2", year: 1405, quarter: 2, sourceIndex: 0 }],
      rows: [
        { label: "Gross margin", values: ["\u06f1\u06f7\u066b\u06f7", ""], grossMargin: true },
      ],
    };

    await downloadQuarterlyFinancialTableImage(model);

    expect(context.fillText).toHaveBeenCalledWith(
      "\u0633\u0627\u0644 \u0645\u0627\u0644\u06cc \u06f1\u06f4\u06f0\u06f5\u060c \u0641\u0635\u0644 \u06f2",
      expect.any(Number),
      expect.any(Number),
      expect.any(Number),
    );
    expect(context.fillText).toHaveBeenCalledWith(
      "\u06f1\u06f7\u066b\u06f7\u066a",
      expect.any(Number),
      expect.any(Number),
      expect.any(Number),
    );
    expect(fillColors.slice(-3)).toEqual(["#ffedd5", "#fef08a", "#ffffff"]);
    expect(downloadedFilename).toMatch(
      /^\u062c\u062f\u0648\u0644-\u0645\u0627\u0644\u06cc-\u0641\u0635\u0644\u06cc-\u0686\u062e\u0632\u0631-\u06f1\u06f4[\u06f0-\u06f9]{2}-[\u06f0-\u06f9]{2}-[\u06f0-\u06f9]{2}\.png$/,
    );
    expect(downloadedFilename).not.toMatch(/--|[<>:"/\\|?*]/);
  });
});
