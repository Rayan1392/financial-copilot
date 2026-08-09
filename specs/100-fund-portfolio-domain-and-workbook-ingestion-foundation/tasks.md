# Tasks — Fund Portfolio Domain and Workbook Ingestion Foundation

## 1. Domain Ownership and Boundaries

- [x] Create a dedicated `FundPortfolio` bounded capability under the existing Financial domain without changing current company-fundamentals entities.
- [x] Add canonical entities/enums for `InvestmentFund`, `FundPortfolioReport`, `FundPortfolioReportSheet`, `FundPortfolioExtractionIssue`, `FundPortfolioParseStatus`, `FundWorkbookLogicalSheetType`, and `FundExtractionIssueSeverity`.
- [x] Define explicit ownership: Feature 100 owns fund/report identity, raw workbook evidence, parser profile, sheet inventory, and extraction issues; Features 102–104 own normalized business rows.
- [x] Prevent direct references from Domain to Excel libraries or Infrastructure implementations.
- [x] Document that a followed symbol or user portfolio is unrelated to an investment fund's disclosed holdings.

## 2. Persistence and Migrations

- [x] Add EF Core configurations and a migration for `InvestmentFunds`, `FundPortfolioReports`, `FundPortfolioReportSheets`, and `FundPortfolioExtractionIssues`.
- [x] Add a unique canonical fund key appropriate to available source identifiers; when no external id exists, use governed normalized-name resolution with review status rather than uncontrolled text matching.
- [x] Add report uniqueness for `(FundId, ProviderName, PeriodEndDate, ReportType, SourceRevision)` and a separate unique file hash/import identity.
- [x] Add indexes for fund, period end, provider, parse status, file hash, logical sheet type, and unresolved/error issues.
- [x] Store Jalali source values and Gregorian parsed values separately.
- [x] Store raw files outside relational columns through the existing storage abstraction; persist only immutable storage reference, checksum, size, MIME type, and original file name.

## 3. Safe Workbook Reader

- [x] Implement a workbook reader that opens `.xlsx` packages without executing formulas, macros, external links, or embedded content.
- [x] Enforce configurable limits for file size, compressed/uncompressed ratio, sheet count, row count, column count, cell text length, and total parsed cells.
- [x] Read displayed/cached values and formula text separately where available; never use formula text as the authoritative numeric value.
- [x] Normalize Persian/Arabic digits, `ي/ی`, `ك/ک`, ZWNJ, control characters, bidi marks, whitespace, percentage signs, negative parentheses, and Rial labels.
- [x] Detect Excel error tokens (`#NAME?`, `#REF!`, `#N/A`, `#VALUE!`, `#DIV/0!`, and variants) and emit structured extraction issues.
- [x] Preserve raw cell text before normalization.

## 4. Parser Profile and Sheet Classification

- [x] Create a versioned parser profile, initially `iran-fund-portfolio-workbook-v1`.
- [x] Implement sheet-name alias normalization plus structural classification from title/header fingerprints.
- [ ] Classify all 20 sample sheets into the logical types listed in the user story.
- [x] Mark sheet `0` as ignored control/formula content while retaining its inventory and issues.
- [x] Retain unknown sheets as `Unclassified` with classification evidence and confidence.
- [x] Detect duplicated logical sheet types and require deterministic priority or manual review; never silently choose an arbitrary sheet.
- [x] Store parser profile version and classifier version on every sheet/report result.

## 5. Cover and Period Extraction

- [x] Extract fund display name, report title, and period-end text from the `تیتر` sheet and repeated sheet headers where available.
- [x] Reconcile repeated header evidence across sheets and flag conflicts.
- [x] Parse Jalali dates using the governed date converter and preserve original strings.
- [x] Support month-end reports whose period end is not the last calendar day; do not infer a different date.
- [x] Define `CurrentPeriod`, `FiscalYearToDate`, `PriorComparablePeriod`, and `UnknownPeriodContext` tokens for downstream dual-block parsing.
- [x] Create a deterministic report identity and source-revision policy.

## 6. Parser Output Contracts

- [x] Add `IFundPortfolioWorkbookParser`, parse request/result contracts, sheet envelope, raw row/header-path representation, and evidence pointer.
- [x] Include sheet index, used range, source address, raw value, normalized value, period context, and parser version in evidence contracts.
- [x] Define partial-success semantics: `Queued`, `Parsing`, `PartiallyParsed`, `Parsed`, `NeedsReview`, `Failed`, `Superseded`.
- [x] Ensure downstream normalizers can consume the parser output without reopening the workbook.
- [x] Add deterministic JSON serialization for diagnostics and replay fixtures.

## 7. Application Use Cases

- [x] Implement `CreateOrResolveInvestmentFundUseCase` with governed normalization and ambiguity handling.
- [x] Implement `IngestFundPortfolioWorkbookUseCase` that validates input, stores the raw file, creates report revision, parses workbook, persists inventory/issues, and dispatches downstream section normalizers when registered.
- [x] Implement `GetFundPortfolioReportStatusUseCase` and `GetFundPortfolioReportIssuesUseCase` for Feature 101 operations.
- [x] Make ingestion idempotent for duplicate file hash and safe under concurrent submissions.
- [x] Preserve prior source revisions immutably when a corrected workbook is supplied.

## 8. Observability and Audit

- [x] Emit correlation id, fund resolution outcome, file hash prefix, parser version, sheet classification counts, issue counts, duration, and final status.
- [x] Do not log full portfolio rows, raw bank account-like data, or unrestricted workbook contents.
- [x] Add audit entries for ingest, duplicate, corrected revision, failure, and supersession.
- [x] Expose metrics for unclassified sheets, formula errors, date failures, and partial parses.

## 9. Tests and Acceptance Scenarios

- [ ] Add a sanitized fixture derived from the supplied 20-sheet workbook.
- [ ] Unit-test Persian normalization, Jalali parsing, Excel-error detection, sheet aliasing, structural classification, and duplicate logical sheets.
- [ ] Integration-test first import, duplicate import, concurrent duplicate import, corrected revision, raw storage failure, partial parse, and unknown-sheet retention.
- [ ] Security-test zip-bomb limits, oversized workbook, external links, macros/embedded content, malformed XML, and formula payloads.
- [ ] Given the sample workbook, when parsing completes, then every sheet is inventoried, all known sheets are classified, sheet `0` is ignored but retained, and formula errors are issues rather than zero values.
- [ ] Given a corrected workbook for the same fund/period, when imported, then a new immutable source revision is created and the earlier revision remains reproducible.

## Completion Gate

- [ ] Keep tasks unchecked until the sample workbook passes classification, duplicate/revision behavior is deterministic, raw evidence is reproducible, unsafe workbook content is blocked, and existing FinancialCopilot regression suites remain green.
