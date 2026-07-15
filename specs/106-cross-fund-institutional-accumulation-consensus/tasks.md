# Tasks — Cross-Fund Institutional Accumulation Consensus

## 1. Period Universe and Eligibility

- [ ] Define `FundConsensusPeriodPolicy` for Jalali month key, date window, accepted revision, late report, and irregular-date handling.
- [ ] Build an eligible-report universe that excludes failed, superseded, duplicate, unresolved-fund, and materially incomplete reports.
- [ ] Store exact report ids/dates used in every snapshot.
- [ ] Ensure one canonical fund contributes once per period/security.
- [ ] Define minimum reporting-fund coverage before a score is available.

## 2. Security-Level Aggregation

- [ ] Aggregate holder, buyer, seller, new-entry, full-exit, increased, and reduced counts from Features 102/105.
- [ ] Aggregate beginning/ending quantities and purchase/sale amounts only within the same canonical security type.
- [ ] Calculate ending weight, weight change, position-rank change, and fund-size-normalized flow distributions.
- [ ] Preserve both total and median/percentile measures.
- [ ] Exclude unresolved securities from canonical company results and report their excluded count/value.

## 3. Sector-Level Aggregation

- [ ] Aggregate fund sector-weight increases/decreases from Feature 105.
- [ ] Calculate breadth, median change, and normalized rotation magnitude.
- [ ] Keep Unknown sector coverage explicit.
- [ ] Prevent double-counting of conglomerates or multi-industry classifications by using the canonical primary industry policy unless a governed multi-label allocation exists.

## 4. Streak and Trend Logic

- [ ] Calculate consecutive accumulation/distribution periods using contiguous accepted period keys.
- [ ] Define behavior for a fund missing one report, a security absent from one report, and late/corrected submissions.
- [ ] Persist prior snapshot linkage and calculation version.
- [ ] Add 1-, 3-, 6-, and 12-period trend views when enough data exists.
- [ ] Do not fill missing periods with zero activity.

## 5. Scoring and Confidence

- [ ] Implement independently versioned accumulation and distribution score calculators.
- [ ] Use buyer/seller breadth, entries/exits, median weight change, normalized deployment, streak, and rank change.
- [ ] Cap the influence of any one fund.
- [ ] Calculate confidence from reporting-fund coverage, normalized-row completeness, resolution rate, report recency, and reconciliation quality.
- [ ] Expose all score components and thresholds in evidence.
- [ ] Reserve a future optional quality-weighted score field for Feature 107 without changing the base score.

## 6. Persistence and Recalculation

- [ ] Add EF Core tables/configuration/migration for security and industry consensus snapshots.
- [ ] Add unique keys by period, subject, and calculation version.
- [ ] Add indexes for score, company, industry, period, coverage, and streak.
- [ ] Trigger recalculation when an eligible report is accepted, superseded, reprocessed, or mapped.
- [ ] Recalculate affected periods/subjects idempotently with lease and bounded concurrency.

## 7. Read APIs and Explainability

- [ ] Add paginated/ranked endpoints with filters for period, industry, minimum reporting funds, score, entry/exit breadth, and confidence.
- [ ] Add security detail showing contributing funds, their disclosed activity class, weight change, report date, and source report.
- [ ] Add industry detail showing sector rotation breadth and top contributing securities.
- [ ] Use deterministic tie-breaks and cursor pagination.
- [ ] Label data as delayed monthly disclosures.

## 8. Tests and Acceptance Scenarios

- [ ] Unit-test period eligibility, one-fund-one-vote, normalized-flow caps, score boundaries, streak gaps, and confidence.
- [ ] Integration-test multiple funds, corrected reports, late reports, unresolved securities, and affected-period recalculation.
- [ ] Given one very large fund buying and several funds selling, verify that raw amount and breadth metrics can diverge and both are shown.
- [ ] Given five funds open new positions across comparable reports, verify new-entry breadth and evidence are reproducible.
- [ ] Given insufficient fund coverage, return unavailable/low-confidence rather than a strong score.

## Completion Gate

- [ ] Keep tasks unchecked until aggregation is period-correct, size-normalized, security-type safe, coverage-aware, reproducible, and explicitly non-real-time.
