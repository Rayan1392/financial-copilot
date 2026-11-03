import { describe, expect, it, vi } from "vitest";

const { api } = vi.hoisted(() => ({ api: vi.fn() }));
vi.mock("../api-client", () => ({ financialCopilotApi: api }));

import { getDisclosures } from "../disclosures";
import { formatDisclosurePublicationDate, formatDisclosureReceiptDate } from "@/lib/format/disclosure";

describe("company disclosure web integration", () => {
  it("serializes publication dates as Gregorian DateOnly values for the backend", async () => {
    api.mockResolvedValueOnce({ items: [] });

    await getDisclosures({
      page: 3,
      search: "فولاد",
      types: ["MonthlyProductionSales", "IncomeStatement"],
      scope: "Both",
      publishedFrom: "2026-07-01",
      publishedTo: "2026-07-31",
    });

    const path = api.mock.calls[0][0] as string;
    const query = new URL(path, "https://financial-copilot.test").searchParams;
    expect(query.get("page")).toBe("3");
    expect(query.get("pageSize")).toBe("20");
    expect(query.get("symbolOrCompany")).toBe("فولاد");
    expect(query.getAll("types")).toEqual(["MonthlyProductionSales", "IncomeStatement"]);
    expect(query.has("providerNames")).toBe(false);
    expect(query.get("consolidationScope")).toBe("Both");
    expect(query.get("publishedFrom")).toBe("2026-07-01");
    expect(query.get("publishedTo")).toBe("2026-07-31");
    expect(query.has("receivedFrom")).toBe(false);
    expect(query.has("receivedTo")).toBe(false);
  });

  it("uses an explicit unknown value for missing publication and never substitutes receipt time", () => {
    expect(formatDisclosurePublicationDate(undefined)).toBe("نامشخص");
    expect(formatDisclosurePublicationDate(undefined)).not.toContain("تهران");
    expect(formatDisclosureReceiptDate("2026-07-01T20:30:00Z")).not.toContain("(تهران)");
  });
});
