# Feature 112 Tasks — Company Disclosure Feed and Telegram Listing

> Specification-only change. Do not implement application code as part of this feature-definition update.

## 1. Repository and Domain Alignment

- [x] Inspect the existing monthly-report, financial-statement, company/symbol, provider, AI response and Telegram models before choosing entity or contract names.
- [x] Document the actual persisted entities/tables and timestamp fields used as publication date, receipt/normalization date, provider identity, statement type, period and consolidation scope.
- [x] Confirm how existing financial-statement records represent `IsComposing`; preserve the product rule that non-consolidated statements are the default.
- [x] Confirm the currently implemented Telegram feature dependency and replace any stale feature-number reference in documentation.
- [x] Confirm existing authorization, tenant and actor-boundary conventions for web/API/Telegram queries.
- [x] Record repository findings in the feature implementation notes so no duplicate symbol catalog, provider abstraction or disclosure persistence model is introduced.

## 2. Canonical Disclosure Read Model

- [x] Define a provider-neutral `DisclosureFeedItem` semantic model covering `MonthlyProductionSales`, `IncomeStatement`, `BalanceSheet` and `CashFlowStatement`.
- [x] Define stable `DisclosureId`, canonical `CompanyId`, canonical symbol, provider/source identity and source/external report identifier.
- [x] Define a logical disclosure/revision-family identifier when corrected reports are represented as separate persisted rows.
- [x] Define latest-successful-revision selection: completed/valid latest revision only; never select failed, partial or processing revisions over a completed revision.
- [x] Define `IsRevised`, revision number/version and consolidation metadata exposed by the read model.
- [x] Define deterministic localized-title generation from disclosure type, reporting period and fiscal-end metadata, including fallback behavior for missing period fields.
- [x] Define `PublishedAt` as nullable provider-reported publication time and prohibit receipt-date substitution.
- [x] Define timezone-aware `ReceivedAt` for the selected revision.
- [x] Define how unmapped company/symbol records affect visibility and partial-coverage metadata; prohibit fuzzy silent attribution.
- [x] Define default null-safe ordering: publication date present first and descending, then receipt date descending, then stable identifier tie-breaker.
- [x] Define source freshness, `AsOf`, partial-coverage flags and machine-readable coverage/freshness reason codes.

## 3. Read Projection and Data Access Design

- [x] Decide whether the canonical feed is implemented as an indexed database view, query projection, materialized read table or repository union, following existing architecture conventions.
- [x] Ensure list requests read persisted normalized data only and never call provider APIs or parse raw provider payloads at request time.
- [x] Define how the projection becomes visible after successful monthly-report or financial-statement normalization without changing provider schedules.
- [x] Define idempotent projection updates/rebuild behavior so repeated ingestion does not create duplicate feed rows.
- [x] Define indexes supporting type, canonical company/symbol, provider, publication date, receipt date, consolidation scope and deterministic ordering.
- [x] Define consistency expectations between source normalized records and the feed, including acceptable lag and how lag contributes to freshness warnings.
- [x] Define migration/backfill requirements for existing persisted disclosures if a new projection/table is selected.
- [x] Define rollback/rebuild strategy for the read model without modifying source normalized records.

## 4. Application Query Contract

- [x] Add a canonical `DisclosureListingQuery` with type list, symbol/company search, provider list, publication range, receipt range, consolidation scope, page and page size.
- [x] Define enum values and serialization names for all four disclosure types and `NonConsolidated`, `Consolidated`, `Both`.
- [x] Set default consolidation scope to `NonConsolidated`.
- [x] Define empty type selection as all supported disclosure types.
- [x] Document OR semantics within type/provider groups and AND semantics across filter groups.
- [x] Define safe page/page-size defaults and a hard maximum page size.
- [x] Define date-boundary and timezone semantics for `From`/`To` filters.
- [x] Validate unsupported types/scopes, invalid date ranges, empty/oversized search values, non-positive pages and excessive page sizes.
- [x] Define deterministic pagination semantics and whether total count is always returned or continuation-only mode is permitted.
- [x] Reset page to 1 whenever any applied filter changes in clients.

