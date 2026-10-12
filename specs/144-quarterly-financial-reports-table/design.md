# Feature 144 Design — Quarterly Financial Reports Table

## Design summary

Build on the persisted financial-statement query and structured-response infrastructure. A deterministic Application use case reads compatible Noavaran income-statement reports, produces standalone-quarter cells and QoQ comparisons, and returns source/calculation provenance. Keep the V2 route specialized, reuse the existing provider-scoped source catalog, preserve the V1 freeze, and make no schema change unless implementation evidence proves a required field is absent.

## Confirmed data contract

- Canonical provider key: `NoavaranCurrentApi`; display name: Noavaran Amin.
- Statement type: `IncomeStatement`.
- Approved mapping matrix, row order, direct source catalog UUIDs, labels, and policies are recorded in `spec.md`; that table is authoritative.
- Exactly 15 rows are directly sourced. Gross Margin is the one and only derived financial metric.
- SourceItemId is provider and statement-type scoped. `MetricCode` is optional and must not gate retrieval.
- Noavaran 3M/6M/9M/12M income-statement amounts are cumulative YTD. All approved additive rows, including EPS per its explicit policy, normalize by adjacent differences. SourceItemId 153 share count is reported unchanged.

## Existing architecture reuse

| Boundary | Decision | Feature 144 use |
|---|---|---|
| Company resolution | Reuse `ICompanyResolverService` | Resolve ticker/name to canonical company/provider identity; ambiguity is typed. |
| Statement storage and catalog | Reuse existing `FinancialStatements`, `FinancialStatementLineItems`, `FinancialStatementSourceItems` and existing provider-scoped catalog | Query the approved source identities; do not add a catalog or ingestion pipeline. |
| Statement table query | Extend Feature 083 query/repository boundary | Read a bounded set of report headers and line items with report/version identity rather than a single latest header. |
| Period arithmetic | Reuse compatible arithmetic concepts from Feature 026; implement a small typed statement policy if needed | Existing `CodalDiscreteQuarterDeriver` is not used unguarded because Feature 144 must validate provider, company, scope, source, unit and provenance. Adjacent periods may use different report variants. |
| Metric formulas | Reuse governed definitions where compatible | Gross Margin and comparison arithmetic only; no reconstruction of direct statement lines. |
| AI routing | Extend the existing V2 specialized capability/tool flow | Route quarterly table requests to the same Application use case; do not enable generic Feature 141 SQL fallback or add V1 behavior. |
| Result and replay | Extend current structured assistant payload and replay mapping | Preserve decimals, typed states, source provenance, and display hints across save/replay. |
| Billing and authorization | Reuse outer AI operation | Existing actor/tenant authorization; one reservation/finalization for the overall query. |
| Persistence schema | Reuse existing schema | Derived cells are response-time values; no migration is justified by current evidence. |

Relevant repository boundaries are the Feature 083 financial statement table contracts/repository, Feature 081 period comparison policies, existing AI orchestration/facade response and replay, and the persisted financial statement entities. Feature 142 remains the acquisition path; Feature 143 remains its Telegram trigger. Neither is called or modified by this read-only query.

## Source selection and data retrieval

1. Resolve the company using the canonical company resolver. Require a unique canonical company identity.
2. Select provider `NoavaranCurrentApi`, statement type `IncomeStatement`, and one statement scope. Within each scope, rank reports by audited+restated, audited+not-restated, unaudited+restated, unaudited+not-restated. Use the existing announcement-date and statement-ID tie breakers; retain conflicts when same-rank tied values disagree.
3. Read enough reports for the requested 1–12 consecutive quarters plus any prior cumulative observation needed for Q4 or a comparison. Bound the query; retain full report and line-item IDs.
4. Fetch the 15 approved source IDs through the existing provider-scoped source catalog. SourceItemId alone is not a cross-provider key. A null MetricCode does not make an approved source unavailable.
5. Retain source title/catalog metadata for the two items whose original title was not provided (12 and 278); do not invent titles.
6. Exclude or mark unavailable any cell with a missing/conflicting/incompatible report, source, period, scope, unit/storage scale, or fiscal year. Missing Currency metadata and differences in audit/restatement class or revision lineage across adjacent periods do not independently block calculation. Never collapse separate and consolidated statements.

Report version precedence, exact fiscal-period boundaries, and provider catalog seeding must use existing persisted evidence and established repository policy. If there is no unique compatible result, return a typed conflict/unavailable reason; do not guess. No production observations or database contents are asserted as inspected by this design update.

## Normalization algorithm

Represent source observations as typed values with period, company, provider, statement type, source catalog identity, source item ID, scope, unit/currency, accounting basis, report/revision identity, and decimal value. Preserve absent observations distinctly from numeric zero.

For each approved cumulative flow row, first group by fiscal year, company/provider, source identity, scope, accounting basis, unit, and compatible revision lineage. Validate fiscal start/end and period duration. For an eligible row:

| Standalone quarter | Formula | Inputs |
|---|---|---|
| Q1 | `YTD_3M` | Current fiscal year's 3M report |
| Q2 | `YTD_6M - YTD_3M` | Same fiscal year, adjacent compatible reports |
| Q3 | `YTD_9M - YTD_6M` | Same fiscal year, adjacent compatible reports |
| Q4 | `YTD_12M - YTD_9M` | Same fiscal year, adjacent compatible reports |

