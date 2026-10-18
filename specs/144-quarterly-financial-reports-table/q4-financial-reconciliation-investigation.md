# Feature 144 — FY1404 Q4 financial reconciliation

**Investigation date:** 2026-10-17 (Asia/Tehran)  
**Evidence boundary:** local `financial_copilot` Development PostgreSQL, queried with `default_transaction_read_only=on`; repository code and isolated tests. No Production database, actual screenshot image, Excel workbook, or original screenshot request payload was available. The Product Owner's numbers and screenshot title came from the supplied request text.

## 1. Company identity and request reproduction

The intended Excel ticker is **غشهداب**, `Companies.Id=d0793b30-6601-4b83-9d85-cdfca77f479c`, `ExternalCompanyId=13214`, name **کشت و صنعت شهداب ناب خراسان**. The title quoted for the screenshot names **بورس**, `Companies.Id=c5821ee3-1298-45a1-b967-582f228d7b74`, `ExternalCompanyId=723`, name **بورس اوراق بهادار تهران**. Both are separate `NoavaranCurrentApi` company records. A table titled `جدول گزارش فصلی بورس اوراق بهادار تهران (بورس)` cannot validate the غشهداب Excel figures.

Read-only execution of `گزارشات فصلی غشهداب` through `FinancialStatementTableIntentRules.BuildQuarterlyQuery` and `IFinancialStatementTableQueryUseCase.ExecuteAsync` parsed `CompanyQuery=غشهداب`, resolved `ExternalCompanyId=13214`, returned `CompanySymbol=غشهداب`, `CompanyName=کشت و صنعت شهداب ناب خراسان`, `Status=Available`, and rendered `### جدول گزارش فصلی کشت و صنعت شهداب ناب خراسان (غشهداب)`. The same read-only route for `گزارشات فصلی بورس` resolved `ExternalCompanyId=723` and rendered the title quoted for the screenshot. This reproduces each identity separately; **the request and response that produced the actual screenshot are NOT VERIFIED**. No wrong company resolver result, stale conversation payload, or cache issue was reproduced.

The selected Noavaran `FinancialStatements.CompanyId` fields are null in this Development snapshot. The application and report repository link these records by `ProviderName` and `ExternalCompanyId=13214`; the `Companies.Id` above is a catalog identity, not a populated FK on those statement rows.

## 2. FY1404 candidate inventory

All matching `NoavaranCurrentApi` `IncomeStatement` records with `PeriodStart=2025-03-21` were inspected before identifying winners. `IsComposing=false` is separate; `true` is consolidated. `IsRepresented` is the restatement flag. All rows carry `jalaliFiscalYearEnd=1404/12/29`, `assumedScale=MillionRials`, and `sourceAmountUnit=N/A` in `WarningsJson`; explicit currency and revision-lineage metadata are absent. Raw source item amounts below are stored decimals. A dash means the item is absent from that persisted candidate.

| Period | Scope | Audit / restated | FinancialStatements.Id | ExternalStatementId | Line count | 143 net profit | 160 EPS | 153 shares |
|---|---|---|---|---|---:|---:|---:|---:|
| 3M, 1404/03/31 | separate | no / no | `a5277d0e-ca04-46b4-84ca-56761ecd8e35` | `525413` | 53 | 435,918 | 49 | 8,846,490 |
| 6M, 1404/06/31 | separate | yes / no | `0f412bdc-a6bf-4912-81f0-1c9d8d87ea77` | `531779` | 46 | 1,534,564 | 173 | 8,846,490 |
| 6M, 1404/06/31 | legacy scope mismatch | no / no | `3f05db43-1285-4340-b9b2-ec863d794804` | `531780` | 8 | — | — | — |
| 6M, 1404/06/31 | consolidated | yes / no | `3f987fa6-9f0a-4002-b057-8b3c527091b2` | `531780` | 49 | 2,200,140 | 248 | 8,846,490 |
| 9M, 1404/09/30 | separate | no / no | **`87cbf026-05f6-4ac7-9fcf-52f5b0618b4a`** | **`532949`** | 54 | **2,184,085** | **246** | **8,846,490** |
| 12M, 1404/12/29 | separate | no / no | **`efec2616-f7ba-46b5-a284-cf0fc6c1b2ef`** | **`540783`** | 46 | **3,289,888** | **294** | **11,180,000** |
| 12M, 1404/12/29 | legacy scope mismatch | no / no | `edb5231a-01c0-4dbd-aa27-b4238854378f` | `540784` | 8 | — | — | — |
| 12M, 1404/12/29 | consolidated | no / no | `70e4ca4c-1cac-4c0f-a217-e498af6f5c45` | `540784` | 49 | 4,594,588 | 410 | 11,180,000 |

