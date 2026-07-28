# Company Disclosure Feed API

`GET /api/v1/disclosures` requires the existing `MarketSummaryRead` authorization policy and authenticated-actor rate limit.

The disclosure feed is read-only over normalized `MonthlyReports` and `FinancialStatements`, resolved through `Companies`. It does not call provider APIs, change ingestion schedules, create alerts/watchlists, or generate investment advice.

Array query values use the standard repeated-key format:

```text
/api/v1/disclosures?types=MonthlyProductionSales&types=IncomeStatement&providerNames=NoavaranCurrentApi&page=1&pageSize=20
```

Examples:

```text
# Latest monthly production/sales disclosures
GET /api/v1/disclosures?types=MonthlyProductionSales

# Non-consolidated income statements (the default scope)
GET /api/v1/disclosures?types=IncomeStatement&consolidationScope=NonConsolidated

# Consolidated balance sheets and cash-flow statements
GET /api/v1/disclosures?types=BalanceSheet&types=CashFlowStatement&consolidationScope=Consolidated
```

The response is `DisclosureListingResult`. No matching disclosures return HTTP 200 with an empty `items` collection and complete pagination/freshness/coverage metadata. `PublishedAt` remains null until a provider publication timestamp is persisted; `ReceivedAt` is the timezone-aware normalized receipt timestamp. Unmapped records remain visible with `UnmappedCompany` coverage metadata. Provider-internal source record identifiers are intentionally not serialized to clients.

`PublishedAt` is nullable and is now persisted when supplied by a provider. A null value means publication time is unknown; clients display an explicit unavailable value and never relabel `ReceivedAt` as publication time. Reporting-period dates in Persian titles are rendered in Jalali, while API dates remain machine-readable.

## AI and Telegram

The explicit-list intent `DisclosureListing` maps natural-language requests such as `فهرست آخرین تولید و فروش منتشر شده را بده` to the same canonical query with `MonthlyProductionSales`. Listing requests for income statement, balance sheet, cash flow, and explicit `تلفیقی` use the corresponding types and consolidation scope. Per-symbol metric questions remain outside this intent.

Telegram renders the canonical result as compact MarkdownV2 rows with a page size of eight. Continuation actions use an opaque `dlp1:<token>:<page>` callback; the expiring server-side token is bound to the actor, tenant, Telegram user, chat, and thread. No raw query or filter data is exposed in callback payloads.
