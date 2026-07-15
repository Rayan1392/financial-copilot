# User Story — Fund Portfolio Analytics and Strategy Signals

## Status
`[ ]` Proposed

## Feature
Create deterministic per-fund monthly analytics and strategy-change signals from normalized holdings, asset allocation, transactions, income, derivatives, liquidity, and valuation-quality data.

## Story

As a FinancialCopilot user,

I want a concise explanation of how a fund's portfolio changed during the month,

so that I can identify its largest holdings, new positions, exits, sector rotation, risk appetite, concentration, liquidity, and sources of reported income.

## Business Context

Normalized rows alone do not explain a fund's strategy. Feature 105 transforms Features 102–104 into reproducible, persisted analytics. It must distinguish disclosed facts from calculated interpretations and must never produce buy/sell recommendations.

## Dependencies

- Features `102`, `103`, and `104`.
- Feature `016-derived-feature-foundation` for versioned snapshots.
- Features `030`/`054`/`064` for canonical market liquidity and sector metadata where available.

## In Scope

- Top holdings and concentration metrics.
- Top purchases and sales by disclosed amount and normalized portfolio impact.
- New positions and full exits.
- Position-weight increases/decreases.
- Industry/sector allocation and rotation.
- Equity, deposit/cash, commodity, and derivative exposure.
- Risk-on/risk-off descriptive posture.
- Turnover and net equity deployment.
- Cash buffer and dry-powder indicators.
- Liquidity exposure using market-volume data with explicit availability.
- Derivative hedge-coverage summary.
- Income composition and top contributors/detractors.
- Portfolio-valuation quality summary.
- Deterministic persisted signals with evidence and confidence.

## Out of Scope

- Predicting future returns.
- Ranking the fund against peers; Feature 107 handles track record.
- Cross-fund consensus; Feature 106 owns it.
- Personal investment advice or user portfolio optimization.

## Acceptance Criteria

1. All analytics are calculated from persisted normalized fund data, never directly from Excel at query time.
2. Every snapshot has calculation version, source report revision, period, input completeness, and evidence.
3. Top holdings and concentration use ending market/net-sale values or valid weights.
4. Risk posture is descriptive and based on allocation changes; it is not a market forecast.
5. Sector rotation compares the current report with the prior comparable report for the same fund.
6. Liquidity metrics show unavailable when market-volume data or canonical security resolution is missing.
7. Turnover does not double-count purchases and sales and declares its denominator.
8. Income contribution facts reuse Feature 104 reconciled values.
9. Material valuation-adjustment exposure reduces confidence and is shown as a quality warning.
10. The same inputs and calculation version produce identical ordered outputs.

## Signal Types

```text
NewPosition
FullExit
MaterialPositionIncrease
MaterialPositionReduction
TopPurchase
TopSale
SectorAllocationIncrease
SectorAllocationDecrease
EquityExposureIncrease
CashBufferIncrease
CommodityExposureIncrease
DerivativeHedgeCoverageChange
ConcentrationIncrease
LiquidityRiskIncrease
UnrealizedIncomeConcentration
MaterialValuationAdjustment
```

## Data Model Proposal

```text
FundPortfolioAnalyticsSnapshots
- Id
- FundId
- ReportId
- PeriodEndDate
- PreviousComparableReportId?
- EquityWeight?
- DepositWeight?
- CommodityWeight?
- DerivativeWeight?
- Top5Concentration?
- Top10Concentration?
- HerfindahlIndex?
- PurchaseAmount?
- SaleAmount?
- NetEquityDeploymentAmount?
- TurnoverRatio?
- NewPositionCount
- FullExitCount
- RiskPosture
- LiquidityRiskStatus
- IncomeCompositionJson
- ValuationQualityStatus
- ConfidenceScore
- CalculationVersion
- EvidenceJson

FundPortfolioSignals
- Id
- SnapshotId
- SignalType
- ExternalCompanyId?
- IndustryCode?
- Magnitude
- ImportanceScore
- ConfidenceScore
- Title
- Reason
- EvidenceJson
- DeduplicationKey
```
