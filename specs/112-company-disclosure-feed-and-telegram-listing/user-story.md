# Feature 112 — Company Disclosure Feed and Telegram Listing

## Status

`[x] Implemented`

## Feature

Add a canonical, filterable and paginated feed of recently published company disclosures, covering monthly production-and-sales reports and the three supported financial-statement types. Expose the same canonical result through the web application, web AI chat, and Telegram with channel-appropriate rendering and navigation.

## Story

As a FinancialCopilot user,

I want to browse or ask for the latest company disclosures and filter them by disclosure type,

so that I can quickly discover newly published monthly production/sales reports and financial statements without already knowing a company symbol.

## Business Context

Monthly production/sales reports and financial statements are already ingested and normalized by existing provider workflows. Feature 112 must create a provider-neutral read model over persisted normalized records and make their disclosure metadata discoverable.

This feature is a read-only discovery surface. It must not:

- fetch or parse provider payloads at listing-query time;
- change ingestion schedules, provider request rates, or normalization behavior;
- infer investment conclusions from disclosure metadata;
- claim complete market-wide coverage when source coverage is partial or stale.

## Dependencies

- Existing persisted monthly production/sales reports and their normalized company linkage.
- Existing persisted financial statements and their statement-type, period, consolidation, provider, publication and ingestion metadata.
- Canonical company, symbol and trading-instrument mappings already used by FinancialCopilot.
- Existing provider/source freshness and health conventions.
- Existing AI query orchestration and structured response contracts.
- Feature 089 Telegram assistant integration and callback handling.

> Implementation must resolve actual entity/table and feature names from the repository. Names in this specification are semantic references, not authorization to introduce duplicate persistence models.

## In Scope

### Supported disclosure types

- `MonthlyProductionSales`
- `IncomeStatement`
- `BalanceSheet`
- `CashFlowStatement`

### Product surfaces

- A Persian RTL disclosure-feed page.
- A server-side paginated disclosure-list API.
- A `DisclosureListing` AI intent returning structured rows.
- Telegram rendering with compact rows and next/previous navigation.

### Filters

- One or more disclosure types, with OR semantics between selected types.
- Canonical symbol or company-name search.
- Provider/source.
- Publication date range.
- System receipt date range.
- Financial-statement consolidation scope:
  - default: non-consolidated statements only;
  - explicit option: consolidated statements;
  - optional option: both, when supported by the UI/query contract.

### Displayed row data

At minimum:

- canonical symbol;
- company name;
- localized disclosure title;
- disclosure type;
- provider-reported publication date, when available;
- system receipt date;
- provider/source;
- revision/correction indicator, when applicable.

## Out of Scope

- New ingestion or parsing for disclosure types not already persisted.
- Disclosure types other than monthly production/sales and the three listed financial statements.
- Rendering full reports, report line items, source PDF/HTML bodies, or generated analysis inside the feed.
- Automatic alerts or notifications when a new disclosure arrives.
- User watchlists, portfolio monitoring, or proactive notification workflows.
- Buy/sell recommendations, valuation conclusions, or AI-generated report interpretation.
- Editing, approving, deleting, or reprocessing disclosure records from this surface.

## Canonical Disclosure Semantics

### Canonical identity

Each feed row must have a stable, provider-neutral `DisclosureId` derived from or mapped to the persisted normalized disclosure record. Identity must remain stable across repeated reads and must not be based on mutable display fields such as title or symbol text.

The canonical record must retain source identity needed for traceability, such as provider name and provider/external report identifier, without exposing raw provider payloads.

### Company linkage

Every row must link to the existing canonical company and symbol model. The feature must not create a second independent symbol catalog. Records that cannot be mapped to a canonical company must not be silently attributed to a similarly named company; they must follow an explicit unmapped/coverage policy.

### Disclosure title

Titles must be generated deterministically from normalized metadata and localized for Persian display. When available, the title should include useful period context without becoming a generated analysis, for example:

