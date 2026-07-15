# User Story — Fund Non-Equity Assets and Derivatives Normalization

## Status
`[ ]` Proposed

## Feature
Normalize fund asset allocation, commodity certificates, bank deposits, protective puts, and exchange-traded option positions from monthly portfolio reports.

## Story

As a FinancialCopilot user,

I want to see a fund's non-equity exposures and hedging instruments,

so that I can understand its cash buffer, commodity allocation, derivative risk, and overall risk posture instead of evaluating only its stock holdings.

## Business Context

The sample workbook contains:

- `سرمایه گذاری ها`: high-level asset allocation;
- `اوراق مشتقه`: purchased protective-put statistics and option positions;
- `سرمایه‌گذاری درگواهی سپرده`: gold bullion, copper cathode, rebar, and similar certificates;
- `(2)سپرده`: beginning, increases, decreases, ending balances, and asset weights by bank.

These sections are economically different and must not be forced into the equity-holding schema.

## Dependencies

- Features `100` and `101`.
- Feature `064` for tradable instrument linkage where applicable.
- Existing market data for optional underlying references; valuation remains source-bound.

## In Scope

- Asset-allocation summary by disclosed asset class.
- Commodity-certificate beginning/activity/ending positions.
- Bank-deposit balances and period movements.
- Protective-put statistics, including underlying, quantity, strike, exercise date, and effective return where disclosed.
- Exchange-traded option positions, including option type, underlying, quantity, strike, expiry, cost/value, and portfolio weight when present.
- Canonical bank, commodity, derivative, and underlying identifiers where deterministically resolvable.
- Reconciliation of detail totals to asset-allocation summary.
- Explicit unknown/unresolved states.

## Out of Scope

- Option Greeks or implied volatility unless required market inputs are available in a later feature.
- Guessing whether an option is a hedge or speculation solely from its existence.
- Bank credit scoring.
- Real-time commodity or derivative valuation.
- Income attribution; Feature 104 owns it.

## Acceptance Criteria

1. Summary allocation and detailed positions are stored separately and linked to the same report/period.
2. Gold, copper, rebar, and future commodity certificates are represented through a generic commodity-certificate model.
3. Deposit movements preserve beginning, increase, decrease, ending, and total-assets weight.
4. Protective puts and ordinary options are distinct derivative categories.
5. Underlying resolution is canonical when possible and remains unresolved with evidence otherwise.
6. Jalali expiry/exercise dates are preserved and converted without changing the disclosed date.
7. The system does not call every derivative position a hedge; it calculates hedge-coverage indicators only when underlying holdings and contract terms support it.
8. Summary/detail differences beyond tolerance create issues.
9. Blank values, zero values, and source errors remain distinct.
10. Reprocessing is idempotent and source-traceable.

## Data Model Proposal

```text
FundAssetAllocationSnapshots
- Id
- ReportId
- FundId
- PeriodContext
- AssetClass
- CostAmount?
- MarketOrNetSaleValue?
- WeightOfTotalAssetsPercentage?
- SourceEvidenceJson

FundCommodityCertificatePositions
- Id
- ReportId
- FundId
- PeriodContext
- CommodityCode?
- TradingInstrumentId?
- RawInstrumentName
- BeginningQuantity?
- BeginningCostAmount?
- BeginningMarketValue?
- PurchasedQuantity?
- PurchaseCostAmount?
- SoldQuantity?
- SaleProceedsAmount?
- EndingQuantity?
- EndingUnitPrice?
- EndingCostAmount?
- EndingMarketValue?
- WeightOfTotalAssetsPercentage?
- ResolutionStatus

FundBankDepositPositions
- Id
- ReportId
- FundId
- PeriodContext
- BankCode?
- RawBankName
- BeginningBalance?
- IncreaseAmount?
- DecreaseAmount?
- EndingBalance?
- WeightOfTotalAssetsPercentage?
- ResolutionStatus

FundDerivativePositions
- Id
- ReportId
- FundId
- PeriodContext
- DerivativeType
- TradingInstrumentId?
- UnderlyingExternalCompanyId?
- UnderlyingTradingInstrumentId?
- RawInstrumentName
- PositionSide?
- ContractQuantity?
- UnderlyingCoverageQuantity?
- StrikePrice?
- ExpiryOrExerciseJalali?
- ExpiryOrExerciseDate?
- EffectiveReturnPercentage?
- CostAmount?
- MarketValue?
- WeightOfTotalAssetsPercentage?
- ResolutionStatus
```
