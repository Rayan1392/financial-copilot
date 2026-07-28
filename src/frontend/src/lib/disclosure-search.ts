import type { DisclosureScope } from "@/integrations/financial-copilot/disclosures";

export type DisclosureSearchState = {
  page: number;
  search: string;
  types: string;
  providers: string;
  scope: DisclosureScope;
  publishedFrom: string;
  publishedTo: string;
  receivedFrom: string;
  receivedTo: string;
};

export function normalizeDisclosureSearch(search: Record<string, unknown>): DisclosureSearchState {
  return {
    page: Math.max(1, Number(search.page) || 1),
    search: typeof search.search === "string" ? search.search : "",
    types: typeof search.types === "string" ? search.types : "",
    providers: typeof search.providers === "string" ? search.providers : "",
    scope: (search.scope === "Consolidated" || search.scope === "Both" ? search.scope : "NonConsolidated") as DisclosureScope,
    publishedFrom: typeof search.publishedFrom === "string" ? search.publishedFrom : "",
    publishedTo: typeof search.publishedTo === "string" ? search.publishedTo : "",
    receivedFrom: typeof search.receivedFrom === "string" ? search.receivedFrom : "",
    receivedTo: typeof search.receivedTo === "string" ? search.receivedTo : "",
  };
}

export function patchDisclosureSearch(
  current: DisclosureSearchState,
  patch: Partial<DisclosureSearchState>,
  resetPage = true,
): DisclosureSearchState {
  return { ...current, ...patch, page: resetPage ? 1 : (patch.page ?? current.page) };
}