The two sparse `IsComposing=false` rows have source metadata identifying `isComposing=true`; the repository excludes them before its 48-report limit. The separate 6M audited row wins over any same-scope lower-priority candidate. The 9M and 12M winners above are the only eligible separate versions persisted for those periods. Their `WarningsJson` announcement dates are 2026-01-20T09:38:14+03:30 and 2026-06-18T14:30:04+03:30, respectively; both have `IsAudited=false`, `IsRepresented=false`. The 9M/12M rows were last synchronized at 2026-07-04T16:58:17.845329+03:30. `PublishedAt` is null on both, so the announcement evidence comes from `WarningsJson`.

The source catalog maps IDs 143, 160, and 153 to the approved provider-scoped catalog IDs `bd36909d-abf8-44c2-b7db-5e53dd678b53`, `2146da26-b28e-4d04-b50c-f49fc9b70e61`, and `e98e6c00-7204-44b1-a279-bbbb68781d3c`. The selected 12M and 9M cells retain source line IDs in the typed response:

| SourceItemId | 12M line ID | 9M line ID |
|---:|---|---|
| 143 | `059ebf93-db4b-4e1f-8626-39a608e54568` | `437e85d9-2503-41fc-b4cc-ad9d0eff0551` |
| 160 | `649c029a-0ec7-4e2b-b0f7-da4489a42b73` | `8e2904ca-53e5-4edd-a87a-2c07da27a5a5` |
| 153 | `e555cd18-b01b-4a4d-a639-2230f9541d25` | `af4dcad3-6c46-4815-8a1c-1fc4d022f304` |

## 3. Raw Noavaran provenance and the Excel source

The stored raw payload `ProviderRawPayloads.Id=b32dfb55-0e01-4bdf-b911-dc036df880bd`, received 2026-07-02T17:00:17.818989+03:30, contains `com_ID=13214`, `bourseSymbol=غشهداب`, `statementID=532949/540783/540784`, and source item amounts identical to the persisted 9M, separate 12M, and consolidated 12M rows. Its checksum `7D50B8F27EBEE1A128E6589E6BFF548134F270C7EB68C9E5A3FFB2B897052D5F` matches the selected 9M/12M records.

A **later raw payload**, `ProviderRawPayloads.Id=10723818-d02b-44e7-8f41-cc462b79c70e`, received **2026-10-03T14:01:11.453622+03:30**, contains:

| Source report | Period | Scope | Audited / restated | Raw 143 | Raw 160 | Raw 153 |
|---|---|---|---|---:|---:|---:|
| `532949` | 9M 1404/09/30 | separate | no / no | 2,184,085 | 246 | 8,846,490 |
| `549831` | 12M 1404/12/29 | separate | yes / no | 3,261,314 | 291 | 11,180,000 |
| **`549833`** | **12M 1404/12/29** | **consolidated** | **yes / no** | **4,566,014** | **408** | **11,180,000** |

