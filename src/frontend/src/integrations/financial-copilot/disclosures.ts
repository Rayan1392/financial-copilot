import { financialCopilotApi } from "./api-client";

export type DisclosureType = "MonthlyProductionSales" | "IncomeStatement" | "BalanceSheet" | "CashFlowStatement";
export type DisclosureScope = "NonConsolidated" | "Consolidated" | "Both";
export type DisclosureItem = { disclosureId: string; symbol?: string; companyName?: string; type: DisclosureType; title: string; publishedAt?: string; reportingPeriodEnd?: string; reportingPeriodType?: string; receivedAt: string; providerName: string; isRevised: boolean; isComposing: boolean };
export type DisclosureListingResult = { items: DisclosureItem[]; page: number; pageSize: number; hasPreviousPage: boolean; hasNextPage: boolean; totalCount: number; totalPages: number; coverageStatus: string; freshnessReasonCode: string };

export async function getDisclosures(filters: { page: number; search?: string; types?: DisclosureType[]; scope?: DisclosureScope; publishedFrom?: string; publishedTo?: string }) {
  const params = new URLSearchParams({ page: String(filters.page), pageSize: "20", consolidationScope: filters.scope ?? "NonConsolidated" });
  if (filters.search) params.set("symbolOrCompany", filters.search);
  filters.types?.forEach((type) => params.append("types", type));
  if (filters.publishedFrom) params.set("publishedFrom", filters.publishedFrom);
  if (filters.publishedTo) params.set("publishedTo", filters.publishedTo);
  return financialCopilotApi<DisclosureListingResult>(`/api/v1/disclosures?${params}`);
}
