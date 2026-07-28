import { financialCopilotApi } from "./api-client";

export type DisclosureType = "MonthlyProductionSales" | "IncomeStatement" | "BalanceSheet" | "CashFlowStatement";
export type DisclosureScope = "NonConsolidated" | "Consolidated" | "Both";
export type DisclosureItem = { disclosureId: string; symbol?: string; companyName?: string; type: DisclosureType; title: string; publishedAt?: string; receivedAt: string; providerName: string; isRevised: boolean; isComposing: boolean };
export type DisclosureListingResult = { items: DisclosureItem[]; page: number; pageSize: number; hasPreviousPage: boolean; hasNextPage: boolean; totalCount: number; totalPages: number; coverageStatus: string; freshnessReasonCode: string };

export async function getDisclosures(filters: { page: number; search?: string; types?: DisclosureType[]; scope?: DisclosureScope; providers?: string[]; publishedFrom?: string; publishedTo?: string; receivedFrom?: string; receivedTo?: string }) {
  const params = new URLSearchParams({ page: String(filters.page), pageSize: "20", consolidationScope: filters.scope ?? "NonConsolidated" });
  if (filters.search) params.set("symbolOrCompany", filters.search);
  filters.types?.forEach((type) => params.append("types", type));
  filters.providers?.forEach((provider) => params.append("providerNames", provider));
  if (filters.publishedFrom) params.set("publishedFrom", startOfDay(filters.publishedFrom)); if (filters.publishedTo) params.set("publishedTo", endOfDay(filters.publishedTo));
  if (filters.receivedFrom) params.set("receivedFrom", startOfDay(filters.receivedFrom)); if (filters.receivedTo) params.set("receivedTo", endOfDay(filters.receivedTo));
  return financialCopilotApi<DisclosureListingResult>(`/api/v1/disclosures?${params}`);
}

// Date-only controls represent Tehran calendar days, rather than the browser's UTC day.
function startOfDay(date: string) { return `${date}T00:00:00.000+03:30`; }
function endOfDay(date: string) { return `${date}T23:59:59.999+03:30`; }