## 5. Canonical Result Contract

- [x] Define `DisclosureListingResult` as the only application result consumed by API, web feed, web AI and Telegram rendering.
- [x] Include row fields: disclosure/company IDs, symbol, company name, type, localized title, nullable publication date, receipt date, provider, consolidation scope and revision metadata.
- [x] Include pagination fields: page, page size, has previous, has next, and total count/total pages where supported.
- [x] Include normalized applied-filter summary.
- [x] Include `AsOf`, freshness and coverage metadata even for empty result sets.
- [x] Keep machine-readable timestamps timezone-aware and perform Jalali/Persian formatting only in presentation layers.
- [x] Prohibit independent channel-specific reconstruction of title, correction or consolidation semantics from raw entities.

## 6. API Surface

- [x] Add an authorized `GET /api/v1/disclosures` endpoint following existing API/controller/minimal-API conventions.
- [x] Follow existing array-query encoding conventions for `types` and `providerNames` rather than introducing an isolated format.
- [x] Map validation failures to the project-standard problem-details/error contract.
- [x] Return the canonical result contract with pagination, applied filters, `AsOf`, freshness and coverage metadata.
- [x] Enforce current tenant/actor authorization boundaries and avoid exposing source identifiers that are not authorized for clients.
- [x] Document response behavior for no matching rows, partial coverage, stale providers and unmapped records.
- [x] Add API contract examples for all disclosure types and consolidated/non-consolidated filtering.

## 7. Web Disclosure Feed

- [x] Add a Persian RTL `/disclosures` route and a navigation entry consistent with the existing application information architecture.
- [x] Render a server-paginated table containing symbol, company name, disclosure title, publication date and system receipt date.
- [x] Surface disclosure type, source/provider, revision/correction and consolidation scope as columns, badges or row details consistent with the design system.
- [x] Add type multi-select, symbol/company search, provider filter, publication range, receipt range and consolidation-scope filter.
- [x] Persist filters and page in URL state and restore the same view after refresh/share.
- [x] Reset to page 1 after any filter change.
- [x] Display missing publication date as `نامشخص` (or approved localized copy) without using receipt date as fallback.
- [x] Distinguish initial loading from page-transition loading and preserve stable table layout during transitions.
- [x] Add explicit localized empty, stale, partial-coverage, validation, authorization and server-error states.
- [x] Ensure empty results do not suppress stale/partial coverage notices.
- [x] Add responsive behavior for narrow screens without converting the feed into an unreadable wide table.
- [x] Add keyboard navigation, accessible labels, focus handling and pagination semantics.

## 8. AI Intent Detection and Query Mapping

- [x] Add `DisclosureListing` as a distinct intent from metric lookup, screening, company analysis, report-body retrieval and line-item queries.
- [x] Recognize the canonical example `فهرست آخرین تولید و فروش منتشر شده را بده`.
- [x] Recognize listing phrases for income statements, balance sheets and cash-flow statements.
- [x] Extract symbol/company, type, date and consolidation cues into `DisclosureListingQuery`.
- [x] Map explicit `تلفیقی` to `Consolidated`; use `NonConsolidated` by default.
- [x] Define handling for wording such as `صورت‌های مالی` that may imply all three financial-statement types.
- [x] Preserve current metric routing for value-oriented requests such as `آخرین فروش ماهانه فولاژ`.
- [x] Route explicit list wording such as `گزارش‌های تولید و فروش فولاژ را لیست کن` to `DisclosureListing`.
- [x] Preserve current behavior for `آخرین صورت سود و زیان {نماد}` unless list/disclosure wording is explicit.
- [x] Reject or safely handle unsupported disclosure categories without inventing a type.
- [x] Map AI queries to the canonical application query service rather than separate repositories.

## 9. Web AI Structured Response

