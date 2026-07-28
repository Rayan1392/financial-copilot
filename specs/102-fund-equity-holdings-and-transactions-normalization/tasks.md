# Tasks — Fund Equity Holdings and Transactions Normalization

## 1. Contracts and Ownership

- [ ] Add `FundEquityPositionSnapshot`, `FundEquityPeriodActivity`, `FundSecurityResolutionStatus`, `FundEquityActivityClassification`, and `FundPositionState` domain/read-model contracts.
- [ ] Define a section normalizer interface consumed by Feature 100 parser envelopes.
- [ ] Make Feature 102 owner of normalized equity/preemptive-right/fund-unit rows and quantity reconciliation.
- [ ] Do not duplicate canonical company or trading-instrument catalogs.

## 2. Header and Block Mapping

- [ ] Define versioned mappings for `سهام` and `سهام (2)` using header-path recognition rather than fixed cell addresses alone.
- [ ] Detect beginning block, period changes, and ending block.
- [ ] Map quantity, cost, market/net-sale value, purchase quantity/cost, sale quantity/proceeds, ending price, ending values, and total-assets percentage.
- [ ] Detect total/subtotal/footer rows and exclude them from security rows while persisting section totals for reconciliation.
- [ ] Detect current-period and comparative/fiscal context independently from sheet order.
- [ ] Emit layout mismatch issues when required headers are missing or duplicated.

## 3. Persian Security Normalization

- [ ] Normalize Persian characters, whitespace, underscores, ZWNJ, punctuation, parenthetical text, and common legal/company suffixes without losing raw display text.
- [ ] Detect likely preemptive-right names and investment-fund units through governed rules and instrument metadata.
- [ ] Reuse canonical company resolver and `TradingInstruments` lookup by symbol, ISIN, instrument code, and governed name aliases.
- [ ] Persist all candidates and resolution evidence for ambiguous/unresolved rows.
- [ ] Feed unresolved rows into Feature 101 mapping review; never ask an LLM to approve identity.

## 4. Persistence and Idempotency

- [ ] Add EF Core tables/configuration/migration for position snapshots and period activities.
- [ ] Add natural uniqueness by report, period context, position state/activity, source logical row, and resolved/raw security identity.
- [ ] Add indexes for fund/period, company/period, instrument, activity classification, security type, and unresolved status.
- [ ] Use replace-by-source-revision or transactional upsert so reprocessing does not duplicate rows.
- [ ] Preserve raw names and evidence even after a governed mapping is resolved.

## 5. Numeric and Unit Rules

- [ ] Parse quantities as decimal/integer-compatible values without floating-point loss.
- [ ] Parse all disclosed monetary values as Rials unless the source explicitly states another unit.
- [ ] Parse percentages as percentage points, not fractions; store scale policy explicitly.
- [ ] Treat blank, zero, and source error as distinct states.
- [ ] Reject impossible negative quantities except where the source format explicitly defines signed period changes; create an issue otherwise.

## 6. Reconciliation and Activity Classification

- [ ] Calculate expected ending quantity from beginning, purchases, sales, and known adjustment.
- [ ] Add a pluggable corporate-action adjustment input; default to null/unknown rather than zero when mismatch suggests unavailable actions.
- [ ] Persist reconciliation difference/status and source section totals.
- [ ] Classify `NewPosition`, `FullExit`, `Increased`, `Reduced`, `Unchanged`, and `Unreconciled` deterministically.
- [ ] Do not infer conviction or recommendation in this feature.
- [ ] Reconcile ending market value and asset-weight totals against available summary/detail totals with configured tolerance.

## 7. Repositories and Read Use Cases

- [ ] Add repositories for fund-period positions, fund-period activity, company fund holders, and unresolved rows.
- [ ] Implement `GetFundEquityPositionsUseCase`, `GetFundEquityActivityUseCase`, and `GetCompanyFundHoldingsUseCase`.
- [ ] Support stable cursor pagination and filters for period, activity type, resolved status, security type, and minimum weight.
- [ ] Return source report id, period, freshness/import time, and reconciliation status.

## 8. Observability

- [ ] Emit row counts, resolved/unresolved rates, new/full-exit counts, reconciliation mismatches, section-total differences, and processing duration.
- [ ] Trace each row to report revision, parser version, mapping decision version, and source address.
- [ ] Log normalized identifiers and issue codes, not unrestricted workbook row contents.

## 9. Tests and Acceptance Scenarios

- [ ] Create sanitized fixtures for normal rows, preemptive rights, fund units, blank values, formula errors, totals, duplicate names, and ambiguous security resolution.
- [ ] Unit-test header mapping, current/comparative block selection, numeric parsing, activity classification, and reconciliation.
- [ ] Integration-test idempotent persistence, corrected report revision, mapping resolution/reprocess, and company/instrument joins.
- [ ] Given beginning zero and ending positive, when parsed, then the activity is `NewPosition` with disclosed purchases preserved.
- [ ] Given beginning positive and ending zero, when parsed, then the activity is `FullExit` without inventing a sale date.
- [ ] Given a quantity mismatch, when parsed, then the source values remain unchanged and the row is marked unreconciled.

## Completion Gate

- [ ] Keep tasks unchecked until both equity sheets parse, security resolution is governed, quantity/value reconciliations are reproducible, and all row-level evidence survives reprocessing.
