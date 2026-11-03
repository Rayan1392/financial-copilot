import type { DisclosureScope } from "@/integrations/financial-copilot/disclosures";

export type DisclosureSearchState = {
  page: number;
  search: string;
  types: string;
  scope: DisclosureScope;
  publishedFrom: string;
  publishedTo: string;
};

export function normalizeDisclosureSearch(search: Record<string, unknown>): DisclosureSearchState {
  return {
    page: Math.max(1, Number(search.page) || 1),
    search: typeof search.search === "string" ? search.search : "",
    types: typeof search.types === "string" ? search.types : "",
    scope: (search.scope === "Consolidated" || search.scope === "Both" ? search.scope : "NonConsolidated") as DisclosureScope,
    publishedFrom: typeof search.publishedFrom === "string" ? search.publishedFrom : "",
    publishedTo: typeof search.publishedTo === "string" ? search.publishedTo : "",
  };
}

export function patchDisclosureSearch(
  current: DisclosureSearchState,
  patch: Partial<DisclosureSearchState>,
  resetPage = true,
): DisclosureSearchState {
  return { ...current, ...patch, page: resetPage ? 1 : (patch.page ?? current.page) };
}
