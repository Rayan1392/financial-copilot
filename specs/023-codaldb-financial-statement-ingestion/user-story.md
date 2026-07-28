# User Story — CodalDB Financial Statement Ingestion

> Depends on `021`, `022`. Schema reference:
> [docs/codaldb-datasource.md](../../docs/codaldb-datasource.md).

## Story

As a scanner user,
I want CodalDB income-statement and balance-sheet line items normalized into the platform's
`NormalizedFinancialStatementRow` / `NormalizedFinancialStatementLineItemRow` tables with correct
fiscal periods and a single canonical statement variant per period,
so that the scanner and derived-metrics engine consume accurate, unambiguous Codal fundamentals.

## Context

In CodalDB a single `Statements` row (`Id`) is a statement header that owns **both** income
items (`IncomeItemAmounts` → `IncomeItems`) and balance items (`BalanceSheetItemAmounts` →
`BalanceSheetItems`). The same `(CompanyId, PeriodEnd, PeriodType)` can appear in multiple
variants (audited/unaudited, consolidated/parent, original/restated). `PeriodType` is the
**cumulative period length in months** (3/6/9/12 = quarterly-cumulative). Amounts carry no
per-row scale (`Unit = 'N/A'`).

The normalized model has one `NormalizedFinancialStatementRow` (with `PeriodType` holding the
statement **type** string, and `PeriodStart`/`PeriodEnd` capturing the fiscal window) plus child
`NormalizedFinancialStatementLineItemRow` records (`MetricCode` + `Value`).

## Curated line-item mapping (Phase 1)

Only the items below are mapped to canonical `MetricCode`s in this story. Each maps from a
verified CodalDB catalog id. A governed `ItemId → MetricCode` table makes this expandable later
without code branches.

### Income statement (`IncomeItems.ItemId`)

| CodalDB `ItemTitleEn` (ItemId) | Canonical `MetricCode` | Note |
|---|---|---|
| Revenue (15) | `REVENUE` | Quarterly/cumulative operating revenue |
| Total Revenue (300) | `TOTAL_REVENUE` | |
| Net income (143) | `NET_PROFIT` | **Aligned to the existing scannable `NET_PROFIT`** so current growth calculators apply |
| Operating profit (140) | `OPERATING_PROFIT` | |
| Gross profit (139) | `GROSS_PROFIT` | |
| Earning per share (160) | `EPS` | User term "EPS" = Codal "Earning per share" |
| Net Profit consolidated per share (168) | `EPS_CONSOLIDATED` | Consolidated EPS variant |
| Finance costs (12) | `FINANCE_COSTS` | Ingested as an input for the derived `EBIT` (see `026`) |
| Income taxes payments (13) | `INCOME_TAX` | Ingested as an input for the derived `EBIT` (see `026`) |

> **EBIT** is **not** a CodalDB line item; it is a derived metric defined in `026`
> (recommended `EBIT = NET_PROFIT + FINANCE_COSTS + INCOME_TAX`, with `OPERATING_PROFIT` as a
> documented proxy fallback). This story only ingests its inputs.

### Balance sheet (`BalanceSheetItems.ItemId`)

| CodalDB `ItemTitleEn` (ItemId) | Canonical `MetricCode` | Note |
|---|---|---|
| Total equity (147) | `TOTAL_EQUITY` | |
| Paid capital (188) | `CAPITAL` | User term "Capital"; alternatives `Issued capital` (57) / `Base capital` (156) documented |

## Acceptance Criteria

- A `CodalDbFinancialStatementNormalizer` (`ProviderName = "CodalDb"`,
  `Dataset = FinancialStatements`) deserializes the statements payload for a company and, per
  selected statement, produces **two** `NormalizedFinancialStatementRow` records sharing the
  same period window:
  - one with `PeriodType = "IncomeStatement"` carrying the mapped income line items,
  - one with `PeriodType = "BalanceSheet"` carrying the mapped balance line items.
  `ExternalStatementId` is the CodalDB statement id suffixed by statement type (e.g.
  `"{StmtId}:INC"`, `"{StmtId}:BS"`) to keep rows distinct and idempotent.
- **Fiscal period mapping:** `PeriodEnd` = `Statements.PeriodEnd` (Gregorian, as `DateOnly`);
  `PeriodStart` = fiscal-year start derived from `FiscalYearEnd` (≈ `FiscalYearEnd − 1 year`),
  so the `PeriodStart..PeriodEnd` span equals the cumulative `PeriodType` months. The Jalali
  strings (`PeriodEndJalali`, `FiscalYearEndJalali`) are retained as source evidence. No
  relative-period estimation is needed (unlike CyclicalWaves) because dates are absolute — so
  **no `StaleData` estimated-date warning** is attached.
- **Canonical variant selection:** for each `(CompanyId, PeriodEnd, PeriodType)` exactly one
  statement variant is normalized, chosen by a documented `CodalDbStatementSelectionPolicy`:
  prefer `IsAudited = 1` over unaudited, then the latest representment (`IsRepresented`), then
  consolidated vs parent by configuration (default: consolidated `IsComposing = 1` when present,
  else parent). The chosen `(IsAudited, IsComposing, IsRepresented)` flags are recorded in
  `WarningsJson`/source evidence so the selection is explainable. Soft-deleted statements
  (`isDeleted = 1`) are excluded.
- Only the curated line items above are written; unmapped CodalDB items are ignored in Phase 1
  (their availability for future expansion is noted, not silently lost — see `024`/`025` for
  other datasets and the governed mapping table below).
- Amounts are taken from `Amount` as-is; the assumed scale (million Rials) is recorded once as
  source/quality evidence rather than per row (`Unit` is ignored, it is `'N/A'`).
- Upserts are idempotent on `(ProviderName, ExternalStatementId)` and `(StatementId, MetricCode)`
  for line items; re-running an unchanged payload yields no duplicates.
- After successful normalization, `DerivedMetricRecalculationRequested` is published (existing
  behavior) so the engine recomputes derived/growth metrics.
- The mapping is data-driven: a single governed `CodalIncomeItemMap` / `CodalBalanceItemMap`
  (`ItemId → MetricCode`) drives normalization; adding a metric is a table entry, not a code
  branch.

## Technical Notes

- **Cumulative-period caveat for growth (handoff to `026`):** Codal statements are cumulative
  (3/6/9/12-month). YoY growth compares same-length cumulative periods one fiscal year apart;
  discrete-quarter (QoQ) values must be derived by differencing consecutive cumulative periods.
  This story persists the cumulative facts faithfully and records the period length; the growth
  comparison mechanics live in `026`.
- Map `NET_PROFIT` from Codal `Net income` (143) deliberately, to reuse the existing
  `NET_PROFIT_GROWTH_*` calculators and existing `NetProfitMetricInputSource` expectations.
  Confirm the input source resolves CodalDb-provider rows (it filters by `MetricCode`, so
  provider-agnostic — verify during implementation).
- `CAPITAL` defaults to `Paid capital` (188). CodalDB also has a dedicated `Capitals` table and
  `RegisterCapitalIncrease` (capital-increase history) — richer capital data is out of scope and
  flagged as available.

## Dependencies

- `021`, `022`.
- `005` normalized statement/line-item rows and pipeline; `003`/`015` `MetricCode` semantics.
- Hands metric vocabulary growth off to `015-financial-semantic-layer` (new `MetricCode`
  definitions + aliases) and `026` (growth calculators).
