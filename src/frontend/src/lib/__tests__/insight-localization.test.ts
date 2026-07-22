import { describe, expect, it } from "vitest";
import { localizeInsightValue, localizePeriod } from "../insight-localization";

describe("insight period localization", () => {
  it("localizes ThreeMonths for followed-symbol events", () => {
    expect(localizePeriod("ThreeMonths")).toBe("سه‌ماهه");
    expect(localizeInsightValue("ThreeMonths", "period_type")).toBe("سه‌ماهه");
  });

  it("localizes ProductSales as the monthly production and sales report type", () => {
    expect(localizeInsightValue("ProductSales", "report_type")).toBe("تولید و فروش ماهانه");
  });
});