- `گزارش فعالیت ماهانه تولید و فروش — خرداد ۱۴۰۵`
- `صورت سود و زیان — دوره ۳ ماهه منتهی به ۱۴۰۵/۰۳/۳۱`
- `ترازنامه — منتهی به ۱۴۰۵/۰۳/۳۱`
- `صورت جریان وجوه نقد — دوره ۶ ماهه منتهی به ۱۴۰۵/۰۶/۳۱`

If required period metadata is missing, the title must fall back to a valid type label rather than inventing a period.

### Financial-statement consolidation

The default listing and natural-language queries must return non-consolidated financial statements only (`IsComposing = false`, or the repository-equivalent semantic). Consolidated statements must be included only when the user explicitly requests them or selects the consolidated/both filter.

The row and structured response must expose consolidation scope for financial statements so clients never present consolidated and non-consolidated records as indistinguishable duplicates.

### Publication date

`PublishedAt` is the provider-reported publication timestamp/date associated with the disclosure. If it is unavailable:

- return `null` in the API/structured contract;
- display `نامشخص` or the approved localized equivalent;
- never substitute the system receipt timestamp while labeling it as publication date.

### System receipt date

`ReceivedAt` is the persisted ingestion/normalization timestamp for the specific disclosure revision that produced the feed row. It must be stored and returned as a timezone-aware timestamp.

### Revision and correction behavior

A disclosure correction/revision must be traceable and must not create ambiguous duplicate rows.

Default behavior:

- show only the latest successfully normalized revision of a logical disclosure;
- expose `IsRevised`, `RevisionNumber` or equivalent revision metadata;
- preserve the original logical disclosure identity or a stable revision-family identifier;
- never replace a valid completed revision with an incomplete, failed, or still-processing revision.

Historical revision browsing is out of scope unless an existing repository convention already exposes it with no new product surface.

### Definition of “latest”

“Latest” means ordering by:

1. publication date descending for records with a publication date;
2. records without a publication date after records with a publication date;
3. system receipt date descending;
4. stable disclosure identifier descending/ascending as documented, used only as the deterministic final tie-breaker.

This ordering must be identical across the web feed, web AI, and Telegram query service.

### Freshness and coverage

The result contract must distinguish:

- `AsOf`: when the feed projection/query result was evaluated;
- source freshness: latest successfully received disclosure timestamp per included source/type where available;
- `IsPartialCoverage`: whether one or more requested types/sources cannot be claimed as complete;
- localized coverage/freshness warning text or machine-readable reason codes.

Provider outage, lag, unmapped records, or unsupported source/type combinations must not be represented as an empty-but-complete market result.

## Canonical Query Contract

The application query must support:

```text
DisclosureListingQuery
- Types[]
- SymbolOrCompany
- ProviderNames[]
- PublishedFrom
- PublishedTo
- ReceivedFrom
- ReceivedTo
- ConsolidationScope = NonConsolidated | Consolidated | Both
- Page
- PageSize
```

Rules:

- Empty `Types[]` means all four supported disclosure types.
- Multiple types and providers use OR semantics within their own filter group.
- Different filter groups combine with AND semantics.
- Date-range boundaries and timezone interpretation must be documented and tested.
- `PageSize` must have a safe default and enforced maximum.
- Invalid ranges, unsupported enum values and excessive page sizes must return validation errors rather than being silently normalized.
- Query execution must use normalized persisted data/read projections only.

## Structured Result Contract

All product surfaces must consume one canonical application result, with channel rendering performed after query execution.

```text
DisclosureListingResult
- Items[]
  - DisclosureId
  - CompanyId
  - Symbol
  - CompanyName
  - DisclosureType
  - LocalizedTitle
  - PublishedAt?
  - ReceivedAt
  - ProviderName
  - ConsolidationScope?
  - IsRevised
  - RevisionNumber?
- Pagination
  - Page
  - PageSize
  - HasPreviousPage
  - HasNextPage
  - TotalCount? or documented continuation semantics
- AppliedFilters
- AsOf
- Freshness
- Coverage
```

