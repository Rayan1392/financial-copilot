# Feature 112 — Repository and Domain Alignment Notes

## Persisted Sources

- Monthly production/sales records are persisted as `MonthlyReports` (`NormalizedMonthlyReportRow`).
- Financial statement records are persisted as `FinancialStatements` (`NormalizedFinancialStatementRow`) with `StatementType` values `IncomeStatement`, `BalanceSheet`, and `CashFlow`.
- Canonical company/symbol metadata is in `Companies` (`NormalizedCompanyRow`). A disclosure first resolves through nullable `CompanyId`; the compatibility fallback is `(ProviderName, ExternalCompanyId)` only. No fuzzy matching is used.

## Date Semantics

- `VendorPeriodDate`, `PeriodStart`, and `PeriodEnd` are reporting-period fields. They are not announcement publication timestamps.
- `LastSynchronizedAt` is the timezone-aware system receipt/normalization timestamp.
- `PublishedAt` is nullable and persisted on both `MonthlyReports` and `FinancialStatements` by migration `20260727170000_AddDisclosurePublicationDates`. It is populated when a provider supplies an announcement date; otherwise it remains null. It must never be inferred from a reporting period or receipt timestamp.

## Identity and Variants

- Monthly source identity is `(ProviderName, ExternalReportId)`.
- Financial-statement identity includes provider, external statement id, statement type, and audited/represented/composing flags because these variants are persisted as distinct records.
- `IsComposing` is retained as source metadata. Existing statement selection logic determines the default non-consolidated behavior; Feature 112 must not alter that policy.

## Implemented Surfaces

- `ICompanyDisclosureFeedRepository` and its EF Core implementation provide a read-only provider-neutral projection over existing persisted rows.
- `IDisclosureListingUseCase` is the sole canonical query path. `GET /api/v1/disclosures`, `/disclosures`, web AI, and Telegram consume its result rather than querying source tables independently.
- The API uses the existing `MarketSummaryRead` authenticated-actor policy and authenticated-actor rate limit. Telegram callback state is additionally bound to actor, tenant, Telegram user, chat, and message thread.
- The web feed is Persian RTL and retains filters/page in the URL. Its title period context is rendered in Jalali; query filtering and ordering remain machine-readable `DateOnly`/`DateTimeOffset` values.
- `DisclosureListing` is a deterministic AI intent. It remains distinct from metric lookups and comprehensive-analysis requests.
- The Telegram dependency is the implemented Feature 089 assistant adapter. It renders compact MarkdownV2 rows and uses opaque `dlp1:<token>:<page>` callbacks backed by expiring server-side state; raw query/filter JSON is never placed in a callback.

## Read Projection and Data Access

- The canonical feed is an EF Core repository union over the normalized source tables, not a new materialized table or database view. It becomes visible immediately after successful normalization commits.
- The repository reads persisted normalized data only; it never calls a provider or parses a raw payload while listing.
- Existing source uniqueness constraints keep source normalization idempotent. The feed then groups by `LogicalDisclosureId` and selects the most recently received normalized revision, so repeated ingestion cannot produce duplicate feed rows.
- Migration `AddCompanyDisclosureFeedIndexes` adds provider/receipt and provider/company/receipt indexes for monthly reports, plus provider/type/consolidation/receipt ordering for financial statements.
- No feed backfill is necessary because the projection reads the existing source records. Rebuilding is achieved by querying source records again; rollback removes only the supporting indexes and leaves source normalized records untouched.

## Application Query Contract

- `DisclosureListingQuery` is the channel-neutral application input. Empty type and provider lists mean all supported types/providers.
- Type and provider lists use OR semantics within each list; all populated filter groups use AND semantics together.
- The default scope is `NonConsolidated`; page defaults to `1`, page size defaults to `20`, and page size is capped at `100`.
- Receipt filters are timezone-aware `DateTimeOffset` bounds. Publication filters are `DateOnly` bounds and currently return no source rows until a provider publication timestamp is persisted.
- The server always returns a total count and applies deterministic ordering. Client pages must reset to page 1 when filters change; that behavior will be implemented with the web client.

## HTTP API

- `GET /api/v1/disclosures` is protected by the existing `MarketSummaryRead` authenticated-actor policy and returns `DisclosureListingResult` directly.
- Repeated `types` and `providerNames` query parameters follow the API's normal array binding. Validation errors use ASP.NET problem details.
- Empty result sets retain `AsOf`, freshness, coverage, and applied filters. Provider source-record IDs are excluded from serialized output.

## Final Contract and Scope Gate

- Disclosure types: `MonthlyProductionSales`, `IncomeStatement`, `BalanceSheet`, and `CashFlowStatement`.
- Consolidation scopes: `NonConsolidated` (default), `Consolidated`, and `Both`.
- Ordering is deterministic: publication date present first and descending, then receipt descending, then stable disclosure ID. The latest persisted normalized revision is selected per logical disclosure.
- `CoverageStatus` and `FreshnessReasonCode` are returned for non-empty and empty results. Clients display partial/stale warnings rather than claiming complete market coverage.
- The feature is read-only. It does **not** add alerts, watchlists, portfolio monitoring, proactive notifications, provider requests at query time, or any ingestion-schedule/provider-rate change.
- Validation covers the canonical read model, authorized API, web filter/URL state, AI mapping and continuation, Telegram rendering/callback integrity, stale/partial coverage, and revision selection.