All three raw reports identify `com_ID=13214` and `bourseSymbol=غشهداب`; the two annual reports have `jalaliFiscalYearEnd=1404/12/29` and `jalaliAnouncementDate=1405/06/02`. Thus the Product Owner's annual 4,566,014 / 408 figures match **audited consolidated** report `549833`, while the Excel 9M 2,184,085 / 246 figures match **separate** report `532949`. The actual workbook/report-reference cells were not supplied, so this is a direct match to stored raw observations, not verification of the workbook file itself.

No `FinancialStatements` row exists for either `549831` or `549833` in the Development database. The `ProviderSyncRuns` entry `d95cc80b-e31d-48e2-bb18-5732699c2933` for company `13214` failed immediately after capturing the 2026-10-03 raw payload: `ProcessedRecords=0`, `ErrorCount=1`, `ErrorMessage=Sequence contains more than one element.` A later run `336ced19-6ec5-4b28-995c-7593ed4b3f76` failed with the same error on 2026-10-14. This proves the newer annual source reports were captured but not normalized in Development. It does not prove Production has the same state.

The Codal figures in the Product Owner's comparison are a separate source observation. In particular, Codal 3M net profit 3,516,669 and the resulting -1,982,105 Q2 are not part of the selected Noavaran statement lineage; no source priority or value was changed to match them.

## 4. FY1404 Q4 evidence matrix

The intended company's persisted fiscal year ends **1404/12/29**. The selected 9M end **1404/09/30** and selected 12M end **1404/12/29** have the same company, `NoavaranCurrentApi`, `IncomeStatement`, separate scope, fiscal year, provider scale evidence, and adjacent 9M/12M periods. Different audit flags would be allowed by Feature 144; both selected Q4 operands happen to be unaudited and not restated. The source catalog's `Unit=N/A` is the provider's raw item-unit field; Feature 144 applies the approved item-specific logical units and the persisted `MillionRials` amount-scale contract. No unit conversion is involved.

In this table, **difference = Excel expected − Development backend calculated**. “Rendered” is the actual backend Markdown from the read-only use-case reproduction. The supplied screenshot's browser/PNG values are NOT VERIFIED.

| Item | Excel expected Q4 | Raw selected 12M | Raw selected 9M | Selected 12M ID / source ref | Selected 9M ID / source ref | Backend calculated Q4 | Typed response Q4 | Rendered Q4 | Difference | Earliest divergence / cause |
|---|---:|---:|---:|---|---|---:|---:|---:|---:|---|
| Net Profit (143), million IRR | 2,381,929 | 3,289,888 | 2,184,085 | `efec2616…` / `540783` | `87cbf026…` / `532949` | 1,105,803 | 1,105,803 | 1٬105٬803 | +1,276,126 | Excel uses consolidated audited 12M `549833`; Development selected separate 12M `540783`. |
| EPS (160), IRR/share | 162 | 294 | 246 | `efec2616…` / `540783` | `87cbf026…` / `532949` | 48 | 48 | 48 | +114 | Same mixed-scope/audit-version source choice; approved EPS subtraction is correct. |
| Share Count (153), thousand shares | 11,180,000 | 11,180,000 | 8,846,490 | `efec2616…` / `540783` | `87cbf026…` / `532949` | 11,180,000 | 11,180,000 | 11٬180٬000 | 0 | Snapshot row is retained unchanged; no discrepancy. |

Manual checks:

- Excel net profit: **4,566,014 − 2,184,085 = 2,381,929**. This subtracts consolidated report `549833` from separate report `532949`, which Feature 144 prohibits.
- Development net profit: **3,289,888 − 2,184,085 = 1,105,803**.
- Excel EPS: **408 − 246 = 162** (same incompatible report pair). Development EPS: **294 − 246 = 48**. No net-profit/share-count EPS recomputation was used.
- Share count: **11,180,000**, copied from the selected 12M statement. `11,180,000 − 8,846,490 = 2,333,510` is not a valid share-count calculation.