- [x] Return a structured disclosure-table response component rather than prose-only markdown.
- [x] Bind table rows directly to `DisclosureListingResult`.
- [x] Include current page/continuation metadata and an explicit next-page action when more results exist.
- [x] Preserve the original filters and actor context during continuation.
- [x] Include source/freshness/coverage notices in the structured response when applicable.
- [x] Ensure missing publication dates, revision labels and consolidation labels match the web feed semantics.
- [x] Ensure AI-generated surrounding text does not turn metadata into investment advice or imply complete coverage.

## 10. Telegram Rendering

- [x] Use the same `DisclosureListing` intent, application query and canonical result used by web/API.
- [x] Add a compact Persian renderer using numbered multi-line rows rather than HTML/Markdown tables.
- [x] Include symbol, concise disclosure type/title, publication date, receipt date and consolidation label when applicable.
- [x] Include source/freshness/partial-coverage warning only when needed and in concise channel-appropriate copy.
- [x] Add current-page and total-page text when total pages are reliable; otherwise indicate page and continuation availability.
- [x] Choose a bounded Telegram-specific page size that keeps normal pages within message limits.
- [x] Escape user/provider/title text according to the selected Telegram parse mode.
- [x] Define safe fallback behavior when rendered content still exceeds platform limits: reduce items or split message chunks without reordering or duplicating rows.
- [x] Add localized empty-result output that still includes partial/stale coverage warnings.

## 11. Telegram Pagination and Callback Security

- [x] Add previous/next inline actions only when the relevant page exists.
- [x] Preserve all selected filters and the requested consolidation scope across page navigation.
- [x] Use signed, tamper-resistant and expiring callback state or an opaque server-side pagination token.
- [x] Keep callback payloads within Telegram limits; do not embed unrestricted raw query/filter JSON.
- [x] Bind callback state to the requesting actor/chat where existing security boundaries require it.
- [x] Validate page bounds and reject arbitrary page-size escalation from callbacks.
- [x] Define behavior for expired, malformed, tampered, replayed and cross-user callback tokens.
- [x] Return a localized recoverable message for invalid/expired navigation and invite the user to run the query again.
- [x] Ensure repeated callback delivery is idempotent and does not mutate disclosure data.

## 12. Localization and Date Presentation

- [x] Define Persian labels for all disclosure types, consolidation scopes, revision indicators, date labels and states.
- [x] Render Jalali/Persian dates according to existing product conventions while retaining machine timestamps in API contracts.
- [x] Include time and timezone for system receipt date where existing UI conventions require it.
- [x] Define display behavior for date-only provider publication values versus full timestamps.
- [x] Verify Persian digits, RTL punctuation and mixed Latin provider/symbol text in both web and Telegram.

## 13. Authorization, Privacy and Safety

- [x] Apply existing API/web/Telegram actor and tenant authorization rules to every query path and continuation action.
- [x] Ensure callback tokens cannot leak filters or identifiers beyond what clients are already allowed to view.
- [x] Ensure no raw provider payload, internal error detail or unrestricted source identifier is returned.
- [x] Ensure disclosure-list responses remain informational and do not generate buy/sell recommendations.
- [x] Confirm the feature does not alter ingestion/provider scheduling, report normalization or financial-analysis behavior.

## 14. Observability

- [x] Emit query count, latency, result count, empty-result count, selected disclosure types and channel (`api`, `web`, `web-ai`, `telegram`).
- [x] Record provider/type freshness and partial-coverage reason distribution without high-cardinality company/symbol labels.
- [x] Record AI intent-routing success/fallback outcomes for disclosure-list phrases.
- [x] Record Telegram page navigation success, expired callback, tampered callback, cross-actor rejection and render-limit fallback outcomes.
- [x] Add correlation identifiers across AI/Telegram request, canonical query and response rendering using existing observability conventions.
- [x] Avoid logging unrestricted user query text, callback secrets or sensitive authorization context.

## 15. Tests — Read Model and Query

