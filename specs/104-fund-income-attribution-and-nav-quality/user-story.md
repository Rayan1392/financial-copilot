# User Story — Fund Income Attribution and NAV Quality

## Status
`[ ]` Proposed

## Feature
Normalize fund investment income, realized/unrealized contribution details, dividend income, deposit income, other income, and adjusted security valuations, then expose deterministic income-quality and valuation-quality evidence.

## Story

As a FinancialCopilot user,

I want to understand where a fund's reported income came from and whether any holdings required adjusted valuation,

so that I can distinguish realized gains, unrealized market appreciation, dividends, commodities, deposits, and valuation-quality risks.

## Business Context

The sample workbook includes an income summary plus detailed sheets for:

- equity dividend income;
- equity price-change/unrealized income;
- equity sale/realized income;
- commodity certificate price-change and sale income;
- bank-deposit income and discount adjustments;
- other income;
- securities whose report-date value was adjusted from the closing market price.

Some sheets contain current-period and cumulative/fiscal-year-to-date blocks. Some derived percentage formulas are broken. The system must normalize disclosed monetary facts and calculate percentages itself only from valid persisted amounts.

## Dependencies

- Features `100`–`103`.
- Feature `009-explainable-results` principles.
- Canonical market/security linkage from Feature `064`.

## In Scope

- Investment income summary by category and period context.
- Security-level equity income attribution: dividend, unrealized/price-change, realized sale, and total.
- Dividend meeting date, shares entitled, DPS, gross income, discount cost, and net income.
- Commodity unrealized and realized income attribution.
- Bank-deposit income summary/detail, discount adjustments, and net income.
- Other income categories.
- Security valuation adjustments: closing price, adjusted price, adjustment percentage, adjusted value, and reason.
- Summary/detail reconciliations.
- Deterministic income-quality and valuation-quality flags/scores with versioned methodology.

## Out of Scope

- Full fund accounting, NAV calculation, unit issuance/redemption accounting, fees, liabilities, or audited financial statements not present in the workbook.
- Claiming a complete independent NAV when only portfolio disclosure is available.
- Performance benchmarking or manager skill; Feature 107 owns historical skill evaluation.
- Recommending a fund.

## Acceptance Criteria

1. Current-period and cumulative/fiscal-year-to-date amounts are stored as different period contexts.
2. Realized, unrealized, dividend, deposit, commodity, and other income are distinct categories.
3. Broken source percentage formulas are not stored as facts; valid percentages are deterministically recalculated from persisted amounts when denominators are available.
4. Security-level detail totals reconcile to category summaries within tolerance or produce an issue.
5. Valuation adjustments preserve both closing and adjusted prices and the disclosed reason.
6. `AdjustedValuationExposurePercentage` is calculated from valid adjusted values and total assets only when both inputs exist.
7. `IncomeQuality` is descriptive and evidence-based; a high unrealized share is not automatically labeled bad.
8. `NavQuality` must be named and described as portfolio-valuation quality, not a full audited NAV opinion.
9. Source errors remain missing/error states, never zero.
10. Every output includes report period, source sheet/detail, calculation version, and reconciliation status.

## Data Model Proposal

```text
FundInvestmentIncomeSummaries
- Id
- ReportId
- FundId
- PeriodContext
- IncomeCategory
- Amount
- PercentageOfTotalIncome?
- PercentageOfTotalAssets?
- SourceEvidenceJson

FundSecurityIncomeAttributions
- Id
- ReportId
- FundId
- PeriodContext
- ExternalCompanyId?
- TradingInstrumentId?
- RawSecurityName
- DividendIncome?
- UnrealizedPriceChangeIncome?
- RealizedSaleIncome?
- TotalIncome?
- ResolutionStatus
- SourceEvidenceJson

FundDividendIncomeDetails
- Id
- ReportId
- FundId
- ExternalCompanyId?
- MeetingDateJalali?
- MeetingDate?
- EntitledQuantity?
- DividendPerShare?
- GrossDividendIncome?
- DiscountCost?
- NetDividendIncome?

FundValuationAdjustments
- Id
- ReportId
- FundId
- ExternalCompanyId?
- TradingInstrumentId?
- Quantity?
- ClosingPrice?
- AdjustedPrice?
- AdjustmentPercentage?
- AdjustedValue?
- Reason
- SourceEvidenceJson

FundPortfolioValuationQualitySnapshots
- Id
- ReportId
- AdjustedSecurityCount
- AdjustedValueAmount?
- AdjustedValueExposurePercentage?
- MaterialReconciliationIssueCount
- QualityStatus
- QualityScore?
- CalculationVersion
- EvidenceJson
```
