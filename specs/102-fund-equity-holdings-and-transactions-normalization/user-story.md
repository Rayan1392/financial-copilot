# User Story — Fund Equity Holdings and Transactions Normalization

## Status
`[ ]` Proposed

## Feature
Normalize beginning positions, purchases, sales, ending positions, market values, and portfolio weights from the equity sections of monthly fund portfolio reports.

## Story

As a FinancialCopilot user,

I want the system to understand what each fund held, bought, sold, newly entered, or fully exited during a reporting period,

so that I can analyze the fund's disclosed equity decisions and later compare institutional activity across funds.

## Business Context

The supplied workbook uses `سهام` and `سهام (2)` for equity, preemptive-right, and investment-fund-unit disclosures. These sheets contain multi-row headers and two position states with period activity between them:

- beginning quantity, cost, and market/net-sale value;
- purchase quantity and cost during the period;
- sale quantity and proceeds during the period;
- ending quantity, market price, cost, market/net-sale value, and share of total fund assets;
- comparative or fiscal-period context in a separate sheet/block.

Names may be company names rather than exchange symbols. Persian spelling differences, preemptive rights, fund units, and unresolved instruments must be handled explicitly.

## Dependencies

- Features `100` and `101`.
- Feature `064-trading-instrument-unification`.
- Existing canonical `Companies` and `TradingInstruments` catalogs.

## In Scope

- Parsing `EquityPortfolioCurrent` and `EquityPortfolioComparative` sheets.
- Beginning and ending position snapshots.
- Period purchase and sale aggregates.
- Cost, market price, market value/net-sale value, and asset-weight fields.
- Security type: ordinary equity, preemptive right, investment-fund unit, or unresolved.
- Canonical company/instrument resolution with evidence and review.
- Current-period versus comparative/fiscal-year-to-date context.
- Quantity and monetary reconciliation.
- New position, full exit, increased, reduced, and unchanged classifications as deterministic raw activity facts.
- Source-row evidence and confidence.

## Out of Scope

- User-owned portfolios or transactions.
- Intraday fund trades; the report is periodic disclosure.
- Inferring undisclosed execution dates or prices.
- Corporate-action reconstruction when the workbook has insufficient evidence.
- Cross-fund scoring, AI rendering, or alerts.

## Acceptance Criteria

1. Every usable equity row is persisted with source report, sheet, row, period context, and canonical security resolution status.
2. Beginning, purchase, sale, and ending values remain separate; no field is overwritten by a derived value.
3. The system distinguishes ordinary shares, preemptive rights, and investment-fund units.
4. Unresolved instruments remain queryable as unresolved source rows but are excluded from company-level consensus until resolved.
5. The quantity equation is checked:

```text
ExpectedEndingQuantity = BeginningQuantity + PurchasedQuantity - SoldQuantity + KnownCorporateActionAdjustment
```

6. A mismatch creates a reconciliation issue and never causes the ending quantity to be replaced.
7. New position requires beginning quantity zero/missing and ending quantity positive; full exit requires beginning positive and ending zero.
8. Purchase/sale amounts are reported as disclosed and are not converted into guessed average prices when a value is missing.
9. Reprocessing the same source revision is idempotent.
10. Existing company resolution behavior is reused and not forked.

## Query Contract Proposal

```http
GET /api/v1/funds/{fundId}/equity-positions?periodEnd=...
GET /api/v1/funds/{fundId}/equity-activity?periodEnd=...
GET /api/v1/symbols/{externalCompanyId}/fund-holdings?periodEnd=...
```

These APIs are read-model inputs for later features; public exposure may be deferred until Feature 108/109.

## Data Model Proposal

```text
FundEquityPositionSnapshots
- Id
- ReportId
- FundId
- PeriodContext
- PositionState (Beginning|Ending)
- SecurityType
- ExternalCompanyId?
- TradingInstrumentId?
- RawSecurityName
- NormalizedSecurityName
- Quantity
- UnitMarketPrice?
- CostAmount?
- MarketOrNetSaleValue?
- WeightOfTotalAssetsPercentage?
- ResolutionStatus
- SourceEvidenceJson

FundEquityPeriodActivities
- Id
- ReportId
- FundId
- PeriodContext
- ExternalCompanyId?
- TradingInstrumentId?
- RawSecurityName
- PurchasedQuantity?
- PurchaseCostAmount?
- SoldQuantity?
- SaleProceedsAmount?
- ActivityClassification
- QuantityReconciliationDifference?
- ReconciliationStatus
- SourceEvidenceJson
```

## Explainability Rules

- Always disclose the report period and that transactions are period aggregates.
- Do not claim exact trade dates, execution prices, or intent.
- Keep disclosed values distinct from calculated comparisons.
