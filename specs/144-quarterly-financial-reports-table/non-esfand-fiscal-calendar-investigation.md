# Feature 144: Non-Esfand Fiscal Calendar Investigation

## Finding

The quarterly normalizer ignored the persisted `JalaliFiscalYearEnd` evidence carried in `QuarterlyReportObservation.FiscalYearEvidence`. It grouped reports by `PeriodStart`, derived the displayed fiscal-year label from the Jalali year of that start, and validated YTD boundaries from the same field. For a fiscal year ending in month 9, its start is in the previous Jalali year: the reports for FY1405 begin at 1404/10/01. The old code therefore labeled that fiscal year as 1404. It also made `PeriodStart`, rather than the report's fiscal-calendar evidence, authoritative for cumulative matching and period validation.

The fix resolves the fiscal-year start from the persisted fiscal-year-end date, groups cumulative reports on that resolved start, labels each period with the year of the fiscal-year-end evidence, and validates the YTD end against the resolved start. Esfand year-end reports continue to map to the same Jalali fiscal-year label. If evidence is absent or malformed, the old `PeriodStart` fallback remains in effect.

The statement repository was not the source of the local coverage gap: its query reads all matching 3/6/9/12-month income statements for the resolved company/provider/scope, bounded to 48 rows. The normalizer then chooses report variants within each fiscal-year/duration group. Production's reported five-column/two-populated result was not reproducible against the available Development snapshot; details and the remaining production-data limitation are below.

## Request and Development Database

Reproduced the exact request `گزارشات فصلی دتولید` by parsing it through `FinancialStatementTableIntentRules.BuildQuarterlyQuery` and executing `IFinancialStatementTableQueryUseCase`. The development PostgreSQL connection was forced read-only with `default_transaction_read_only=on`.

- Company resolution: `دتولید`
- Provider: `NoavaranCurrentApi`
- Statement type: `IncomeStatement`
- Scope: separate (`IsComposing=false`)
- Result after fix: `Available`, five consecutive fiscal quarters
- Evidence artifact: `detolid-development-evidence.json` (request, parsed query, persisted inventory, selected reports, normalized table)

The normalized Development inventory contained these FY1404/FY1405 income-statement reports:

| Fiscal year end | Duration | Period end | Persisted report ID | Audited | Represented | Scope |
|---|---:|---|---|---:|---:|---|
| 1404/09/30 | 3M | 1403/12/29 | `bae63137-7c6f-4e36-9f48-b4410a5323ca` | no | no | separate |
| 1404/09/30 | 6M | 1404/03/31 | `b6e24ad3-4769-46a3-a4e6-e466fc64d5e7` | yes | no | separate |
| 1404/09/30 | 6M | 1404/03/31 | `39bc556a-59bd-4c5a-8cd0-ffc33e1b8f3c` | no | no | separate |
| 1404/09/30 | 9M | 1404/06/31 | `9625ea68-0517-4ae9-839c-1a78eae58b78` | no | no | separate |
| 1404/09/30 | 12M | 1404/09/30 | `cc4524cc-1f9d-4a75-a23d-a1bc3659043c` | yes | no | separate |
| 1404/09/30 | 12M | 1404/09/30 | `46bcc12e-e57a-4826-bb72-7a739adc8ab8` | no | no | separate |
| 1405/09/30 | 3M | 1404/12/29 | `8cfbdf76-dd09-4551-a1aa-f04656b28247` | no | no | separate |

The local inventory has no 6M FY1405 report ending 1405/03/31. That statement is present in the production evidence supplied with the request, but its report ID and source observations are not available in this Development snapshot. No ID or values have been inferred for it.

## Five-Quarter Coverage

The before column below records the reported production symptom: the table had five columns, with only Q1/Q2 populated and three older quarters unavailable. The after columns distinguish the available local Development result from the requested production evidence.

| Fiscal year / quarter | Expected cumulative report | Selected Development report | Predecessor report | Report period end | Development source status | Production evidence status |
|---|---|---|---|---|---|---|
| FY1405 Q2 | 6M FY1405 | none in local snapshot | `8cfbdf76-dd09-4551-a1aa-f04656b28247` (3M) | expected 1405/03/31 | unavailable locally: no persisted matching 6M statement | inventory supplied by user confirms report exists; production ID not supplied |
| FY1405 Q1 | 3M FY1405 | `8cfbdf76-dd09-4551-a1aa-f04656b28247` | none | 1404/12/29 | available | report present in supplied inventory |
| FY1404 Q4 | 12M FY1404 | `cc4524cc-1f9d-4a75-a23d-a1bc3659043c` | `9625ea68-0517-4ae9-839c-1a78eae58b78` (9M) | 1404/09/30 | revenue available; 12M minus 9M | annual report present in supplied inventory |
| FY1404 Q3 | 9M FY1404 | `9625ea68-0517-4ae9-839c-1a78eae58b78` | `b6e24ad3-4769-46a3-a4e6-e466fc64d5e7` (audited 6M) | 1404/06/31 | available; 9M minus 6M | corresponding production ID not present in the supplied evidence |
| FY1404 Q2 | 6M FY1404 | `b6e24ad3-4769-46a3-a4e6-e466fc64d5e7` | `bae63137-7c6f-4e36-9f48-b4410a5323ca` (3M) | 1404/03/31 | available; audited 6M minus 3M | corresponding production ID not present in the supplied evidence |

