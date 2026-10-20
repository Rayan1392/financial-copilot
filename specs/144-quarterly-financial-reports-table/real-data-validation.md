# Feature 144 Real-Data Validation

## Execution

- Company: `چخزر` (external company ID `12783`); provider `NoavaranCurrentApi`; statement `IncomeStatement`; five quarters; separate/non-consolidated scope (`IsComposing=false`).
- The actual `IQuarterlyFinancialReportsQueryUseCase` and backend table rendering path were run against the local Development PostgreSQL database. The connection forced `default_transaction_read_only=on`; the queries were read-only. No production data or source-provider calls were used.
- Result JSON, persisted report/source evidence, and full rendered 16 × 5 table: [typed result](real-data-validation-result.json), [database evidence](real-data-database-evidence.json), [rendered answer](real-data-rendered-answer.md).

## Before and after availability

| Result | Before policy update | After policy update |
|---|---:|---:|
| Available | 18 | 80 |
| Newly Available | — | 62 |
| IncompatibleObservation | 14 | 0 |
| MissingPredecessor | 14 | 0 |
| MissingObservation | 30 | 0 |
| Gross Margin unavailable | 4 | 0 |
| Total unavailable | 62 | 0 |

The prior 18 available cells consisted of all 16 rows for 1405 Q1 (16 nonzero and 2 zero values), plus SourceItemId 153 share count for 1405 Q2 and 1404 Q3. All 62 prior unavailable cells are now available under the confirmed Noavaran unit contract and report-selection policy. The 44 missing source-row states (30 direct missing observations and 14 missing predecessor rows) were checked against persisted evidence; no values were manufactured. The 14 previous incompatibilities resulted from missing currency/accounting-basis/revision-lineage equality evidence, which is not required by the approved provider contract. Gross Margin now has both normalized operands for all five periods.

There are no remaining incompatible cells, missing-predecessor cells, missing observations, or unavailable Gross Margin cells. Exact remaining unavailable cell list: **none**. Exact remaining unavailable reasons: **none**.

## Selected persisted report identities

| Fiscal period | Duration | External statement ID | Persisted report ID | Audited | Restated (`IsRepresented`) | Scope (`IsComposing`) |
|---|---:|---:|---|---|---|---|
| 1405 Q2 | YTD6M | 553383 | `12b8207f-de6b-46eb-b1c1-739d5e15d8f7` | No | No | Separate |
| 1405 Q1 | YTD3M | 546627 | `0ac092de-89c9-470d-b7d8-294dd5770c41` | No | No | Separate |
| 1404 Q4 | YTD12M | 538950 | `03bce28c-20fd-4004-afbe-74750d11e3a6` | Yes | No | Separate |
| 1404 Q3 | YTD9M | 532579 | `782c277c-d2eb-4455-b8f9-dbd663405d3f` | No | No | Separate |
| 1404 Q2 | YTD6M | 530286 | `37a7231f-e36d-4f86-ad77-49cecd83b1ef` | Yes | No | Separate |
| 1404 Q1 predecessor | YTD3M | 524153 | `a3b75e41-85ee-4a20-bed7-4f0e84c77fb2` | No | No | Separate |

Selected report IDs, checksums, source-line IDs, and all persisted metadata are included in the JSON evidence. Audit/restatement class can differ across periods; each report remains individually traceable.

## Original arithmetic and units (EPS policy superseded)

- 1405 Q2 revenue: `16,754,501 − 7,365,921 = 9,388,580` million Iranian rials.
- 1405 Q2 EPS: `24,967 − 10,155 = 14,812` Iranian rials per share.
- Share count SourceItemId 153 remains the reported `200,000` thousand shares and is not differenced.
- Monetary values use million IRR, SourceItemId 160 uses IRR/share, SourceItemId 153 uses thousand shares, and Gross Margin uses percent. The Noavaran provider contract supplies these units even where Currency metadata is absent. No currency conversion was performed.
- Numeric zero remains `0` / `Available` in typed output and renders as `-`. Missing observations remain distinct from numeric zero.

## Test limitations

- The actual Development smoke test passed 1/1 after the update.
- The isolated PostgreSQL repository test was skipped in this rerun because `FINANCIAL_COPILOT_TEST_POSTGRES_CONNECTION_STRING` was not configured. Existing prior isolated-PostgreSQL evidence is retained in the implementation report; no isolated DB was created in this run.
- Full browser pixel layout was not captured; backend Markdown output was generated and contains the complete table.


## EPS policy correction

The EPS and share-basis rows above document the original policy and are superseded by the Feature 144 correction. Refreshed Development smoke artifacts label row 160 as comparable EPS and row 153 as the comparison share basis. For Chakhzar, required attributable-profit/share evidence is absent, so these cells correctly return typed unavailable rather than carrying forward cumulative-EPS subtraction or assuming an unsupported share count. Monetary profit rows remain source-derived and unchanged. Current Qasem arithmetic and verification results are recorded in [the EPS/share-basis investigation](eps-share-basis-investigation.md).
