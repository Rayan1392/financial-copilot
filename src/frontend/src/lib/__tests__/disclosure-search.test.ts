import { describe, expect, it } from "vitest";
import { normalizeDisclosureSearch, patchDisclosureSearch } from "../disclosure-search";

describe("disclosure feed URL state", () => {
  it("uses a safe default state and restores every supported filter from the URL", () => {
    expect(normalizeDisclosureSearch({})).toEqual({
      page: 1, search: "", types: "", providers: "", scope: "NonConsolidated",
      publishedFrom: "", publishedTo: "", receivedFrom: "", receivedTo: "",
    });
    expect(normalizeDisclosureSearch({
      page: "4", search: "فولاد", types: "MonthlyProductionSales,IncomeStatement", providers: "A,B", scope: "Both",
      publishedFrom: "2026-07-01", publishedTo: "2026-07-31", receivedFrom: "2026-08-01", receivedTo: "2026-08-02",
    })).toMatchObject({ page: 4, search: "فولاد", types: "MonthlyProductionSales,IncomeStatement", providers: "A,B", scope: "Both", publishedFrom: "2026-07-01", receivedTo: "2026-08-02" });
  });

  it("resets pagination when a filter changes but preserves it for pagination actions", () => {
    const current = normalizeDisclosureSearch({ page: "3", search: "فولاد", scope: "Both" });

    expect(patchDisclosureSearch(current, { providers: "ProviderA" })).toMatchObject({ page: 1, providers: "ProviderA", search: "فولاد", scope: "Both" });
    expect(patchDisclosureSearch(current, { page: 4 }, false)).toMatchObject({ page: 4, search: "فولاد", scope: "Both" });
  });
});