- [x] Test all four disclosure types in a mixed canonical result.
- [x] Test canonical company/symbol linkage and explicit behavior for unmapped records.
- [x] Test default non-consolidated selection and explicit consolidated/both selection.
- [x] Test latest-successful-revision selection when newer failed/processing revisions exist.
- [x] Test corrected disclosures do not create ambiguous duplicates.
- [x] Test missing publication date remains null and sorts after dated records.
- [x] Test receipt-date and stable-ID tie-breakers.
- [x] Test type/provider OR semantics and cross-filter AND semantics.
- [x] Test publication and receipt date ranges including boundaries/timezones.
- [x] Test deterministic pagination with no duplicates/omissions while data is unchanged.
- [x] Test partial coverage and stale-source metadata for non-empty and empty results.
- [x] Test read path does not invoke external provider clients.

## 16. Tests — API and Web

- [x] Test authorized and unauthorized API requests using existing actor/tenant rules.
- [x] Test invalid enum, invalid date range, invalid page and excessive page size problem responses.
- [x] Test response contract includes rows, pagination, applied filters, `AsOf`, freshness and coverage.
- [x] Test web default state, each filter, combined filters, URL restoration and page reset after filter change.
- [x] Test loading, empty, partial, stale, authorization and error states.
- [x] Test missing publication date rendering and no receipt-date substitution.
- [x] Test RTL/responsive/accessibility behavior for long Persian titles and mixed Latin/Persian values.

## 17. Tests — AI Routing and Web AI

- [x] Test `فهرست آخرین تولید و فروش منتشر شده را بده` routes to `DisclosureListing` with `MonthlyProductionSales`.
- [x] Test listing phrases for income statement, balance sheet and cash-flow statement.
- [x] Test `صورت‌های مالی` maps to the documented three-type set.
- [x] Test explicit `تلفیقی` and default non-consolidated mapping.
- [x] Test symbol/company and date extraction.
- [x] Regression-test metric requests such as `آخرین فروش ماهانه فولاژ` and existing per-symbol statement/analysis requests.
- [x] Test web AI structured table contract and continuation preserving filters.
- [x] Test partial/stale coverage appears in AI output without claiming complete market coverage.

## 18. Tests — Telegram

- [x] Test compact rendering for every disclosure type and consolidation scope.
- [x] Test Persian/Jalali date formatting, missing publication date and mixed RTL/LTR text.
- [x] Test empty result, partial coverage and stale-source messages.
- [x] Test single-page, first-page, middle-page and last-page navigation controls.
- [x] Test filters remain unchanged across next/previous callbacks.
- [x] Test message-length fallback and parse-mode escaping.
- [x] Test callback payload size limits.
- [x] Test expired, malformed, tampered, replayed and cross-actor callbacks.
- [x] Test duplicate callback delivery is idempotent.

## 19. End-to-End Scenarios

- [x] Web: open feed, filter to monthly production/sales, navigate pages and verify stable ordering.
- [x] Web: filter to non-consolidated income statements and verify consolidated rows are excluded.
- [x] Web AI: ask for latest production/sales disclosures and continue to the next page.
- [x] Web AI: request latest consolidated financial statements and verify consolidation labels.
- [x] Telegram: ask the canonical Persian query, receive compact rows, navigate forward and backward with preserved filters.
- [x] Coverage: simulate stale/partial provider state and verify warning consistency across API, web, web AI and Telegram.
- [x] Revision: ingest/prepare a corrected logical disclosure and verify only the latest successful revision is listed by default.

## 20. Documentation and Completion Gate

- [x] Document final entity/table mappings, read-model strategy, enum values, endpoint contract, AI examples and Telegram callback design.
- [x] Add Feature 112 to the repository implementation checklist in the appropriate order, keeping it unchecked until this completion gate passes.
- [x] Explicitly state that this feature does not add alerts/watchlists or modify ingestion scheduling.
- [x] Keep all implementation tasks unchecked until code is implemented and verified in a later implementation phase.
- [x] Do not mark Feature 112 complete until the canonical read model, API, web RTL feed, AI routing, Telegram rendering/navigation, authorization, freshness/coverage semantics and end-to-end tests pass.