Web AI and Telegram must not independently re-query different tables or reconstruct disclosure semantics.

## Web Experience

### Route

```text
/disclosures
```

### Table

The Persian RTL table must show:

- نماد
- نام شرکت
- عنوان اطلاعیه
- تاریخ انتشار
- تاریخ دریافت در سیستم

Disclosure type, provider/source, correction status and consolidation scope may be shown as badges, secondary text, expandable details, or additional columns according to the existing design system.

### Filters and state

- Multi-select disclosure-type filter.
- Symbol/company search with debounce or explicit submit according to existing patterns.
- Provider/source filter.
- Publication-date range.
- System-receipt-date range.
- Consolidation scope for financial statements.
- Reset action.
- Filter and page state represented in the URL so the view can be refreshed/shared without losing state.

Changing any filter must reset pagination to the first page.

### States

The page must provide distinct localized states for:

- initial loading;
- page transition/loading;
- no matching results;
- partial coverage;
- stale sources;
- validation failure;
- authorization failure;
- recoverable server error.

An empty result must not hide a partial-coverage or stale-source warning.

## AI Query Routing

### Intent

Add `DisclosureListing` as a list/discovery intent separate from:

- per-symbol financial metric lookup;
- production/sales metric lookup;
- stock screening;
- comprehensive company analysis;
- full-report or line-item retrieval.

### Supported natural-language examples

- `فهرست آخرین تولید و فروش منتشر شده را بده`
- `آخرین صورت‌های سود و زیان منتشرشده را لیست کن`
- `آخرین ترازنامه‌های منتشرشده را نشان بده`
- `فهرست صورت جریان وجوه نقد شرکت‌ها`
- `اطلاعیه‌های مالی منتشرشده امروز را نمایش بده`
- `آخرین صورت‌های مالی تلفیقی را بده`
- `گزارش‌های تولید و فروش کچاد را لیست کن`

The intent mapper must extract supported type, symbol/company, date and consolidation cues. It must use safe defaults when omitted and reject unsupported interpretations rather than hallucinating filters.

### Ambiguity preservation

Existing symbol-specific metric requests must remain routed to their current metric intent. For example:

- `آخرین فروش ماهانه فولاژ` is a metric/value request, not automatically a disclosure listing.
- `آخرین گزارش‌های تولید و فروش فولاژ را لیست کن` is a disclosure listing.
- `آخرین صورت سود و زیان سکرد` remains governed by the existing full-statement/metric behavior unless the wording clearly asks for a list of disclosures.

### AI response

For web AI, return a structured table component backed by `DisclosureListingResult`, not prose-only markdown. The response must include an explicit next-page/continuation action when more results exist and retain the original filters during continuation.

## Telegram Experience

Telegram must use the same `DisclosureListing` intent and canonical query service but render a channel-specific compact list rather than a wide table.

### Suggested row template

```text
۱) شغدیر — تولید و فروش ماهانه
گزارش فعالیت ماهانه — خرداد ۱۴۰۵
انتشار: ۱۴۰۵/۰۴/۳۱ | دریافت: ۱۴۰۵/۰۵/۰۴ ۱۹:۲۲
```

For financial statements, include a concise consolidation label when relevant, such as `غیرتلفیقی` or `تلفیقی`.

### Telegram requirements

- Show current page and total pages when a reliable total is available; otherwise show page number and continuation availability.
- Use bounded page sizes appropriate for Telegram readability.
- Provide previous/next inline actions only when applicable.
- Preserve filters across navigation.
- Use signed, tamper-resistant and expiring callback state or a server-side token reference.
- Do not place unrestricted raw filter JSON in callback payloads.
- Handle expired, malformed or replayed callback state with a localized recoverable response.
- Respect Telegram message and callback payload limits.
- If a page cannot fit, reduce rows or split safely without changing the logical page order.
- Never render HTML tables or rely on horizontal scrolling.
- Escape Telegram formatting characters according to the selected parse mode.
- Preserve authorization/user binding so one user cannot reuse another user’s pagination token where actor boundaries apply.

