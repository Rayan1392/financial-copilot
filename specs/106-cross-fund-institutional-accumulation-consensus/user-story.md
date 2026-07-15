# User Story — Cross-Fund Institutional Accumulation Consensus

## Status
`[ ]` Proposed

## Feature
Aggregate monthly disclosed fund activity across multiple funds to identify broad institutional accumulation, distribution, new-entry, exit, and sector-rotation patterns for Iranian securities.

## Story

As a FinancialCopilot user,

I want to know which shares are being accumulated or reduced by multiple investment funds,

so that I can add evidence-backed institutional activity to my research process instead of following one fund in isolation.

## Business Context

A single fund purchase can be idiosyncratic. A stronger research signal may emerge when several independent funds increase a position, open new positions, or rotate toward the same sector during comparable reporting periods.

Cross-fund aggregation must account for different fund sizes, reporting dates, source completeness, duplicate/corrected reports, and unresolved securities. It must not imply that institutional consensus guarantees future returns.

## Dependencies

- Feature `105-fund-portfolio-analytics-and-strategy-signals`.
- Feature `016-derived-feature-foundation`.
- Canonical company/instrument/industry metadata.
- Feature `107` may later add a quality-weighted overlay but is not required for the base consensus score.

## In Scope

- Period alignment across accepted fund reports.
- Per-security holder, buyer, seller, new-entry, and full-exit counts.
- Aggregate beginning/ending quantities and disclosed purchase/sale amounts.
- Aggregate and median portfolio weights/weight changes.
- Breadth-based and size-normalized accumulation/distribution metrics.
- Consecutive-period accumulation/distribution streaks.
- Sector-level fund flow/rotation consensus.
- Base institutional accumulation and distribution scores.
- Confidence based on fund/report coverage, source quality, and security resolution.
- Evidence listing contributing funds without exposing unauthorized operational data.

## Out of Scope

- Claiming beneficial ownership beyond what reports disclose.
- Converting delayed monthly disclosures into real-time order-flow data.
- Fund-quality weighting; Feature 107 owns it.
- Buy/sell recommendations.

## Acceptance Criteria

1. Only accepted, non-superseded report revisions participate.
2. Reports are aligned through an explicit period-window policy; exact dates are retained.
3. One fund contributes at most once per security and period context.
4. Raw purchase amounts and size-normalized weight changes are both available; large funds do not dominate every score solely by size.
5. Preemptive rights, ordinary shares, fund units, derivatives, and commodities are not mixed.
6. Unresolved securities are excluded from company-level consensus and included in coverage diagnostics.
7. Accumulation/distribution score methodology and thresholds are versioned and reproducible.
8. Consecutive-period streaks do not bridge missing or rejected report periods without explicit policy.
9. Every result includes coverage counts, source-report dates, confidence, and contributing evidence.
10. Outputs are labeled institutional disclosure analytics, not real-time smart-money flow or advice.

## Base Score Proposal

The exact weights must be configurable/versioned. An initial base score may use:

```text
25% Buyer breadth among reporting funds
20% New-entry breadth
20% Median positive position-weight change
15% Size-normalized net disclosed deployment
10% Consecutive accumulation periods
10% Position-rank improvement breadth
```

A separate distribution score must use symmetric evidence rather than `100 - accumulation`.

## Data Model Proposal

```text
FundSecurityConsensusSnapshots
- Id
- PeriodKey
- PeriodWindowStart
- PeriodWindowEnd
- ExternalCompanyId
- TradingInstrumentId?
- ReportingFundCount
- HolderFundCount
- BuyerFundCount
- SellerFundCount
- NewEntryFundCount
- FullExitFundCount
- AggregateBeginningQuantity?
- AggregateEndingQuantity?
- AggregatePurchaseAmount?
- AggregateSaleAmount?
- MedianEndingWeight?
- MedianWeightChange?
- ConsecutiveAccumulationPeriods
- AccumulationScore
- DistributionScore
- ConfidenceScore
- CalculationVersion
- EvidenceJson

FundIndustryConsensusSnapshots
- Id
- PeriodKey
- IndustryCode
- ReportingFundCount
- IncreasingFundCount
- DecreasingFundCount
- MedianWeightChange
- AccumulationScore
- DistributionScore
- ConfidenceScore
- CalculationVersion
```

## API Proposal

```http
GET /api/v1/fund-intelligence/consensus/securities
GET /api/v1/fund-intelligence/consensus/securities/{externalCompanyId}
GET /api/v1/fund-intelligence/consensus/industries
```
