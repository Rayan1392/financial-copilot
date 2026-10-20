# Feature 144 Design — Quarterly Financial Reports Table

## Design summary

Build on the persisted financial-statement query and structured-response infrastructure. A deterministic Application use case reads compatible Noavaran income-statement reports, produces standalone-quarter cells and QoQ comparisons, and returns source/calculation provenance. Keep the V2 route specialized, reuse the existing provider-scoped source catalog, preserve the V1 freeze, and make no schema change unless implementation evidence proves a required field is absent.

## Confirmed data contract

- Canonical provider key: `NoavaranCurrentApi`; display name: Noavaran Amin.
- Statement type: `IncomeStatement`.
- Approved mapping matrix, row order, direct source catalog UUIDs, labels, and policies are recorded in `spec.md`; that table is authoritative.
- Exactly 16 display rows remain. Source observations are immutable; Gross Margin is derived, and EPS/share display cells may carry separate comparative values under the revised contract.
- SourceItemId is provider and statement-type scoped. `MetricCode` is optional and must not gate retrieval.
- Noavaran 3M/6M/9M/12M **monetary flow** amounts are cumulative YTD and normalize by adjacent differences. Source EPS item 160 is a ratio and must not take that subtraction path without verified equal bases. Source share item 153 is immutable; its display comparison basis is separately derived. See [the source investigation](eps-share-basis-investigation.md).

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
2. Select provider `NoavaranCurrentApi`, statement type `IncomeStatement`, and **separate/non-consolidated scope only**. Within that scope, rank reports by audited+restated, audited+not-restated, unaudited+restated, unaudited+not-restated. Use the existing announcement-date and statement-ID tie breakers; retain conflicts when same-rank tied values disagree. Never substitute a consolidated report.
3. Read enough reports for the requested 1–12 consecutive quarters, any adjacent YTD predecessor, and matching same-quarter following-year reference needed for comparison. Bound the query; retain full report and line-item IDs. A reference report can fall outside the displayed five columns.
4. Fetch the 15 display-source IDs plus supporting attributable-profit item 144 and, when present, Codal EPS item 317 through the existing provider-scoped catalog. These supporting items do not add display rows. SourceItemId alone is not a cross-provider key. A null MetricCode does not make an approved source unavailable.
5. Retain source title/catalog metadata for the two items whose original title was not provided (12 and 278); do not invent titles.
6. Exclude or mark unavailable any cell with a missing/conflicting/incompatible report, source, period, scope, unit/storage scale, or fiscal year. Missing Currency metadata and differences in audit/restatement class or revision lineage across adjacent periods do not independently block calculation. Never collapse separate and consolidated statements.

Report version precedence, exact fiscal-period boundaries, and provider catalog seeding must use existing persisted evidence and established repository policy. If there is no unique compatible result, return a typed conflict/unavailable reason; do not guess. Qasem Development and live source observations for the correction are documented separately in [the investigation](eps-share-basis-investigation.md); no Production FinancialCopilot records were inspected.

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

Keep SourceItemId 160 and optional 317 as immutable cumulative ratio observations. Never subtract them. Derive comparable EPS from standalone item 144 divided by the approved share basis; when item 144 is absent, use item 143 only for a verified separate statement whose source EPS reconciles to item 143 and its reported shares. Record the numerator source ID. Convert `million IRR × 1,000 / thousand shares`, then round to the nearest whole rial, midpoint away from zero. Validate catalog identities and units, separate scope, supported scale, positive shares, and source EPS within one rial; if item 317 is present it must also reconcile within one rial. Use item 153 from the same quarter in FY `Y+1` when present; otherwise use the own-period reported shares as a clearly identified fallback. The denominator is a reported period-end count, not weighted-average shares. If the reference share evidence or numerator evidence fails, return typed unavailable. Keep the database's original row labels; identify the derived calculation in provenance and the EPS methodology note. See [investigation](eps-share-basis-investigation.md).

### Share-count policy

Keep SourceItemId 153 as a point-in-time **raw** observation in thousand shares. The typed result holds a distinct displayed/comparable share basis, reference fiscal quarter and report ID, derivation policy, and availability state/reason. When a verified FY `Y+1` same-quarter report exists, use its approved basis for FY `Y` quarter `Q`; recompute after arrival/restatement. Never apply a single latest share count to all old periods. If no matching next-year report exists, verified own-period reported shares are the explicit fallback. If a matching report exists but its share observation is missing, zero, duplicate, or incompatible, return unavailable. Do not overwrite the original item 153 line.

### Gross Margin

Normalize direct revenue (15) and gross profit (139) first. When both values are available and revenue is nonzero, compute grossProfitQuarter / revenueQuarter * 100 at full decimal precision. Otherwise return unavailable and an explicit reason. Comparable EPS is a derived presentation value under the approved evidence policy; the row retains its database title and provenance distinguishes the derived value from source EPS. No other monetary row is reconstructed or rebased.

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

Extend existing structured financial-statement response infrastructure with `QuarterlyFinancialReportTableResult`. Metadata includes canonical company/ticker, provider, separate scope, fiscal calendar evidence, requested quarter count, source freshness, units/currency, and selected report revisions. Columns carry fiscal year/quarter, dates, order, and coverage. Rows carry stable ID, approved Persian label/order, nullable SourceItemId (null only for Gross Margin), catalog identity, source/derived-presentation classification, normalization policy, unit/precision, and cells. Cells carry decimal-or-null, state/reason, source report/line-item IDs, normalization operands, calculation provenance, and presentation hint. EPS/share cells additionally require distinct raw observation and displayed comparison value, numerator/denominator evidence, reference fiscal quarter/report ID or explicit own-period fallback, rebase method, and availability reason. Comparisons carry quarter pair, absolute delta, valid relative/percentage-point values, and typed state.

Do not store formatted values in place of numbers. Conversation persistence and replay must preserve `0` vs `null`, state, reason, units, and provenance without recomputing values from prose. The renderer owns RTL layout, Persian number formatting, dash rendering, and accounting negatives. Existing authorization applies; tool execution does not reserve or charge independently of the outer AI operation.

## Validation and failure handling

Return explicit typed outcomes for unresolved/ambiguous company, missing source item/report, unsupported period, incompatible period/scope/accounting basis/unit, missing cumulative predecessor, conflicting duplicate/revision, unavailable Gross Margin, unavailable QoQ percent, and stale/missing data. Fail closed for the affected cell/quarter; preserve other independently valid cells. Keep warning/reason codes stable enough for structured clients. Do not silently pick a provider, scope, report revision, fiscal calendar, unit scale, or accounting interpretation where repository evidence does not identify a unique choice.

## Persistence and migrations

No schema change is currently justified. Existing statement/report and source-row identities, source catalog UUIDs, periods, provider, variants and numeric values should support an initial response-time table. If implementation confirms a required active datum is absent, document that concrete gap before proposing the smallest backward-compatible migration. Do not persist derived quarterly observations without a demonstrated reuse need and an invalidation/versioning contract.

## Implementation test design

Evidence-backed regression cases cover Qasem changing shares and same-quarter next-year rebasing; positive FY1404 Q4 profit with no false `−518` EPS; FY1405 Q2 with no false `171` EPS; later FY1405 Q4 arrival; stable shares; missing/zero/conflicting denominator; absent future counterpart; restatement; non-Esfand fiscal year; separate-only scope; units/rounding; raw-vs-comparable provenance; typed V2 response/replay; web and PNG rendering. Retain monetary-flow, Gross Margin, zero/missing, authorization, and one-charge regressions. Older implementation reports describe the superseded EPS subtraction behavior.
