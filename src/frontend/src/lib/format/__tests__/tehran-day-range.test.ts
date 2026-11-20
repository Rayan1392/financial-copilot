import { describe, expect, it } from "vitest";
import {
  lastTehranDays,
  tehranDayRangeToUtc,
  tehranDayStartUtc,
  tehranToday,
} from "@/lib/format/tehran-day-range";

describe("Tehran day range", () => {
  it("starts a Tehran day at 20:30 UTC the evening before (UTC+03:30)", () => {
    expect(tehranDayStartUtc("2026-11-01")?.toISOString()).toBe("2026-10-31T20:30:00.000Z");
  });

  it("uses the daylight-saving offset for summer days before 2022 (UTC+04:30)", () => {
    expect(tehranDayStartUtc("2021-07-01")?.toISOString()).toBe("2021-06-30T19:30:00.000Z");
  });

  it("makes the end exclusive at the start of the following day", () => {
    expect(tehranDayRangeToUtc("2026-10-03", "2026-11-01")).toEqual({
      fromUtc: "2026-10-02T20:30:00.000Z",
      toUtc: "2026-11-01T20:30:00.000Z",
    });
  });

  it("leaves an empty or invalid side open", () => {
    expect(tehranDayRangeToUtc("", "")).toEqual({ fromUtc: undefined, toUtc: undefined });
    expect(tehranDayRangeToUtc("not-a-day", "2026-11-01").fromUtc).toBeUndefined();
    expect(tehranDayStartUtc("2026/11/01")).toBeUndefined();
  });

  it("knows today in Tehran across the UTC date line", () => {
    expect(tehranToday(new Date("2026-11-01T21:00:00Z"))).toBe("2026-11-02");
    expect(tehranToday(new Date("2026-11-01T20:00:00Z"))).toBe("2026-11-01");
  });

  it("returns the last thirty Tehran days including today", () => {
    expect(lastTehranDays(30, new Date("2026-11-01T21:00:00Z"))).toEqual({
      from: "2026-10-04",
      to: "2026-11-02",
    });
  });
});