## Proposed API and UI Surface

```text
GET /api/v1/disclosures
  ?types=MonthlyProductionSales,IncomeStatement,BalanceSheet,CashFlowStatement
  &symbolOrCompany=<text>
  &providerNames=<comma-separated-or-repeated-values>
  &publishedFrom=<ISO-8601>
  &publishedTo=<ISO-8601>
  &receivedFrom=<ISO-8601>
  &receivedTo=<ISO-8601>
  &consolidationScope=NonConsolidated|Consolidated|Both
  &page=<positive integer>
  &pageSize=<bounded integer>

Web route: /disclosures
AI: DisclosureListing intent -> canonical DisclosureListingQuery
Telegram: DisclosureListing intent -> canonical query -> Telegram renderer + callback navigation
```

The final parameter encoding must follow existing API conventions; do not introduce a one-off format if the project already has array-query conventions.

## Acceptance Criteria

1. A user can open `/disclosures` and see the newest supported disclosures in a server-paginated Persian RTL table.
2. Every displayed row contains canonical symbol, company name, localized disclosure title, publication date or an explicit unavailable value, and system receipt date.
3. Users can filter by any combination of the four supported disclosure types; selected values use OR semantics.
4. Users can search by canonical symbol/company and filter by provider/source, publication date, receipt date and financial-statement consolidation scope.
5. Non-consolidated financial statements are the default; consolidated statements appear only when explicitly requested/selected.
6. The default ordering follows the specified null-safe publication/receipt/id ordering and is deterministic while underlying data is unchanged.
7. Page navigation does not create duplicates or omissions while the underlying dataset and applied filters are unchanged.
8. Corrected disclosures show only the latest successfully normalized revision by default and expose correction/revision metadata.
9. Missing publication dates remain null in contracts and visibly unavailable in clients; receipt dates are never relabeled as publication dates.
10. `فهرست آخرین تولید و فروش منتشر شده را بده` is recognized as `DisclosureListing` and produces the canonical structured result.
11. Listing requests for each financial-statement type map to the correct type, and explicit `تلفیقی` requests map to consolidated scope.
12. Existing metric/value and comprehensive-analysis intents remain unchanged for requests that do not clearly ask for a disclosure list.
13. Web AI displays a structured, paginated table and provides a continuation action that preserves filters.
14. Telegram displays a compact Persian list, not a web/HTML table, and provides bounded previous/next navigation.
15. Telegram callback state is signed/tamper-resistant, expires, remains bound to the appropriate actor where required, and preserves applied filters.
16. Telegram output respects message, callback and formatting limits and handles expired/tampered callbacks with a localized recoverable message.
17. Results expose provider/source, `AsOf`, freshness and coverage metadata; partial or stale coverage is clearly disclosed even when zero rows match.
18. The list query reads persisted normalized data/read projections only and does not call external providers at request time.
19. Authorization and existing tenant/actor boundaries are enforced consistently for API, web AI and Telegram.
20. The feature does not change ingestion scheduling, provider synchronization, financial advice behavior, or existing report-analysis flows.

## Example Web Result

| نماد | نام شرکت | عنوان اطلاعیه | تاریخ انتشار | تاریخ دریافت در سیستم |
|---|---|---|---|---|
| شغدیر | پتروشیمی غدیر | گزارش فعالیت ماهانه تولید و فروش — خرداد ۱۴۰۵ | ۱۴۰۵/۰۴/۳۱ | ۱۴۰۵/۰۵/۰۴، ۱۹:۲۲ |

Date values must be transported as timezone-aware machine-readable values and localized only in presentation clients. Persian/Jalali formatting must not alter filtering or sorting semantics.