Use signed decimal subtraction without intermediate rounding. Missing observations, conflicting duplicate periods, non-adjacent inputs, cross-year boundaries, or incompatible versions produce null plus a typed reason. Do not replace a missing operand with zero. Carry both input report and line-item identities into each normalized cell.

### EPS policy

Treat SourceItemId 160 as a directly sourced statement row with Product Owner-approved cumulative normalization. Apply the same Q1 and adjacent Q2–Q4 formulas; do not treat EPS as a ratio/stock exception for this feature. Preserve exact source operands and derived standalone EPS. Fixture: `24,967 - 10,155 = 14,812`. Do not investigate or mention the excluded one-unit screenshot discrepancy.

### Share-count policy

Treat SourceItemId 153 as a point-in-time reported value. Select the value associated with the fiscal reporting period under the compatible report policy and preserve it unchanged. Unit is thousand shares. Do not subtract cumulative observations, multiply by nominal share value, convert to monetary capital, or derive from net income/EPS.

### Gross Margin

Normalize direct revenue (15) and gross profit (139) first. When both values are available and revenue is nonzero, compute `grossProfitQuarter / revenueQuarter * 100` at full decimal precision. Otherwise return unavailable and an explicit reason. Gross Margin is the only derived metric; every other financial row is the approved reported source item, with EPS's normalization remaining a source-row normalization rather than a new metric.

## Zero, missing, sign, and unit semantics

Use a typed cell model such as `{ value: decimal?, state, reason?, unit, displayHint }`:

- Available zero: `value = 0`, `state = Available`, `displayHint = ZeroAsDash`; renderer shows `-`.
- Missing or unavailable: `value = null`, a non-Available state and reason; renderer can use a placeholder but accessible metadata must distinguish it from available zero.
- Nonzero positive/negative: preserve exact decimal and sign; render according to unit and accounting display policy.

Zero-as-dash applies to final financial table cells, including SourceItemIds 12 and 278 when their resolved values are zero, normalized EPS, and share count. It does not turn a numeric value into a string or null. A missing 3M with 6M zero cannot yield Q2 zero. Noavaran source unit conventions in the feature are million Iranian rials for non-per-share monetary rows, Iranian rials/share for EPS, thousand shares for item 153, and percent for Gross Margin. Carry units explicitly, confirm active storage scale when implemented, preserve precision, and round only at rendering.

Keep expense/tax/source signs intact. Do not parse formatted Persian strings for calculation. Negative display may use accounting parentheses without changing the numeric value.

## QoQ comparisons

Compare adjacent standalone-quarter values, never cumulative source observations. Compute exact absolute delta where both values exist. Compute relative percent `(current / previous - 1) * 100` only for a valid positive base. Zero/negative bases, sign changes, and unavailable inputs return typed statuses and must not display misleading relative growth. Gross Margin reference display uses relative percentage change; any percentage-point change is a separate comparison kind/value.

## Typed response and rendering

Extend existing structured financial-statement response infrastructure with `QuarterlyFinancialReportTableResult` (or the existing equivalent if one is introduced during implementation). Metadata includes canonical company/ticker, provider, statement type/scope, fiscal calendar evidence, requested quarter count, source freshness, units/currency, and selected report revisions. Columns carry fiscal year/quarter, dates, order, and coverage. Rows carry stable ID, approved Persian label/order, nullable SourceItemId (null only for Gross Margin), catalog identity, direct/derived classification, normalization policy, unit/precision, and cells. Cells carry decimal-or-null, state/reason, source report/line-item IDs, normalization operands, calculation provenance, and presentation hint. Comparisons carry quarter pair, absolute delta, valid relative/percentage-point values, and typed state.

Do not store formatted values in place of numbers. Conversation persistence and replay must preserve `0` vs `null`, state, reason, units, and provenance without recomputing values from prose. The renderer owns RTL layout, Persian number formatting, dash rendering, and accounting negatives. Existing authorization applies; tool execution does not reserve or charge independently of the outer AI operation.

## Validation and failure handling

Return explicit typed outcomes for unresolved/ambiguous company, missing source item/report, unsupported period, incompatible period/scope/accounting basis/unit, missing cumulative predecessor, conflicting duplicate/revision, unavailable Gross Margin, unavailable QoQ percent, and stale/missing data. Fail closed for the affected cell/quarter; preserve other independently valid cells. Keep warning/reason codes stable enough for structured clients. Do not silently pick a provider, scope, report revision, fiscal calendar, unit scale, or accounting interpretation where repository evidence does not identify a unique choice.

## Persistence and migrations

No schema change is currently justified. Existing statement/report and source-row identities, source catalog UUIDs, periods, provider, variants and numeric values should support an initial response-time table. If implementation confirms a required active datum is absent, document that concrete gap before proposing the smallest backward-compatible migration. Do not persist derived quarterly observations without a demonstrated reuse need and an invalidation/versioning contract.

## Implementation test design

Implement deterministic fixture coverage specified in `spec.md` and `tasks.md`: revenue and EPS arithmetic, share preservation, normalized Gross Margin, full mapping/order, zeros (including IDs 12/278), missing vs zero, negatives, fiscal boundaries, period/scope/revision conflicts, units/provenance, V2 structured response/replay, authorization and one Billing charge. Tests are future implementation tasks; no tests are run as part of this design-only update.
