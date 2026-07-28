# User Story — Fund Historical Conviction Quality Scoring

## Status
`[ ]` Proposed

## Feature
Evaluate the historical outcomes of disclosed fund entries and material position increases using point-in-time market data, producing transparent fund-level and sector-level conviction-quality analytics.

## Story

As a FinancialCopilot user,

I want to identify funds that have historically entered or increased positions before meaningful relative performance,

so that I can use their future disclosed activity as one additional research signal while understanding the methodology and limitations.

## Business Context

The product concept includes finding funds that accumulated a successful company before the broader market recognized its growth, then monitoring those funds' later disclosures. This requires a rigorous point-in-time evaluation rather than selecting famous winners after the fact.

The workbook does not contain full fund return history or manager-tenure data. Therefore this feature scores the historical quality of disclosed conviction events, not the personal skill of a named manager and not the total performance of a fund.

## Dependencies

- Features `105` and `106`.
- Features `030`, `054`, and `064` for canonical historical market data, sessions, instruments, and corporate actions where available.
- Industry/benchmark metadata.

## In Scope

- Eligible conviction events: new position and material position increase.
- Event anchor policy based on report period end and public-availability timestamp when available.
- Forward absolute and benchmark-relative returns at governed horizons such as 1, 3, 6, and 12 months.
- Maximum favorable/adverse excursion as descriptive analytics when data coverage allows.
- Sector/industry-specific fund conviction quality.
- Sample-size, coverage, dispersion, hit-rate, and median-relative-return evidence.
- Time-decayed fund conviction quality score.
- Optional quality-weighted overlay for Feature 106 consensus.
- Methodology versioning and anti-look-ahead safeguards.

## Out of Scope

- Total fund NAV performance, fees, cash flows, or Sharpe ratio without complete fund return data.
- Naming a manager as skilled unless verified manager-tenure data is added later.
- Backtested investment performance claims or simulated portfolio returns in marketing copy.
- Treating score as advice or guaranteed alpha.

## Acceptance Criteria

1. The event anchor never predates the information's public availability when a publication timestamp exists.
2. If only report period end is known, the methodology explicitly labels publication-time uncertainty and uses a conservative anchor policy.
3. Future returns use point-in-time adjusted market data and exchange sessions.
4. Corporate actions, suspensions, missing quotes, and delistings follow explicit versioned policies.
5. Benchmark-relative results use the canonical market or industry benchmark available at the event time.
6. A score is unavailable below a governed minimum sample size.
7. Score outputs show sample count, eligible/observed count, horizons, median/dispersion, and confidence.
8. A fund can have different quality scores by industry and horizon.
9. Feature 106 base consensus remains unchanged; quality-weighted consensus is a separate field/version.
10. Historical results are descriptive and must not be phrased as future-return predictions.

## Methodology Proposal

```text
ConvictionEvent = NewPosition OR MaterialPositionIncrease
Anchor = first eligible market close after verified publication timestamp
Fallback Anchor = governed conservative date when publication timestamp is unavailable
Outcome = Security total return - benchmark total return over horizon

FundConvictionQualityScore components:
- Median benchmark-relative return
- Positive-relative-return rate
- Downside/adverse-excursion control
- Sample size and coverage confidence
- Recency weighting
- Cross-sector consistency or sector-specialist evidence
```

## Data Model Proposal

```text
FundConvictionEvents
- Id
- FundId
- ReportId
- SignalId
- ExternalCompanyId
- IndustryCode?
- EventType
- EventMagnitude
- ReportPeriodEndDate
- PublicAvailabilityAtUtc?
- AnchorTradingDate
- AnchorPrice
- MethodologyVersion
- EvidenceJson

FundConvictionEventOutcomes
- Id
- ConvictionEventId
- HorizonCode
- HorizonTradingDate?
- SecurityReturnPercentage?
- BenchmarkReturnPercentage?
- RelativeReturnPercentage?
- MaximumFavorableExcursionPercentage?
- MaximumAdverseExcursionPercentage?
- OutcomeStatus
- CalculationVersion

FundConvictionQualitySnapshots
- Id
- FundId
- IndustryCode?
- AsOfDate
- HorizonCode
- EligibleEventCount
- ObservedEventCount
- MedianRelativeReturn?
- PositiveRelativeRate?
- Dispersion?
- QualityScore?
- ConfidenceScore
- MethodologyVersion
- EvidenceJson
```
