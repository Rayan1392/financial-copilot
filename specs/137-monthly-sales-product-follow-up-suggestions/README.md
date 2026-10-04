# Feature 137 — Monthly Sales Product Follow-up Suggestions

## Problem

Company-level monthly sales trend responses already provide deterministic analysis and a chart, but they stop at the company total. Users must manually know a product name and formulate a second query before they can inspect product-level monthly sales.

This feature adds a small, deterministic set of follow-up actions after a successful company-level monthly sales trend response. The actions point to real products from the resolved company’s accepted monthly product-sales data and invoke the existing `monthly_product_trend` capability.

## User value

The user can move from a company total to the company’s most relevant real products with one click. Product names, ordering, eligibility, and query text are produced by backend data and canonical resolvers rather than by the LLM.

## Example interaction

User: `روند فروش کچاد`

Existing response:

- company-level monthly sales analysis;
- existing monthly sales chart.

Structured follow-up actions at the end of the response may include:

- `روند فروش کنسانتره آهن (خشک) کچاد`;
- `روند فروش گندله کچاد`;
- `روند فروش فولاد کچاد`.

The exact products and order are determined from the current accepted data. The example does not hard-code `کچاد` or any product.

## In scope

- Detecting the existing company-level `MonthlyActivityTrend` result, independent of one exact Persian phrase.
- Resolving eligible products from accepted, company-scoped monthly `ProductSales` observations.
- Reusing the existing `MonthlyProductTrend` capability and its product identity/resolution rules.
- Ranking candidates deterministically by recent product sales value.
- Returning at most three structured `SuggestedAction` items.
- Reusing the existing web/API/conversation/Telegram suggested-action transport.
- Explicit V2 integration, V1 freeze treatment, tests, rollout, and observability.

## Out of scope

- A general recommendation engine or watchlist engine.
- Investment advice, portfolio suggestions, or product popularity inference.
- LLM-generated product names, SQL, ranking, or follow-up prose.
- A new vector database, data provider, migration, or raw-data aggregation at response time.
- Changes to company sales calculations, product sales calculations, or the existing company chart.
- Frontend redesign. Existing suggested-action rendering is the intended UI surface.
- Adding a new V1 capability, route, parser, or response branch.

## Dependencies

- Company trend: Feature 076 persisted `CompanyMonthlyActivityTrendSnapshots`, Feature 077 query routing/use case, and the existing company chart path.
- Product trend: Feature 136 `MonthlyProductTrend`, including accepted `ProductSales`/`OutputType = 0` filtering, company-scoped product identity, 12-position product-bounded windows, and typed resolution outcomes.
- Existing response action contract: `SuggestedAction`, `SuggestedActionHttpResponse`, conversation payload persistence, API mapping, frontend `suggestedActions`, and the web click handler.
- Existing company resolver: `ICompanyResolverService` and `ResolvedCompany`.
- Active orchestration mode: Microsoft Agent Framework V2. V1 remains frozen for new work under `specs/POLICY-V1-FREEZE.md`.

## High-level behavior

1. The normal semantic route selects and executes the existing company monthly sales trend capability.
2. Only after a usable company trend result is produced, a deterministic follow-up selector resolves the canonical company identity.
3. The selector reads the latest accepted company-scoped `ProductSales` period available for the trend response, using the existing product read repository and source predicates.
4. It keeps products with a valid sales-value observation in that anchor period, an unambiguous canonical identity, and a queryable product-level trend path.
5. It ranks candidates by aggregated product sales value for the anchor period, breaks ties by normalized title and stable product key, and takes three.
6. It creates `SuggestedAction` values whose label and message use the canonical product title and resolved company symbol. Each action targets `monthly_product_trend` with `RunRelatedCapability`.
7. The actions are carried in the existing structured response metadata. The company answer and chart remain unchanged.
8. If no eligible candidates exist, the company response succeeds with zero follow-up actions.

## Acceptance criteria summary

- At most three real, eligible, deterministically ordered product actions are returned.
- Product names and queries come from structured backend data, never LLM invention.
- The generated query reaches the existing product-level monthly trend capability.
- Missing, stale, ambiguous, unsupported, or unresolved product data produces no fabricated action.
- Existing company trend output, chart, calculations, API consumers, and V1 behavior remain compatible.

See [Design.md](./Design.md) for the audited architecture and decisions, and [Tasks.md](./Tasks.md) for the implementation slices.
