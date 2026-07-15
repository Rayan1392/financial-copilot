# Tasks — Frontend Fund Intelligence Experience

## 1. API Client and Types

- [ ] Add typed server functions/clients for fund directory, report periods, fund analytics, details, consensus, conviction quality, and evidence.
- [ ] Reuse authenticated backend bridge, error normalization, and request correlation patterns.
- [ ] Add stable frontend types matching backend contracts; do not derive financial calculations in React.
- [ ] Enforce bounded paging/filter parameters.

## 2. Fund Directory

- [ ] Add searchable/filterable fund directory with canonical name, symbol/type when available, latest report date, latest import status, and coverage badge.
- [ ] Support pagination and stable URL query state.
- [ ] Distinguish unavailable latest report from a fund with zero holdings.
- [ ] Add navigation entry labeled `صندوق‌ها` or `اطلاعات صندوق‌ها`, not `پرتفوی من`.

## 3. Fund Detail Shell

- [ ] Add fund header with canonical identity, report period selector, source/provider, revision, freshness, confidence, and report-status badges.
- [ ] Add tabs for Overview, Holdings, Activity, Sectors, Risk & Liquidity, Income, Valuation Quality, and Historical Conviction.
- [ ] Preserve selected period/tab in URL.
- [ ] Add authorized source-report/evidence action.
- [ ] Add AI follow-up action carrying canonical fund/report context.

## 4. Overview and Allocation

- [ ] Render allocation cards/chart for equity, deposits, commodities, derivatives, and unknown/other.
- [ ] Render risk-posture change with evidence and non-predictive wording.
- [ ] Show top holdings, top purchases/sales, new positions, exits, concentration, turnover, cash buffer, and data-quality summary.
- [ ] Display unavailable metrics explicitly when required source sections are missing.

## 5. Holdings and Activity Tables

- [ ] Add sortable/paginated holdings with security, beginning/ending quantity/value, ending weight, resolution, and reconciliation state.
- [ ] Add activity table with purchases, sales, net quantity change, classification, and source evidence.
- [ ] Add filters for new position, full exit, increased, reduced, unresolved, industry, and minimum weight.
- [ ] Link resolved securities to symbol pages and offer Feature 085 follow action.
- [ ] Never imply exact execution date/price.

## 6. Sector, Risk, Income, and Valuation Views

- [ ] Render sector allocation and period-over-period rotation with unknown bucket.
- [ ] Render liquidity coverage/risk, deposit concentration, commodity exposure, derivatives, and hedge coverage.
- [ ] Render realized/unrealized/dividend/commodity/deposit/other income composition and top contributors/detractors.
- [ ] Render adjusted securities with closing/adjusted price, exposure, reason, and valuation-quality status.
- [ ] Add methodology/info tooltips for turnover, liquidity, income quality, and valuation quality.

## 7. Symbol Institutional Activity

- [ ] Add a symbol-page section showing fund holders, buyers, sellers, new entries, exits, median weight change, consensus scores, coverage, and report window.
- [ ] Add contributing-fund detail drawer with report dates and evidence.
- [ ] Label the data as delayed monthly fund disclosure.
- [ ] Keep ordinary shares/preemptive rights/derivatives separated.

## 8. Market Consensus Radar

- [ ] Add ranked accumulation/distribution securities and sector rotation views.
- [ ] Add filters for period, minimum reporting funds, industry, score, confidence, new entries, and streak.
- [ ] Show base and quality-weighted scores as separate modes with methodology.
- [ ] Use deterministic backend order and cursor pagination.
- [ ] Add create-tracker/ask-AI/follow-symbol actions.

## 9. Historical Conviction UX

- [ ] Render fund-level and industry-level quality snapshots by horizon.
- [ ] Show eligible/observed samples, median relative return, positive rate, dispersion, confidence, benchmark, methodology version, and limitations.
- [ ] Show unavailable state below minimum sample.
- [ ] Avoid promotional win-rate styling or future-performance claims.

## 10. States, Accessibility, and Tests

- [ ] Implement skeleton, empty, partial, stale, low-confidence, unresolved, unauthorized, and failure states.
- [ ] Use Persian RTL formatting, accessible tables/charts, keyboard navigation, semantic labels, and responsive design.
- [ ] Add component tests for all states and Playwright flows for fund search, period change, evidence, symbol activity, consensus filters, AI handoff, and follow action.
- [ ] Regression-test existing chat, scanner, followed-symbol, and market-insight routes.

## Completion Gate

- [ ] Keep tasks unchecked until fund/symbol/market surfaces are complete, calculations remain backend-owned, delayed disclosure and evidence are visible, and responsive RTL accessibility tests pass.
