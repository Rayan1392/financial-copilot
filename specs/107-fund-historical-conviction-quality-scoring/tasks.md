# Tasks — Fund Historical Conviction Quality Scoring

## 1. Methodology Governance

- [ ] Write a methodology contract covering eligible events, public-availability anchor, fallback anchor, horizons, total-return inputs, benchmarks, corporate actions, suspensions, missing data, delistings, outliers, minimum sample, recency, and confidence.
- [ ] Version methodology separately from calculation implementation.
- [ ] State explicitly that this is disclosed-conviction quality, not complete fund or manager performance.
- [ ] Require methodology/version in every API and AI result.

## 2. Conviction Event Creation

- [ ] Create events only from persisted Feature 105 `NewPosition` and governed material-increase signals.
- [ ] Persist exact report, source revision, signal magnitude, company, industry, and public-availability evidence.
- [ ] Use verified publication timestamp when available; otherwise apply a conservative configured fallback and reduce confidence.
- [ ] Ensure corrected/superseded reports invalidate or supersede affected events/outcomes deterministically.
- [ ] Deduplicate by fund, report, security, event type, and methodology version.

## 3. Historical Price and Benchmark Inputs

- [ ] Resolve canonical adjusted security prices through existing market-data services.
- [ ] Add/verify corporate-action adjustment policy for dividends, splits, capital increases, rights, and symbol changes.
- [ ] Resolve canonical broad-market and industry benchmarks valid at the event date.
- [ ] Select first eligible session after anchor and horizon session by exchange calendar.
- [ ] Handle no-trade/suspension with bounded forward search and explicit unavailable status.
- [ ] Record exact price rows, benchmark rows, dates, and data versions.

## 4. Outcome Calculations

- [ ] Calculate security return, benchmark return, and relative return at each governed horizon.
- [ ] Optionally calculate maximum favorable/adverse excursion from daily adjusted closes with explicit window.
- [ ] Never substitute current prices for missing historical horizon prices.
- [ ] Store pending outcomes until horizon becomes due; update append-only/versioned outcome rows.
- [ ] Recalculate only when input data is corrected/versioned and retain prior calculation evidence where policy requires.

## 5. Quality Score

- [ ] Aggregate outcomes by fund, horizon, and optional industry.
- [ ] Calculate median relative return, positive-relative rate, dispersion, downside metric, eligible/observed counts, and recency-weighted statistics.
- [ ] Enforce minimum sample and coverage; return unavailable when insufficient.
- [ ] Cap outlier influence and document robust-statistics policy.
- [ ] Calculate quality and confidence separately.
- [ ] Avoid a single score across unrelated sectors when sector-specific evidence is materially different.

## 6. Quality-Weighted Consensus Overlay

- [ ] Extend Feature 106 with optional `QualityWeightedAccumulationScore` and version only after quality snapshots are available.
- [ ] Cap each fund's quality weight and require minimum sample/confidence.
- [ ] Keep base breadth/flow score visible and unchanged.
- [ ] Show which funds contributed and the quality evidence used.
- [ ] Return unavailable rather than assigning neutral quality to unknown funds unless policy explicitly defines it.

## 7. Persistence, Workers, and APIs

- [ ] Add EF Core tables/configuration/migration for events, outcomes, and quality snapshots.
- [ ] Add indexes for due horizon, fund, company, industry, as-of date, score, and methodology version.
- [ ] Add a due-outcome worker with lease, bounded concurrency, retry/backoff, and poison handling.
- [ ] Add recalculation orchestration for corrected market data, report revisions, and methodology versions.
- [ ] Add APIs for fund track record, event detail, sector specialization, and quality-weighted consensus.

## 8. Explainability, Compliance, and Tests

- [ ] Render sample size, event definition, anchor rule, horizons, benchmark, median, dispersion, coverage, and limitations.
- [ ] Prohibit phrases such as guaranteed, proven winner, or expected return.
- [ ] Unit-test session anchors, publication fallback, corporate actions, benchmark selection, minimum samples, outlier caps, and score boundaries.
- [ ] Integration-test pending/due outcomes, report correction, market-data correction, sector scores, and weighted-consensus caps.
- [ ] Backtest methodology on point-in-time fixtures and verify no future data enters event creation or anchor selection.
- [ ] Given a historically strong result based on only one event, return insufficient sample rather than a high quality score.

## Completion Gate

- [ ] Keep tasks unchecked until anti-look-ahead tests, methodology disclosure, minimum-sample behavior, benchmark/corporate-action correctness, and separate base-versus-quality-weighted consensus are verified.