The new audited **separate** raw annual report `549831`, if successfully normalized and selected under the approved priority, would instead provide raw net profit 3,261,314 and EPS 291. Using the currently available separate 9M operand would yield **1,077,229** and **45**, respectively. These are arithmetic scenarios from the raw snapshot, **not current application output or a proposed override**.

The source-basis issue can affect more than the three named rows. Across all 15 approved direct source IDs, the later audited consolidated annual values differ from the currently selected separate annual values for **12** rows. The later audited separate annual values differ for **6** rows (IDs 40, 142, 334, 279, 143, 160). Excel expected Q4 values for those other rows were not supplied, so their Excel differences are NOT VERIFIED. Gross Margin is derived from 139 and 15 and may also differ under a consolidated basis.

### Exact execution path

- `POST /api/ai/v1/query` enters `src/backend/FinancialCopilot.API/Controllers/AiFacadeController.cs`; the V2 workflow's specialized table route is in `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs`.
- `src/backend/FinancialCopilot.Application/AI/Orchestration/FinancialStatementTableIntentRules.cs` builds the quarterly query and extracts the company hint. `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/QuarterlyFinancialReportsContracts.cs` resolves the canonical company and requests persisted reports. The company resolver implementation is `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/CompanyResolverService.cs`.
- `EfCoreFinancialStatementTableRepository.GetQuarterlyIncomeStatementReportsAsync` in `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/FinancialStatementTableServices.cs` filters provider, company, statement type, scope, and source catalog identity. `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/QuarterlyFinancialReportsNormalizer.cs` selects revisions and calculates Q4 cells. `FinancialStatementTableQueryUseCase.RenderQuarterly` in the same infrastructure services file formats the typed result as Markdown; the API controller maps the typed result into the response.
- `src/frontend/src/components/app/markdown-message.tsx` recognizes the table. `src/frontend/src/components/app/quarterly-financial-table.tsx` parses the Markdown cell strings and renders the web table; `src/frontend/src/components/app/quarterly-financial-table-image.ts` draws the same model strings into PNG. The older source ingestion path and its repaired line lookup are in `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/NadpcoApiFinancialStatementNormalizer.cs`.

## 5. Earliest layer and root-cause classification

1. **A — screenshot/Excel company mismatch:** confirmed for the quoted title and intended ticker. The actual screenshot pixels and originating request are unavailable. Reproduced بورس and غشهداب requests resolve and render their own companies correctly.
2. **B/K — statement scope/reporting basis mismatch:** confirmed in stored Noavaran raw data. The Excel annual figures exactly match audited **consolidated** `549833`; its 9M figures match **separate** `532949`. Cross-scope Q4 subtraction violates the approved Feature 144 contract. There is no matching consolidated 9M report in the inspected later raw payload.
3. **C/J — newer audited versions captured but not persisted:** confirmed in Development. Audited separate `549831` and consolidated `549833` appear in the October raw payload, while the normalized table still contains earlier unaudited `540783/540784`. This is an ingestion freshness failure, not a wrong winner among currently persisted eligible reports.
4. **L — ingestion lookup defect:** confirmed by code, database duplicates, failed sync records, and a red/green regression test. `NadpcoApiFinancialStatementNormalizer.UpsertLineItemsAsync` previously used `SingleOrDefaultAsync` on `FinancialStatementId + MetricCode`. For report `532949`, `NET_PROFIT` exists both as a legacy metric-only line `9ebaefef-0491-4bb0-b186-9d9e4b780b5e` (null catalog) and the approved SourceItemId 143 line `437e85d9-2503-41fc-b4cc-ad9d0eff0551`. The same pattern exists for EPS and other metrics. The lookup throws before a company sync can normalize later annual reports. The database's physical metric-code index is nonunique; the source-catalog index is unique.

