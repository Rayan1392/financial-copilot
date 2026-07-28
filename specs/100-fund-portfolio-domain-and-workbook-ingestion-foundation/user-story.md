# User Story — Fund Portfolio Domain and Workbook Ingestion Foundation

## Status
`[ ]` Proposed

## Feature
Introduce the canonical investment-fund portfolio-report domain and a versioned Excel workbook ingestion framework for Iranian monthly fund portfolio statements.

## Story

As a FinancialCopilot data administrator,

I want to ingest a monthly investment-fund portfolio workbook into a canonical, source-traceable report model,

so that later features can normalize holdings, income, risk exposures, and institutional activity without depending directly on fragile Excel formulas or sheet coordinates.

## Business Context

FinancialCopilot already ingests company fundamentals, financial statements, monthly production/sales data, and market data. It does not yet have a canonical model for investment funds or their monthly portfolio statements.

The supplied sample workbook, `صورت وضعیت پرتفوی منتهی به 1405.04.16.xlsx`, contains 20 sheets and demonstrates the main ingestion challenges:

- merged and multi-row headers;
- Persian text and Jalali dates;
- current-period and cumulative/comparative blocks in the same sheet;
- formulas whose cached values contain `#NAME?`, `#REF!`, or `#N/A`;
- summary sheets that reference detail sheets;
- different instrument classes, including equities, preemptive rights, derivatives, commodity certificates, and bank deposits;
- report totals that must be reconciled rather than trusted blindly;
- sheet names that may contain spaces, ZWNJ characters, suffixes, or truncated text.

This feature establishes the additive foundation only. It must preserve all existing FinancialCopilot behavior and must not modify the existing company-fundamentals ingestion paths.

## Dependencies

- Feature `003-financial-domain-model`.
- Feature `004-third-party-data-provider-abstraction`.
- Feature `005-data-ingestion-and-normalization`.
- Feature `012-admin-data-operations`.
- Feature `018-ai-observability-and-telemetry`.
- Feature `051-noavaran-archive-and-current-api-strategy` for source provenance conventions.
- Feature `064-trading-instrument-unification` for canonical security linkage.

## In Scope

- Canonical investment-fund identity.
- Canonical monthly portfolio-report header and lifecycle.
- Raw workbook storage reference, SHA-256 hash, parser-profile version, and source provenance.
- Workbook inspection without relying on Excel recalculation.
- Versioned sheet classification and section detection.
- Recognition of the sample workbook's complete sheet inventory.
- Jalali date preservation plus Gregorian conversion when valid.
- Rial/percentage/quantity parsing with Persian and Arabic digit normalization.
- Source evidence at workbook, sheet, row, and field level.
- Structured extraction issues and partial-success status.
- Idempotent report ingestion by canonical fund, source report identity, reporting period, and file hash.
- Internal contracts that later normalization features can consume.

## Out of Scope

- Detailed equity row persistence; Feature 102 owns it.
- Non-equity and derivative persistence; Feature 103 owns it.
- Income and valuation-adjustment persistence; Feature 104 owns it.
- Portfolio analytics, consensus scores, AI questions, frontend screens, or alerts.
- Guessing or scraping a Codal endpoint that has not been supplied or verified.
- Rewriting or repairing the source workbook.

## Workbook Classification Requirements

The parser profile must recognize these logical sheet types even when normalized names vary:

| Sample sheet | Logical classification |
|---|---|
| `تیتر` | ReportCover |
| `0` | FormulaOrControlSheetIgnored |
| `سرمایه گذاری ها` | AssetAllocationSummary |
| `سهام` | EquityPortfolioCurrent |
| `سهام (2)` | EquityPortfolioComparative |
| `اوراق مشتقه` | DerivativePositions |
| `سرمایه‌گذاری درگواهی سپرده` | CommodityCertificatePositions |
| `(2)سپرده` | BankDepositPositions |
| `تعدیل قیمت` | ValuationAdjustments |
| `درآمدها` | InvestmentIncomeSummary |
| `سرمایه گذاری در سهام` | EquityIncomeSummary |
| `درآمد سود سهام` | DividendIncomeDetail |
| `درآمد ناشی از تغییر قیمت سهام` | EquityUnrealizedIncomeDetail |
| `درآمد ناشی از فروش سهام` | EquityRealizedIncomeDetail |
| `درآمد گواهی سپرده کالایی` | CommodityIncomeSummary |
| `درآمد تغییر قیمت گواهی سپرده` | CommodityUnrealizedIncomeDetail |
| `درآمد فروش گواهی سپرد (2` | CommodityRealizedIncomeDetail |
| `درآمد سپرده بانکی` | DepositIncomeSummary |
| `درآمد سپرده بانکی 2` | DepositIncomeDetail |
| `سایر درآمدها` | OtherIncomeDetail |

