import { afterEach, describe, expect, it, vi } from "vitest";
import type { MonthlyProductTrendResult } from "@/lib/chat.functions";
import { downloadMonthlyProductTrendChartImage } from "../monthly-product-trend-chart-image";
import {
  createProductTrendQuantityAxis,
  formatProductTrendIntegerNumber,
  PRODUCT_TREND_PANEL_LABELS,
} from "../monthly-product-trend-chart-model";

const productTrend = {
  resultDiscriminator: "monthly_product_trend",
  resultVersion: 1,
  resolutionState: "Resolved",
  companyText: "کچاد",
  companySymbol: "کچاد",
  productTitle: "آهن اسفنجی",
  productUnit: "تن",
  points: [
    {
      period: "1404/05",
      fiscalLabel: "05/1404",
      productKey: "product-key",
      productTitle: "آهن اسفنجی",
      productUnit: "تن",
      productionQuantity: 148_868,
      saleQuantity: 1_360,
      salesValueMillionRial: 299_991,
      salesValueBillionToman: 29.46,
      calculatedSaleRateToman: 22_058_161.7647,
      rateStatus: "ValidRate",
      isGap: false,
    },
  ],
  candidates: [],
  evidence: [],
} satisfies MonthlyProductTrendResult;

afterEach(() => {
  vi.restoreAllMocks();
});

describe("downloadMonthlyProductTrendChartImage", () => {
  it("uses whole-number quantity ticks derived from the plotted production and sales values", () => {
    const axis = createProductTrendQuantityAxis([148_868, 1_360]);
    expect(axis).toEqual({ max: 150_000, ticks: [0, 50_000, 100_000, 150_000] });
    expect(
      axis.ticks.map(formatProductTrendIntegerNumber).every((label) => !/[.,]/u.test(label)),
    ).toBe(true);
  });

  it("keeps filename, dates, numeric labels, and axis title export-safe", async () => {
    const fillTextCalls: Array<{
      text: string;
      x: number;
      y: number;
      direction: string;
      textAlign: string;
    }> = [];
    const strokeTextCalls: string[] = [];
    const strokeColors: string[] = [];
    const context = {
      direction: "ltr",
      textAlign: "left",
      fillStyle: "",
      strokeStyle: "",
      font: "",
      lineWidth: 0,
      scale: vi.fn(),
      fillRect: vi.fn(),
      beginPath: vi.fn(),
      moveTo: vi.fn(),
      lineTo: vi.fn(),
      stroke: () => strokeColors.push(context.strokeStyle),
      arc: vi.fn(),
      measureText: (text: string) => ({ width: text.length * 10 }),
      fill: vi.fn(),
      save: vi.fn(),
      restore: vi.fn(),
      strokeText: (text: string) => strokeTextCalls.push(text),
      fillText: (text: string, x: number, y: number) =>
        fillTextCalls.push({
          text,
          x,
          y,
          direction: context.direction,
          textAlign: context.textAlign,
        }),
    } as unknown as CanvasRenderingContext2D;
    const canvas = {
      width: 0,
      height: 0,
      getContext: vi.fn(() => context),
      toBlob: (callback: BlobCallback) => callback(new Blob(["png"], { type: "image/png" })),
    } as unknown as HTMLCanvasElement;
    const link = { href: "", download: "", click: vi.fn() } as unknown as HTMLAnchorElement;
    const createElement = vi
      .spyOn(document, "createElement")
      .mockImplementation((tagName: string) => {
        if (tagName === "canvas") return canvas;
        if (tagName === "a") return link;
        throw new Error(`Unexpected element: ${tagName}`);
      });
    vi.spyOn(URL, "createObjectURL").mockReturnValue("blob:product-trend");
    vi.spyOn(URL, "revokeObjectURL").mockImplementation(() => undefined);

    await downloadMonthlyProductTrendChartImage(productTrend);

    expect(createElement).toHaveBeenCalledWith("canvas");
    expect(link.download).toBe("روند-فروش-آهن-اسفنجی-کچاد.png");
    expect(link.click).toHaveBeenCalledOnce();
    expect(fillTextCalls.some((call) => call.text === "۱۴۰۴/۰۵" && call.direction === "ltr")).toBe(
      true,
    );
    expect(
      fillTextCalls.some((call) => call.text === "۲۲٬۰۵۸٬۱۶۲" && call.direction === "ltr"),
    ).toBe(true);
    expect(strokeTextCalls).toContain("۲۲٬۰۵۸٬۱۶۲");
    expect(fillTextCalls).toEqual(
      expect.arrayContaining([
        expect.objectContaining({ text: "مبلغ فروش" }),
        expect.objectContaining({ text: "نرخ فروش (تومان/تن)" }),
      ]),
    );
    expect(fillTextCalls.some((call) => call.text === "نرخ فروش (تن)")).toBe(false);
    expect(fillTextCalls.some((call) => call.text === "۲۲٬۰۵۸٬۱۶۱٫۷۶")).toBe(false);
    expect(strokeColors).toContain("#4f46e5");
    expect(strokeColors).toContain("#0f766e");
    expect(fillTextCalls.some((call) => call.text === PRODUCT_TREND_PANEL_LABELS.main)).toBe(true);
    expect(
      fillTextCalls.some((call) => call.text.startsWith(PRODUCT_TREND_PANEL_LABELS.quantity)),
    ).toBe(true);
    expect(fillTextCalls.some((call) => call.y >= 790 && call.y < 1120)).toBe(true);
    const rateTicks = fillTextCalls.filter((call) => call.x === 177 && call.y < 700);
    expect(rateTicks).toHaveLength(5);
    const quantityTicks = fillTextCalls.filter(
      (call) => call.x === 177 && call.y >= 790 && call.y <= 1126,
    );
    expect(quantityTicks.map((call) => call.text)).toEqual(
      [0, 50_000, 100_000, 150_000].map(formatProductTrendIntegerNumber),
    );
    expect(quantityTicks.every((call) => !/[.,]/u.test(call.text))).toBe(true);
    expect(rateTicks.every((call) => !/[.٫]/u.test(call.text))).toBe(true);
    expect(fillTextCalls.some((call) => call.text === "۲۹٫۴۶")).toBe(true);
    expect(productTrend.points[0].calculatedSaleRateToman).toBe(22_058_161.7647);

    const axisTitle = fillTextCalls.find((call) => call.text === "مبلغ فروش (میلیارد تومان)");
    expect(axisTitle).toMatchObject({ x: 1710, textAlign: "right", direction: "rtl" });
    expect(axisTitle?.x).toBeLessThanOrEqual(1800);
    expect(fillTextCalls.some((call) => call.y >= 700 && call.y <= 1200)).toBe(true);
  });
});