**Not supported as causes of this Q4 value difference:** wrong fiscal-year mapping, wrong 9M/12M winner within the persisted separate scope, subtraction error, share-count subtraction, item-ID mix-up, unit conversion, typed serialization, or backend Markdown formatting. The typed and Markdown Q4 values agree with the selected raw operands. The frontend table parser copies the Markdown quarter cells into its model; PNG export draws those model strings. An actual browser rendering and PNG generated from the original screenshot response were not supplied or independently verified.

## 6. Corrective code change and verification

The minimal safe correction changes only `NadpcoApiFinancialStatementNormalizer.UpsertLineItemsAsync`: find an existing line by `FinancialStatementId + SourceItemCatalogId` first, including tracked unsaved rows. Only when no catalog-linked line exists may a metric-code lookup reuse a **legacy line with null `SourceItemCatalogId`**. It preserves the provider's raw amounts and Feature 144's report selection, EPS, share-count, and cumulative-to-quarter policies. It does not delete or overwrite legacy records in this investigation.

The new `Normalize_UsesSourceCatalogIdentityWhenLegacyMetricOnlyLineExists` test seeded a catalog-linked NET_PROFIT line plus a legacy metric-only NET_PROFIT line. **Before** the code change it failed with the same `Sequence contains more than one element` stack in `UpsertLineItemsAsync`; **after**, it passed and retained both line identities and values. Focused normalizer and Feature 144 unit tests: **57 passed, 0 failed**. Frontend quarterly table, Markdown, and PNG component tests: **27 passed, 0 failed**. Read-only execution of the intended company's use case after the change still returned current Development Q4 **1,105,803 / 48 / 11,180,000**, confirming that no source data was silently updated.

The filtered V2 endpoint suite returned **8 passed, 1 failed**. The unrelated unknown-ticker test `V2AiQuery_UnknownQuarterlyTickerReturnsTypedCompanyNotFoundOutcome` reproducibly received a null `financialStatementTableResult` where its fixture expects an object; it was not changed as part of this Q4 reconciliation. Four PostgreSQL integration tests were **skipped** because their isolated database/test environment was unavailable. The read-only Development use-case probe above supplied actual repository/use-case/Markdown evidence independently of those skipped tests.

## 7. Impact, uncertainties, and Product Owner decisions

The mixed legacy/catalog duplicate pattern is broader than غشهداب in the inspected Development snapshot: **34,580 report/metric pairs across 10,057 statements and 803 companies** have one legacy and one catalog-linked line with the same metric code. The same `Sequence contains more than one element.` sync failure appears in **17,203** Development `ProviderSyncRuns` rows across **795** company references. These counts indicate exposure to ingestion failure; they do **not** establish a financial-value discrepancy for every company or fiscal year. No duplicate-legacy-only or duplicate-catalog-only metric group was found in that snapshot.

**Remaining NOT VERIFIED:** Production's company records, raw payloads, normalized annual versions, actual Feature 144 Q4 output, browser screenshot/PNG, original request/conversation payload, and the Excel workbook's source report references. The available evidence is sufficient to reconcile the *supplied* numbers against Development/raw observations, but not to certify the reported Production screenshot.

**Product Owner decisions required:** confirm whether the desired FY1404 table is separate or consolidated and provide the actual screenshot request/response plus workbook source report IDs. For a separate table, the Excel annual `549833` must not be combined with separate 9M `532949`; the later audited separate `549831` is the corresponding annual source in the captured raw snapshot. For a consolidated Q4, a compatible consolidated 9M observation is required under the existing policy. Any change to scope mixing, source priority, financial methodology, or historical data requires explicit approval.

No Production records were accessed or changed. No Development financial records were changed. No re-ingestion, backfill, provider switch, Billing change, V1/V2 routing change, commit, push, or deployment was performed. The ingestion fix is code-only and requires a separately authorized rollout and a subsequent ordinary sync before newly captured annual reports can affect live answers.

**Status: `FEATURE_144_Q4_RECONCILIATION_BLOCKED`** — local root causes are evidenced; Production and the original screenshot/workbook provenance remain unverified.