The exact local Development query's five columns were FY1405 Q1, FY1404 Q4, Q3, Q2, and Q1, newest first. Revenue (`SourceItemId=15`) was available in every returned quarter. Its normalized values were 1,777,040; 743,545; 2,061,492; 1,700,516; and 1,003,658 million Rials, respectively. In FY1404 Q4, `SourceItemId=278` is `MissingObservation` because a required source line item is absent from the selected cumulative operands; the other 15 table rows are available. The oldest Q1 appears because the local Development snapshot lacks the FY1405 6M statement; this is not represented as production coverage.

The regression fixture includes FY1405 Q2 and produces the expected five periods newest first: FY1405 Q2, FY1405 Q1, FY1404 Q4, FY1404 Q3, FY1404 Q2. It selects the approved audited FY1405 6M variant and computes sample revenue quarters as 120, 80, 200, 150, and 150; EPS follows the same subtraction rule, shares remain as reported, and gross margin is computed from normalized gross profit/revenue. Thus the fixture demonstrates 5/5 populated coverage after the fix. Before the fix, the same non-Esfand periods were assigned to the Jalali year of `PeriodStart`, shifting the FY labels; the reported production baseline had only 2/5 populated columns.

## Selection, Scope, and Data-Quality Checks

- **Company/provider resolution:** the exact local request resolved `دتولید` to the `NoavaranCurrentApi` company. The query and returned reports use the same resolved company ID and provider. No provider mismatch was observed.
- **Statement filtering:** repository reads are restricted to `IncomeStatement` and period types 3/6/9/12 months. The local FY1404 and FY1405 reports were returned by that query.
- **Scope:** all selected local reports are separate statements (`IsComposing=false`). The PostgreSQL regression also seeds a different scope only in the existing isolation test; it never combines separate and consolidated values.
- **Audit/restatement precedence:** reports are grouped by resolved fiscal-year start and duration, then ordered by audited, represented, announcement date, and external report ID. Conflicting top-priority ties fail closed; identical-value ties use report ID deterministically. Local FY1404 6M and 12M each have audited and unaudited variants; the audited report IDs above were selected. The regression asserts that the audited FY1405 6M version wins over its unaudited peer in the same scope.
- **Unit/catalog compatibility:** the repository selects only approved source-item catalog IDs. The new fixture supplies approved revenue, gross profit, EPS, and share-count catalog IDs/units with matching cumulative observations. Existing checks for scale, source amount unit, missing line items, typed zero, and source IDs remain unchanged.
- **Source status vs. absent report:** the Development FY1405 Q2 is an absent report in this database snapshot. Its production counterpart is confirmed only by the supplied inventory evidence; no production report ID or cell value was available to verify here. The other four Development quarters have selected reports and available revenue observations.

## Files Changed

- `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/QuarterlyFinancialReportsNormalizer.cs` — resolve fiscal start/year from the persisted Jalali fiscal-year-end evidence and use that identity for grouping and boundary validation.
- `tests/FinancialCopilot.UnitTests/QuarterlyFinancialReports144Tests.cs` — assert FY1405 labeling for a fiscal year starting in 1404, and align fiscal-year-end fixture evidence with the report period.
- `tests/FinancialCopilot.IntegrationTests/QuarterlyFinancialReports144PostgreSqlTests.cs` — add a PostgreSQL regression for five consecutive non-Esfand fiscal quarters, audit selection, and cumulative arithmetic; add a read-only Development smoke test for the exact `دتولید` request and inventory.
- `specs/144-quarterly-financial-reports-table/detolid-development-evidence.json` — output from the read-only Development smoke run.

## Verification

- `dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj --no-restore --filter FullyQualifiedName~QuarterlyFinancialReports144Tests` — passed, 34 tests.
- `dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj --no-restore -p:BuildProjectReferences=false --filter FullyQualifiedName~NonEsfandFiscalCalendar_MapsAndCalculatesFiveConsecutiveQuarters` — passed against an isolated PostgreSQL test database.
- `DevelopmentDatabase_ReadOnly_DetolidInventoryAndExactQuarterlyRequest` — passed against the local `financial_copilot` Development database with PostgreSQL read-only mode enforced; result and inventory are recorded in the evidence artifact.

The integration regression executed against a uniquely named isolated PostgreSQL test database. The read-only live use case separately verifies actual company resolution, report inventory, selection, fiscal labels, five-quarter ordering, and available source values without changing the Development database.

FEATURE_144_NON_ESFAND_FISCAL_CALENDAR_FIXED
