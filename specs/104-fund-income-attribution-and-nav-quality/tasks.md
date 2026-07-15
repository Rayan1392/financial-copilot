# Tasks — Fund Income Attribution and NAV Quality

## 1. Domain and Category Taxonomy

- [ ] Add governed income categories: EquityDividend, EquityUnrealized, EquityRealized, CommodityUnrealized, CommodityRealized, DepositInterest, OtherIncome, and Unknown.
- [ ] Add entities/contracts for income summaries, security attribution, dividend details, deposit income details, commodity income details, valuation adjustments, and portfolio-valuation quality snapshots.
- [ ] Define period contexts consistently with Feature 100.
- [ ] Make Feature 104 owner of income/detail reconciliation and valuation-quality methodology.

## 2. Summary Sheet Parsing

- [ ] Map `درآمدها` into category, amount, source percentage fields, asset percentage, and cumulative amount when valid.
- [ ] Map `سرمایه گذاری در سهام` into security-level current/cumulative dividend, unrealized, realized, and total components.
- [ ] Map `درآمد گواهی سپرده کالایی` and `درآمد سپرده بانکی` into category summaries.
- [ ] Treat `#NAME?` percentage outputs as invalid source formulas and deterministically recompute only when numerator/denominator are valid.
- [ ] Persist source totals separately from calculated totals.

## 3. Equity Income Detail

- [ ] Parse `درآمد سود سهام` with meeting date, entitled quantity, DPS, gross income, discount cost, and net income for each period context.
- [ ] Parse `درآمد ناشی از تغییر قیمت سهام` as unrealized/price-change income detail.
- [ ] Parse `درآمد ناشی از فروش سهام` as realized sale income detail.
- [ ] Resolve securities through Feature 102 mapping and preserve unresolved raw names.
- [ ] Reconcile detail rows to `سرمایه گذاری در سهام` and the top-level income summary.
- [ ] Do not interpret a negative value as data corruption when it is economically valid.

## 4. Commodity, Deposit, and Other Income Detail

- [ ] Parse `درآمد تغییر قیمت گواهی سپرده` and `درآمد فروش گواهی سپرد (2` into commodity-level current/cumulative income.
- [ ] Parse `درآمد سپرده بانکی 2` into bank-level gross, discount, and net income.
- [ ] Parse `سایر درآمدها` into governed other-income codes while preserving raw descriptions.
- [ ] Reuse commodity and bank mappings from Feature 103.
- [ ] Reconcile all detail categories to their summary sheets.

## 5. Valuation Adjustments

- [ ] Parse `تعدیل قیمت` rows with security, quantity, closing price, adjusted price, percentage, adjusted value, and reason.
- [ ] Resolve security identity and compare disclosed adjustment percentage with a deterministic calculation.
- [ ] Preserve source percentage and calculated percentage separately when both exist.
- [ ] Calculate adjustment impact/exposure only from valid total-assets evidence.
- [ ] Flag material, unresolved, extreme, or reason-missing adjustments through governed thresholds.
- [ ] Never replace canonical market quotes with adjusted fund values outside this fund report context.

## 6. Income and Valuation Quality Methodology

- [ ] Calculate income composition percentages from valid persisted category amounts.
- [ ] Produce descriptive facts such as realized share, unrealized share, dividend share, commodity share, and deposit share.
- [ ] Define `PortfolioValuationQualityStatus` using evidence completeness, material reconciliation issues, adjusted exposure, source errors, and unresolved securities.
- [ ] Version all formulas and thresholds.
- [ ] Ensure the score does not claim an audit opinion or full NAV correctness.

## 7. Persistence and Read Models

- [ ] Add EF Core tables/configuration/migration with natural uniqueness by report, context, category/security/source row.
- [ ] Add indexes for fund/period, company, income category, adjustment severity, and reconciliation status.
- [ ] Implement idempotent replace-by-source-revision persistence.
- [ ] Add repositories/use cases for income overview, top contributors/detractors, dividend details, realized/unrealized composition, and valuation-adjustment risk.

## 8. Reconciliation and Issues

- [ ] Reconcile security detail to category summary and category summary to total income using configurable tolerance.
- [ ] Keep current and cumulative blocks separate in every reconciliation.
- [ ] Generate Feature 101 review items for material mismatches, impossible dates, invalid percentages, unresolved securities, and missing valuation reasons.
- [ ] Store reconciliation evidence and differences; never force totals to agree.

## 9. Tests and Acceptance Scenarios

- [ ] Unit-test all sheet mappings, signed values, current/cumulative contexts, percentage recalculation, dividend net income, and adjustment calculations.
- [ ] Integration-test cross-sheet reconciliation, mapping reuse, idempotent reprocess, and quality snapshot generation.
- [ ] Given broken percentage formulas with valid amounts, when processed, then calculated percentages are returned with calculation version and source formula error remains an issue.
- [ ] Given a fund whose income is mostly unrealized, when rendered, then the system states the composition without calling it poor performance.
- [ ] Given adjusted securities, when queried, then both closing and adjusted values and disclosed reasons are shown.

## Completion Gate

- [ ] Keep tasks unchecked until every income/adjustment sheet is covered, current/cumulative contexts reconcile independently, quality labels are non-auditorial, and source facts remain immutable.
