import { describe, expect, it } from "vitest";
import type { AdminUserAnalyticsCell } from "@/integrations/financial-copilot/admin-client";
import {
  buildDrillTree,
  buildPivot,
  buildTrend,
  dimensionValues,
  drillInto,
  filterCells,
  jalaliMonthKey,
  periodRange,
  pivotToCsv,
  previousRange,
  reportingDay,
  sumUsers,
  toggleSelection,
  type DimensionKey,
} from "../user-analytics-model";

const cell = (overrides: Partial<AdminUserAnalyticsCell>): AdminUserAnalyticsCell => ({
  acquisitionSource: "tahlilapp",
  plan: "Free",
  status: "enabled",
  activity: "active7d",
  registeredOn: "2026-11-01",
  users: 1,
  ...overrides,
});

const cells: AdminUserAnalyticsCell[] = [
  cell({ plan: "Free", users: 10 }),
  cell({ plan: "Plus", users: 4 }),
  cell({ plan: "Pro", users: 2, status: "locked" }),
  cell({ plan: "Max", users: 1, registeredOn: "2026-09-10" }),
  cell({ acquisitionSource: "native", plan: "none", users: 5, registeredOn: null }),
  cell({ acquisitionSource: "native", plan: "Plus", users: 3, registeredOn: "2026-11-08" }),
];
const valuesFor = (dimension: DimensionKey) => dimensionValues(dimension, cells);

describe("user analytics model", () => {
  it("drills TahlilApp users down into their plans", () => {
    const tree = buildDrillTree(cells, ["acquisitionSource", "plan"], valuesFor);
    const tahlilApp = tree.find((node) => node.value.code === "tahlilapp")!;

    expect(tahlilApp.users).toBe(17);
    expect(tahlilApp.children.map((node) => [node.value.label, node.users])).toEqual([
      ["رایگان", 10],
      ["پلاس", 4],
      ["پرو", 2],
      ["مکس", 1],
    ]);
    expect(tahlilApp.children[0].shareOfParent).toBeCloseTo((10 / 17) * 100);
    expect(tahlilApp.children[0].path).toEqual({ acquisitionSource: "tahlilapp", plan: "Free" });
  });

  it("cross-filters while leaving the slicer's own dimension open", () => {
    const selections = toggleSelection({}, "acquisitionSource", "native");

    expect(sumUsers(filterCells(cells, selections, null))).toBe(8);
    expect(sumUsers(filterCells(cells, selections, null, "acquisitionSource"))).toBe(25);
    expect(toggleSelection(selections, "acquisitionSource", "native")).toEqual({
      acquisitionSource: undefined,
    });
    expect(drillInto(selections, { acquisitionSource: "tahlilapp", plan: "Plus" })).toEqual({
      acquisitionSource: ["tahlilapp"],
      plan: ["Plus"],
    });
  });

  it("restricts by registration day and drops undated users from dated periods", () => {
    const range = periodRange("7d", "2026-11-09");

    expect(range).toEqual({ from: "2026-11-03", to: "2026-11-09" });
    expect(sumUsers(filterCells(cells, {}, range))).toBe(3);
    expect(previousRange("7d", "2026-11-09")).toEqual({ from: "2026-10-27", to: "2026-11-02" });
  });

  it("uses Jalali months for this-month and monthly buckets", () => {
    // 2026-10-23 is 1 Aban 1405.
    expect(jalaliMonthKey("2026-10-23")).toBe("1405-08");
    expect(jalaliMonthKey("2026-10-22")).toBe("1405-07");
    expect(periodRange("thisMonth", "2026-11-09")).toEqual({
      from: "2026-10-23",
      to: "2026-11-09",
    });
    expect(previousRange("thisMonth", "2026-11-09")).toEqual({
      from: "2026-09-23",
      to: "2026-10-10",
    });
  });

  it("builds a pivot without empty rows or columns and exports it", () => {
    const pivot = buildPivot(
      cells,
      "acquisitionSource",
      valuesFor("acquisitionSource"),
      "plan",
      valuesFor("plan"),
    );

    expect(pivot.rows.map((row) => row.code)).toEqual(["native", "tahlilapp"]);
    expect(pivot.columns.map((column) => column.code)).toEqual([
      "Free",
      "Plus",
      "Pro",
      "Max",
      "none",
    ]);
    expect(pivot.values).toEqual([
      [0, 3, 0, 0, 5],
      [10, 4, 2, 1, 0],
    ]);
    expect(pivot.total).toBe(25);
    const csv = pivotToCsv(pivot, "acquisitionSource", "plan");
    expect(csv.startsWith("﻿")).toBe(true);
    expect(csv).toContain("تحلیل‌اپ,10,4,2,1,0,17");
    expect(csv).toContain("جمع,10,7,2,1,5,25");
  });

  it("keeps unexpected codes instead of dropping them", () => {
    const values = dimensionValues("plan", [cell({ plan: "Enterprise" })], {
      Enterprise: "سازمانی",
    });
    expect(values.find((value) => value.code === "Enterprise")?.label).toBe("سازمانی");
  });

  it("buckets short periods by day and longer ones by Jalali month", () => {
    const daily = buildTrend(cells, "plan", periodRange("7d", "2026-11-09"), "2026-11-09");
    expect(daily.granularity).toBe("day");
    expect(daily.buckets).toHaveLength(7);
    expect(daily.buckets.at(-2)?.counts.get("Plus")).toBe(3);

    // Shahrivar (1 Max), Mehr (none), Aban (16 TahlilApp + 3 native); undated users are excluded.
    const monthly = buildTrend(cells, "acquisitionSource", null, "2026-11-09");
    expect(monthly.granularity).toBe("month");
    expect(monthly.buckets.map((bucket) => bucket.total)).toEqual([1, 0, 19]);
  });

  it("reads the reporting day in Tehran time", () => {
    expect(reportingDay("2026-11-08T21:00:00Z")).toBe("2026-11-09");
  });
});
