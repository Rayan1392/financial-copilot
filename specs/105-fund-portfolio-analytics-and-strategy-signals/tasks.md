# Tasks — Fund Portfolio Analytics and Strategy Signals

## 1. Analytics Contracts and Calculation Policy

- [ ] Add snapshot/signal entities, DTOs, repositories, calculators, calculation-version registry, and deterministic ordering policy.
- [ ] Define input-completeness dimensions for equity, allocation, non-equity, income, market liquidity, and valuation quality.
- [ ] Make Feature 105 owner of per-fund analytics/signals; do not publish notifications here.
- [ ] Register calculations through Feature 016 patterns rather than ad hoc query-time logic.

## 2. Comparable Period Selection

- [ ] Select the prior comparable report for the same canonical fund/provider/report type using period order and accepted source revision.
- [ ] Skip superseded, failed, or unresolved reports.
- [ ] Define behavior for first report, irregular reporting dates, multiple reports in one month, and gaps.
- [ ] Record selected previous report and selection policy version in evidence.
- [ ] Never compare current-period block with cumulative block.

## 3. Holdings and Activity Analytics

- [ ] Calculate top holdings, top 5/top 10 concentration, HHI, and count of material positions.
- [ ] Rank purchases and sales by disclosed amount, quantity change, and portfolio-weight impact using separate fields.
- [ ] Produce new-position/full-exit/increase/reduction lists from Feature 102 facts.
- [ ] Calculate net equity deployment and explicitly label it as disclosed purchases minus disclosed sale proceeds, not net cash flow of the fund.
- [ ] Define materiality thresholds by absolute amount, asset-weight change, and percentile with configuration/versioning.

## 4. Sector and Strategy Rotation

- [ ] Aggregate ending holdings by canonical industry/sector.
- [ ] Compare sector weights with prior comparable report and persist increases/decreases.
- [ ] Keep unresolved securities in an `Unknown` bucket and reduce confidence.
- [ ] Calculate equity/deposit/commodity/derivative allocation changes.
- [ ] Define descriptive risk posture (`MoreRiskOn`, `Stable`, `MoreDefensive`, `Unknown`) from governed allocation rules.
- [ ] Avoid interpreting every cash decrease as bullish when issue/redemption cash-flow data is unavailable.

## 5. Turnover, Cash Buffer, and Liquidity

- [ ] Define and version turnover denominator, e.g. average disclosed portfolio market value where available.
- [ ] Calculate purchases, sales, gross turnover, and net deployment separately.
- [ ] Calculate deposit/cash buffer from Feature 103 and its period change.
- [ ] Join canonical market-volume data to estimate liquidation days for resolved equity positions using a configurable participation rate.
- [ ] Produce weighted liquidity-risk metrics and explicit missing-data coverage.
- [ ] Handle suspended/no-volume instruments without dividing by zero or inventing liquidity.

## 6. Derivative, Income, and Valuation Analytics

- [ ] Summarize protective-put coverage and uncovered/unknown underlying exposure.
- [ ] Summarize income composition and top security contributors/detractors from Feature 104.
- [ ] Calculate unrealized-income concentration and realized/dividend composition without value judgment.
- [ ] Surface valuation-adjustment count, exposure, reasons, and quality status.
- [ ] Propagate reconciliation failures and source errors into confidence.

## 7. Signal Generation and Scoring

- [ ] Implement the governed signal types listed in the story.
- [ ] Define magnitude, importance, and confidence independently.
- [ ] Create stable deduplication keys by fund, report, signal type, subject, and calculation version.
- [ ] Persist exact inputs/thresholds/baseline in evidence.
- [ ] Do not use recommendation language.

## 8. Read APIs

- [ ] Add `GET /api/v1/funds/{fundId}/portfolio-intelligence` with period selection.
- [ ] Add endpoints or internal queries for holdings, activity, allocation, sectors, income attribution, risk, and source evidence.
- [ ] Provide stable pagination for detailed lists and deterministic rankings/ties.
- [ ] Return report freshness/import time, source revision, confidence, reconciliation, and calculation version.

## 9. Recalculation and Orchestration

- [ ] Trigger analytics after all required normalized sections for a report reach terminal state.
- [ ] Allow recalculation after mapping, market-data, or calculation-version changes.
- [ ] Make recalculation idempotent, lease-protected, and failure-isolated.
- [ ] Do not roll back normalized source data if analytics calculation fails.

## 10. Tests and Acceptance Scenarios

- [ ] Unit-test every calculation, threshold boundary, tie-break, missing-data branch, and confidence factor.
- [ ] Integration-test prior-report selection, industry aggregation, liquidity joins, recalculation, and deterministic persistence.
- [ ] Given unchanged holdings but higher equity prices, ensure the system does not label the position as purchased unless disclosed activity/quantity supports it.
- [ ] Given lower deposits and high purchases but unavailable unit issuance/redemption data, state increased deployment while keeping market-intent confidence bounded.
- [ ] Given source reconciliation failures, show analytics with reduced confidence or unavailable status rather than fabricated precision.

## Completion Gate

- [ ] Keep tasks unchecked until all analytics are persisted, point-in-time reproducible, missing-data aware, non-recommendatory, and verified against at least two consecutive fund-report fixtures.
