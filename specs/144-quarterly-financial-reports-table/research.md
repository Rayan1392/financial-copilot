# Feature 144 Research — Decisions and Repository Evidence

## Scope and method

Read the repository README and existing Feature 144 package, inspected Feature 141's source-item snapshot, reviewed Feature 142/143 design evidence, and used the indexed codebase graph for existing statement query, V2 routing, and response/replay boundaries. The graph project `D-Source-TahlilApp-AI` was ready (89,644 nodes). The compatibility-policy continuation updated the Feature 144 normalizer and tests, then queried only the authorized local Development database with a read-only transaction. No production database or live provider was used.

## Confirmed Product Owner decisions

These decisions are final for this design and supersede conflicting earlier Feature 144 assumptions:

- The table has exactly 16 rows: the 15 mapped rows below plus Gross Margin as the only derived financial metric.
- All 15 source rows use provider `NoavaranCurrentApi` (display: Noavaran Amin), `StatementType = IncomeStatement`, and the existing provider-scoped Source Item Catalog.
- The final mappings, display order, and row policies are approved. MetricCode is optional, not required. No alternate mapping research or new source catalog is allowed.
- Noavaran 3M/6M/9M/12M income-statement source reports contain cumulative fiscal-year-to-date amounts. Approved flow values display standalone quarters by adjacent cumulative differences.
- SourceItemId 160 (EPS) uses the same explicit cumulative subtraction: Q1 = YTD3M; later quarters = current YTD minus the immediately previous YTD. Fixture Q2 is 14,812. The one-unit screenshot discrepancy is out of scope and is not an issue or task.
- SourceItemId 153 share count is preserved as reported, with no subtraction or monetary conversion; unit is thousand shares.
- Gross Margin uses normalized gross profit / normalized revenue × 100 and is the only derived financial metric.
- An actual numeric zero is valid (`Available`, numeric 0) and displays as `-` in a financial table cell. Missing/unavailable remains null with separate state/reason. SourceItemIds 12 and 278 are approved mappings and their zero values are available, not absent.
- Arithmetic is deterministic and full precision; no LLM-generated financial calculations. No query-time provider call.

### Approved source map

Provider key and catalog identities below come from the Feature 141 source-item snapshot. `MetricCode` is shown only where that snapshot has one; blank values are permitted and do not affect Feature 144 mapping.

| Order | Display label | SourceItemId | Source catalog UUID | Snapshot MetricCode |
|---:|---|---:|---|---|
| 1 | فروش | 15 | `07c03a70-a1fa-4b9b-861c-937748e2a1dc` | `REVENUE` |
| 2 | بهای تمام‌شده کالای فروش‌رفته | 1 | `57940eb5-27f8-42b1-8827-d64dc49eab2d` | — |
| 3 | سود (زیان) ناخالص | 139 | `917b2bbb-3859-42f0-9864-2eac3f98a3b3` | `GROSS_PROFIT` |
| 4 | مارجین ناخالص | — | Derived row | — |
| 5 | هزینه‌های عمومی، اداری و تشکیلاتی | 5 | `c04b4fae-69db-48d9-a63e-c96a932e7b89` | — |
| 6 | خالص سایر درآمدها (هزینه‌ها)ی عملیاتی | 39 | `17f91e30-ba52-4789-89d0-3ffd3ac61c17` | — |
| 7 | سود (زیان) عملیاتی | 140 | `f2a6e460-176f-4426-a4e7-78d4ffd86ba6` | `OPERATING_PROFIT` |
| 8 | هزینه‌های مالی | 12 | `b8342891-ad45-4c4b-a4dd-361bb5f37adb` | `FINANCE_COSTS` |
| 9 | خالص سایر درآمدها و هزینه‌های غیرعملیاتی | 40 | `e08f0ce7-ab6a-4a24-8fab-efc3f93c140f` | — |
| 10 | سود (زیان) عملیات در حال تداوم قبل از مالیات | 142 | `1cbc0ee5-21bb-4b9d-b5ba-72c3b84731b3` | — |
| 11 | مالیات | 334 | `31f4454a-78d2-41fb-8caa-b86564423cd6` | — |
| 12 | سود (زیان) خالص عملیات در حال تداوم | 279 | `abdbd529-7e27-43cc-a955-10ae19aea4cb` | — |
| 13 | سود (زیان) عملیات متوقف‌شده پس از اثر مالیاتی | 278 | `cb6c92c3-cff6-40c1-a85a-db9fde4ba791` | — |
| 14 | سود (زیان) خالص | 143 | `bd36909d-abf8-44c2-b7db-5e53dd678b53` | `NET_PROFIT` |
| 15 | سود هر سهم پس از کسر مالیات | 160 | `2146da26-b28e-4d04-b50c-f49fc9b70e61` | `EPS` |
| 16 | تعداد سهام شرکت (هزار سهم) | 153 | `e98e6c00-7204-44b1-a279-bbbb68781d3c` | — |

