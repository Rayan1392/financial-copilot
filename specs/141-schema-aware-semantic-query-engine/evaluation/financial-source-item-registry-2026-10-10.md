# Feature 141 — Noavaran Source-Item Registry — 2026-10-10

## Result

The versioned registry and navigation hierarchy now contain all 296 repository-snapshot source items. Provider identity and Persian-title approval are recorded independently; neither semantic approval nor query authorization is implied. Exact inventory comparison found no duplicate/missing keys, UUID mismatches, or title mismatches.

| Statement type | Registered | Snapshot match |
|---|---:|---|
| BalanceSheet | 107 | 107/107 |
| IncomeStatement | 119 | 119/119 |
| CashFlow | 70 | 70/70 |
| Total | 296 | 296/296 |

The input used was `SemanticCatalog/source-item-snapshot.json`, whose source declaration identifies `public.FinancialStatementSourceItems` and optional `FinancialStatementSourceItemMetricMappings.MetricCode`. Its 296 unique `(ProviderName, StatementType, SourceItemId)` keys, UUIDs, and `titleFa` values match the generated registry exactly. The available schema snapshot/catalog definitions include `FinancialStatementSourceItems`, `FinancialStatementLineItems`, and the source-item mapping table. No live database was queried in this task.

## Metadata and binding

- Each row retains its actual `sourceItemCatalogId` UUID. The stable catalog key is derived from `ProviderName + StatementType + SourceItemId`.
- All 296 entries now have separate `identityApproval.status=OwnerApproved` and `titleApproval.status=OwnerApproved` records, scoped to the identity fields and `titleFa` respectively.
- Deterministic binding is available for **1 concept**: `operatingProfitPerShare` → `NoavaranCurrentApi / IncomeStatement / 161` (`DirectSourceItem`, `IRRPerShare`, reported value). It uses the pre-existing owner-approved semantic review plus the reviewed canonical concept entry. The generated hierarchy carries this binding, and a typed resolver rejects absent, ambiguous, dimension-mismatched, or unit-incomplete bindings.
- **295 source items remain semantically unbound/pending review.** Existing `MetricCode` values are preserved, not promoted to concept equivalence by themselves. In particular, items 143 (`NET_PROFIT`), 160 (`EPS`), 168 (`EPS_CONSOLIDATED`), 317 (Codal-suffixed), and BalanceSheet/271 remain distinct and unbound. IncomeStatement/168 is not merged with /160; consolidation remains a separate statement qualifier.
- Item BalanceSheet/286 (“نوع دوره مالی”) is categorical and now has no effective monetary unit. Unit distribution is 284 `MillionIRR`, 8 `IRRPerShare`, and 4 unresolved. The other unresolved cases remain BalanceSheet/99, CashFlow/35, and IncomeStatement/153. Raw provider units remain separate and unknown where the source says `N/A`.
- **Query authorization: 0/296 authorized.** Identity/title approval, semantic binding, and unit classification do not grant SQL execution. Production and Development chat routing, Planner authorization, and SQL authorization were not expanded.

The requested representative rows were checked: IncomeStatement/143, /160, /161, /168 and BalanceSheet/271, /286. Unreviewed rows return incomplete rather than borrowing a similar mapping.

## Verification

- Catalog artifact unit tests: 21/21 passed.
- .NET `SchemaNavigatorTests`: 25/25 passed, including exact binding through the regenerated hierarchy and non-authorization checks.
- Release solution build: passed, 0 warnings, 0 errors.
- Python catalog integrity validation: passed.
- .NET catalog integrity validation: passed; source hash `ceaac8fcd3aca514bf8f3b51da93d7da098b90d2710b53e3c7bac98135f20eb2`.
- No broad LLM benchmark, holdout, or PostgreSQL execution was run.

## Remaining Option B blocker

This registers governed source identities and makes reviewed source bindings machine-readable; it does **not** complete the previous Navigator integration boundary. `CanonicalSemanticIntent` still is not carried losslessly into the existing Navigator/Planner request, and 295 source items have no reviewed canonical concept binding. PostgreSQL execution remains unverified; the preceding integration report records the local disposable fixture blocked by unavailable Docker. Recommendation: **LIMITED_GO for registry completeness and the one reviewed deterministic binding; NO_GO for broader Option B shadow integration** until the canonical intent contract is carried through Navigator/Planner and remaining concepts have sufficient reviewed semantic/unit metadata.
