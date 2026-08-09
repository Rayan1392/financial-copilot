# Tasks — Fund Income Attribution and NAV Quality

## 1. Domain and Category Taxonomy

- [x] Add governed income categories: EquityDividend, EquityUnrealized, EquityRealized, CommodityUnrealized, CommodityRealized, DepositInterest, OtherIncome, and Unknown.
- [x] Add entities/contracts for income summaries, security attribution, dividend details, deposit income details, commodity income details, valuation adjustments, and portfolio-valuation quality snapshots.
- [x] Define period contexts consistently with Feature 100.
- [x] Make Feature 104 owner of income/detail reconciliation and valuation-quality methodology.

## 2. Summary Sheet Parsing

- [x] Map `درآمدها` into category, amount, source percentage fields, asset percentage, and cumulative amount when valid.
- [x] Map `سرمایه گذاری در سهام` into security-level current/cumulative dividend, unrealized, realized, and total components.
- [x] Map `درآمد گواهی سپرده کالایی` and `درآمد سپرده بانکی` into category summaries.
- [x] Treat `#NAME?` percentage outputs as invalid source formulas and deterministically recompute only when numerator/denominator are valid.
- [x] Persist source totals separately from calculated totals.

## 3. Equity Income Detail

- [x] Parse `درآمد سود سهام` with meeting date, entitled quantity, DPS, gross income, discount cost, and net income for each period context.
- [x] Parse `درآمد ناشی از تغییر قیمت سهام` as unrealized/price-change income detail.
- [x] Parse `درآمد ناشی از فروش سهام` as realized sale income detail.
- [x] Resolve securities through Feature 102 mapping and preserve unresolved raw names.
- [x] Reconcile detail rows to `سرمایه گذاری در سهام` and the top-level income summary.
- [x] Do not interpret a negative value as data corruption when it is economically valid.

## 4. Commodity, Deposit, and Other Income Detail

- [x] Parse `درآمد تغییر قیمت گواهی سپرده` and `درآمد فروش گواهی سپرده` into commodity-level current/cumulative income.
- [x] Parse `درآمد سپرده بانکی 2` into bank-level gross, discount, and net income.
- [x] Parse `سایر درآمدها` into governed other-income codes while preserving raw descriptions.
- [x] Reuse commodity and bank mappings from Feature 103.
- [x] Reconcile all detail categories to their summary sheets.

## 5. Valuation Adjustments

- [x] Parse `تعدیل قیمت` rows with security, quantity, closing price, adjusted price, percentage, adjusted value, and reason.
- [x] Resolve security identity and compare disclosed adjustment percentage with a deterministic calculation.
- [x] Preserve source percentage and calculated percentage separately when both exist.
- [x] Calculate adjustment impact/exposure only from valid total-assets evidence.
- [x] Flag material, unresolved, extreme, or reason-missing adjustments through governed thresholds.
- [x] Never replace canonical market quotes with adjusted fund values outside this fund report context.

## 6. Income and Valuation Quality Methodology

- [x] Calculate income composition percentages from valid persisted category amounts.
- [x] Produce descriptive facts such as realized share, unrealized share, dividend share, commodity share, and deposit share.
- [x] Define `PortfolioValuationQualityStatus` using evidence completeness, material reconciliation issues, adjusted exposure, source errors, and unresolved securities.
- [x] Version all formulas and thresholds.
- [x] Ensure the score does not claim an audit opinion or full NAV correctness.

## 7. Persistence and Read Models

- [x] Add EF Core tables/configuration/migration with natural uniqueness by report, context, category/security/source row.
- [x] Add indexes for fund/period, company, income category, adjustment severity, and reconciliation status.
- [x] Implement idempotent replace-by-source-revision persistence.
- [x] Add repositories/use cases for income overview, top contributors/detractors, dividend details, realized/unrealized composition, and valuation-adjustment risk.

## 8. Reconciliation and Issues

- [x] Reconcile security detail to category summary and category summary to total income using configurable tolerance.
- [x] Keep current and cumulative blocks separate in every reconciliation.
- [x] Generate Feature 101 review items for material mismatches, impossible dates, invalid percentages, unresolved securities, and missing valuation reasons.
- [x] Store reconciliation evidence and differences; never force totals to agree.

## 9. Tests and Acceptance Scenarios

- [x] Unit-test all sheet mappings, signed values, current/cumulative contexts, percentage recalculation, dividend net income, and adjustment calculations.
- [x] Integration-test cross-sheet reconciliation, mapping reuse, idempotent reprocess, and quality snapshot generation.
- [x] Given broken percentage formulas with valid amounts, when processed, then calculated percentages are returned with calculation version and source formula error remains an issue.
- [x] Given a fund whose income is mostly unrealized, when rendered, then the system states the composition without calling it poor performance.
- [x] Given adjusted securities, when queried, then both closing and adjusted values and disclosed reasons are shown.

## Completion Gate

- [x] Keep tasks unchecked until every income/adjustment sheet is covered, current/cumulative contexts reconcile independently, quality labels are non-auditorial, and source facts remain immutable.