SourceItemIds 12 and 278 use catalog TitleFa at implementation time because the Product Owner did not supply their exact original titles. No title is invented here.

## Repository findings (not Product Owner decisions)

- Existing provider identity in source catalog is `NoavaranCurrentApi`; provider display metadata maps it to Noavaran Amin. Source items are keyed by provider and statement type, and catalog UUIDs are present for all 15 approved IDs in the checked snapshot.
- Feature 141 has a governed semantic catalog, but generic query authorization/runtime status remains separate; Feature 144 should use a specialized V2 capability and should not activate generic SQL fallback.
- Financial statement reports and line items already persist provider, statement type, period, variants/scope, source catalog identities, row IDs, and values. Feature 083 returns a single report table; a bounded multi-report query extension is needed to assemble quarters.
- Feature 081 has period-aware analysis/comparison behavior. Feature 026's `CodalDiscreteQuarterDeriver` supplies an arithmetic precedent but is not sufficient by itself for Feature 144 report compatibility, missing periods, revision lineage, or row-level provenance.
- Current V2 tools, typed assistant payload, conversation replay, authorization, and outer Billing boundaries should be extended/reused. Feature 142 supplies ingestion; Feature 143 triggers that ingestion. Neither is invoked by a table query.
- Feature 143 evidence says the normalized company catalog does not provide a universally reliable fiscal-calendar field; exact fiscal-period evidence must be used rather than assuming an Esfand year end.

## Actual persisted observation verification

Performed against the local Development database using the actual Application use case. The 16 × 5 result contains 80 available cells, including Q2 revenue `9,388,580` and EPS `14,812`; selected report/source identities are recorded in `real-data-database-evidence.json`. No production data or live Noavaran request was used. See `real-data-validation.md` for the before/after comparison and complete rendered table.

## Implementation evidence to confirm (technical, not open product decisions)

1. The local read-only Development query verified all approved catalog identities and five fiscal periods for `چخزر`.
2. Persisted storage scale and fiscal-period boundaries were verified. Units are million IRR for monetary lines, IRR/share for SourceItemId 160, thousand shares for SourceItemId 153, and percent for Gross Margin. Missing explicit Currency metadata is compatible under the Noavaran unit contract.
3. Persisted `IsAudited`, `IsRepresented` (restated), and `IsComposing` (consolidated) flags were preserved in selected report evidence. The implementation applies four-level report priority and existing announcement-date/statement-ID tie breaks; focused tests verify unresolved source conflicts. Different audit/restatement flags and revision lineages across adjacent periods remain valid.
4. The existing catalog titles and row IDs are preserved in database evidence.
5. The requested five quarters have persisted report and line-item observations; the complete result has no unavailable cells.

No additional Product Owner decision is open. The isolated PostgreSQL repository test was skipped in this rerun because its test-admin connection was not configured; the real-data Development use-case smoke passed read-only.

## Readiness

`IMPLEMENTED_AND_VALIDATED_AGAINST_LOCAL_DEVELOPMENT_DATA`. Product Owner choices are resolved. This evidence is not a production-data verification claim.