The exact display name must be stored as source metadata. Classification must use normalized aliases and structural header evidence rather than exact-string matching alone.

## Acceptance Criteria

1. The same workbook uploaded twice produces one canonical report and an idempotent duplicate result.
2. A changed workbook for the same fund and period creates a new immutable source revision and does not silently overwrite prior evidence.
3. Fund name and report period are extracted from the cover/header evidence and preserved in raw and normalized forms.
4. Valid Jalali dates are stored as original text and converted to Gregorian dates through the existing date policy; invalid dates produce extraction issues.
5. `#NAME?`, `#REF!`, `#N/A`, and similar Excel errors are never parsed as numeric zero.
6. The parser does not require Microsoft Excel, LibreOffice, or formula recalculation at runtime.
7. Every normalized value produced by downstream features can reference a source workbook, sheet, row, column/header path, parser version, and extraction issue state.
8. Unknown sheets are retained in the workbook inventory and marked `Unclassified`; they are not silently ignored.
9. The report can reach `PartiallyParsed` when some sections are usable and others fail.
10. Existing company, statement, monthly-sales, scanner, AI, billing, and alert behavior remains unchanged.

## Internal Contract Proposal

```csharp
public sealed record FundPortfolioWorkbookEnvelope(
    Guid ReportId,
    Guid FundId,
    string ProviderName,
    string OriginalFileName,
    string FileSha256,
    string ParserProfileVersion,
    FundPortfolioReportPeriod Period,
    IReadOnlyList<FundWorkbookSheetEnvelope> Sheets,
    IReadOnlyList<FundExtractionIssue> Issues);

public interface IFundPortfolioWorkbookParser
{
    Task<FundPortfolioWorkbookEnvelope> ParseAsync(
        FundPortfolioWorkbookParseRequest request,
        CancellationToken cancellationToken);
}
```

## Data Model Proposal

```text
InvestmentFunds
- Id
- ExternalFundId?
- FundName
- NormalizedFundName
- FundSymbol?
- RegistrationNumber?
- ManagerName?
- ProviderName
- IsActive
- CreatedAtUtc
- UpdatedAtUtc

FundPortfolioReports
- Id
- FundId
- ProviderName
- ExternalReportId?
- ReportType
- PeriodStartJalali?
- PeriodEndJalali
- PeriodStartDate?
- PeriodEndDate
- FiscalYearStartJalali?
- FiscalYearEndJalali?
- OriginalFileName
- FileSha256
- RawStorageKey
- ParserProfileVersion
- ParseStatus
- SourceRevision
- ImportedAtUtc
- SupersedesReportId?

FundPortfolioReportSheets
- Id
- ReportId
- OriginalSheetName
- NormalizedSheetName
- LogicalSheetType
- SheetIndex
- UsedRange
- ClassificationConfidence
- HeaderFingerprint

FundPortfolioExtractionIssues
- Id
- ReportId
- SheetId?
- Severity
- IssueCode
- SourceAddress?
- RawValue?
- Message
- ParserProfileVersion
- CreatedAtUtc
```

## Security and Compliance Rules

- Raw files and parsed evidence are DataAdmin-controlled resources.
- File names, storage keys, and source rows must not be exposed to unauthorized actors.
- Validate extension, MIME type, workbook size, decompression limits, row/column limits, and formula/link payloads.
- Do not execute macros, external links, embedded objects, or workbook formulas.
- Treat outputs as factual fund disclosures, not recommendations.
