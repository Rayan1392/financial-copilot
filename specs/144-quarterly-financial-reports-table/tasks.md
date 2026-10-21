# Feature 144 Implementation Tasks â€” Quarterly Financial Reports Table

This is a design package. Every unchecked task below is future implementation work; none authorizes production code as part of this design update. The approved source mappings and Product Owner policies are inputs, not discovery tasks.

## Data retrieval and source identity

- [x] **T01** Reuse the existing provider-scoped Source Item Catalog and verify that `NoavaranCurrentApi` / `IncomeStatement` contains the 15 approved SourceItemIds and UUIDs from `spec.md` in the target catalog; allow null MetricCode and do not create a parallel catalog.
- [x] **T02** Add a typed Application request/result boundary for the quarterly table, reusing canonical company resolution and returning unresolved/ambiguous company outcomes.
- [x] **T03** Extend the existing statement repository to retrieve a bounded ordered set of compatible cumulative reports for one company, provider, statement type, explicit/unique scope, and requested fiscal periods.
- [x] **T04** Retrieve all 15 approved source rows, including rows with numeric zero; retain provider, catalog UUID, SourceItemId, source row ID, report ID, report variant, unit, and period identity.
- [x] **T05** Enforce compatible company, provider, IncomeStatement type, fiscal year, independent report scope, source identity, unit, and persisted million-rial storage scale. Do not require explicit Currency metadata or cross-period equality of audit/restatement flags or revision lineage.
- [x] **T06** Rank report variants independently within each scope: audited+restated, audited+not-restated, unaudited+restated, unaudited+not-restated. Reuse announcement-date/statement-ID tie-breaks; retain unresolved conflicting values as typed conflicts.
- [x] **T07** Resolve fiscal quarter boundaries from persisted/report evidence; handle non-Esfand fiscal years and leave unsupported/ambiguous boundaries explicitly unavailable.
- [x] **T08** Confirm exact original catalog titles for SourceItemIds 12 and 278 at implementation; use the catalog title and do not invent one.

## Quarter normalization and calculations

- [x] **T09** Implement Q1 pass-through and adjacent Q2/Q3/Q4 cumulative differences for the approved additive flow items using signed full-precision decimals.
- [x] **T10** Reject missing intermediate periods, cross-year operands, conflicting duplicates, and incompatible report variants; preserve unavailable state/reason and never substitute missing with zero.
- [x] **T11** Preserve normalization lineage for each value, including both operand observation, line-item, and report identities where subtraction is performed.
- [x] **T12** Historical EPS subtraction behavior is superseded by the approved evidence-checked comparable-EPS calculation described in E1–E7.
- [x] **T13** Keep SourceItemId 153 as its selected reported value without subtraction or monetary conversion; expose unit `thousand shares`.
- [x] **T14** Implement Gross Margin as the only derived financial metric, after normalizing SourceItemIds 139 and 15; return unavailable for missing operands or zero revenue.
- [x] **T15** Implement typed QoQ comparison from standalone values, with exact absolute delta where meaningful, relative percent only for a valid positive base, and separate relative-margin/percentage-point comparison kinds.
- [x] **T16** Preserve signs and decimal precision across normalization, margin, comparisons, and serialization; round only in presentation.

## Response, rendering, and AI integration

- [x] **T17** Return exactly 16 rows in the `spec.md` order with approved Persian labels, stable row IDs, 15 direct source identities, and Gross Margin as the only derived row.
- [x] **T18** Carry declared units/currency, precision, fiscal dates, freshness, selected report revisions, source provenance, calculation lineage, and cell availability/reason in typed output.
- [x] **T19** Preserve numeric zero as `0` with `Available` state and a zero-as-dash presentation hint; render final financial zeros as `-`, including valid zeros for SourceItemIds 12 and 278.
- [x] **T20** Preserve missing/unavailable as null with distinct typed state/reason and accessible rendering; prove serialization and replay do not collapse it into available zero.
- [x] **T21** Preserve negative numeric values through structured output and render RTL/accounting negatives without changing arithmetic values.
- [x] **T22** Extend the existing V2 specialized capability/tool route to call the Application use case; leave V1 frozen and do not enable generic LLM-generated SQL for this capability.
- [x] **T23** Extend assistant structured payload serialization, conversation persistence/replay, and table rendering without replacing numeric values with display strings or recalculating from prose.
- [x] **T24** Reuse existing authorization/actor context and outer Billing reservation/finalization; verify one overall charge and no tool-level nested charge.
- [x] **T25** Keep the use case directly callable without an LLM; add a protected HTTP endpoint only if an established external-client requirement exists.
- [x] **T26** Make no query-time Noavaran/provider call and introduce no schema migration unless implementation demonstrates a missing required field.

## Tests and verification

- [x] **T27** Unit-test revenue fixtures: Q1 `7,365,921`; Q2 `16,754,501 - 7,365,921 = 9,388,580`.
- [x] **T28** EPS fixtures preserve raw cumulative values and never subtract EPS across reports; comparable EPS is derived from standalone attributable profit and an evidence-checked share basis.
- [x] **T29** Unit-test raw reported share count `200,000` remains unchanged in provenance while the displayed comparison share basis is separately typed as thousand shares.
- [x] **T30** Unit-test normalized Gross Margin, zero revenue, and missing gross-profit/revenue outcomes.
- [x] **T31** Assert all 15 direct SourceItemIds and catalog identities match `spec.md`; Gross Margin has no SourceItemId; result has exactly 16 rows in prescribed order.
- [x] **T32** Unit-test numeric zero remains Available and displays `-`, including SourceItemIds 12 and 278, zero-to-zero and equal-YTD differences, and zero after normalization.
- [x] **T33** Unit-test missing-vs-zero behavior: missing YTD3M with YTD6M zero yields unavailable/null and never computes `0 - 0`.
- [x] **T34** Unit-test signed negative flow subtraction, including Q2 cost `-10,000,000 - (-4,751,954) = -5,248,046`.
- [x] **T35** Test fiscal calendar boundaries, non-Esfand year ends, adjacent-period requirements, and unsupported/missing fiscal evidence.
- [x] **T36** Repository-test provider/source isolation, separate/consolidated isolation, compatible report versions, conflicting revisions/duplicates, row zeros, ending-period comparison-reference retrieval, and full provenance. The isolated PostgreSQL suite passed 4/4.
- [x] **T37** Test units, currency/storage scale, full precision, renderer rounding, RTL labels, and accounting negatives. Backend rendering, frontend 16-row RTL component output, typed API boundary, and browser PNG export were verified.
- [x] **T38** Test QoQ positive, negative, sign-change, zero-base, available-zero, missing, and unavailable cases, including distinct percent and percentage-point margin changes.
- [x] **T39** Integration-test V2 structured response, direct Application invocation, conversation persistence/replay, authorization, and exactly-once Billing behavior.
- [x] **T40** Run supported PostgreSQL integration coverage and regressions for existing statement table, Feature 141, Feature 142, and Feature 143 behavior. Feature 144's isolated PostgreSQL suite passed 4/4, its V2 table response/replay test passed 1/1, and Development smoke checks passed read-only. Historical Feature 143 handler failures remain classified in `implementation-report.md`.

## Delivery notes

- [x] **T41** Unit-test all four report-priority levels, audited/not-restated over unaudited/restated, independent scope selection, cross-period variant/revision compatibility, missing Currency, and unresolved conflicting top-priority values.

- Implementation task count: **41**; **41 completed with evidence**. See `implementation-report.md` and `real-data-validation.md` for limits and remaining acceptance blockers.
- Product Owner mapping discovery/approval tasks are intentionally absent because those decisions are resolved.
- The Feature 144 report records backend/frontend verification boundaries and does not claim clean full-suite results or full acceptance completion.

## Supplemental Noavaran source-item fallbacks (2026-10-21)

- [x] **F1** Document approved table-row fallbacks `139 ← 310`, `140 ← 311`, `142 ← 312`, and `143 ← 313`; prefer the primary when it has a value and retain the selected source identity in provenance.
- [x] **F2** Include supplemental IDs `310`, `311`, `312`, `313`, and `317` in statement retrieval; apply profit-row fallbacks only when the primary is absent or has a null amount, while duplicate primary observations remain ambiguous.
- [x] **F3** Derive comparable EPS from standalone row-143 net profit (using 313 when 143 is absent/null) and verified shares; raw items 160/317 are optional provenance and do not gate or supply the displayed value.

## EPS and comparative share-basis correction (2026-10-20)

The 41 completed tasks above describe the **original** implementation. T28's EPS subtraction expectation and T29's displayed-raw-share expectation are superseded for the correction; retain and revise their tests rather than deleting them. [Source investigation and approved methodology](eps-share-basis-investigation.md).

- [x] **E1** Read-only Qasem Development statement inventory and one live FY1405 6M Noavaran source response; record selected report IDs, 143/144/153/160/317 operands, units, scope, and evidence limits.
- [x] **E2** Revise `spec.md` and `design.md` to prohibit unverified EPS subtraction, preserve raw observations, and define the Product Owner's rolling same-quarter comparison rule.
- [x] **E3** Product Owner approved evidence-checked comparable EPS, explicit denominator qualification, rounding, fallback, and invalid-reference behavior.
- [x] **E4** Implemented typed EPS/share-basis calculation in the existing V2 Application/repository path, preserving source provenance and selecting separate statements only.
- [x] **E5** Updated backend renderer, web table, and PNG to use the same typed values and expose compact per-quarter provenance without adding a row.
- [x] **E6** Replaced invalid EPS-subtraction expectations and added Qasem rebasing, future-quarter arrival, missing-reference, ending-period lookup, source-provenance, V2 replay, frontend, and PNG regressions.
- [x] **E7** Ran focused unit (39/39), V2 persistence/replay (1/1), PostgreSQL integration (4/4), frontend (9/9), read-only Development smoke checks, browser PNG export, and production build checks.

## Production missing profit rows (2026-10-21)

- [x] **B1** Inspect production read-only: confirm the 1405/06/31 separate income statement exists and contains gross profit as SourceItemId 310 and net profit as SourceItemId 313, while primary IDs 139 and 143 are absent from that report.
- [x] **B2** Add bounded retrieval and primary-first fallback mapping `310 → 139` and `313 → 143`; preserve the approved table row IDs and use the supplemental catalog item only when the primary item is absent.
- [x] **B3** Keep the EPS row derived from standalone net profit and shares; accept approved fallback 313 when item 143 is absent, without requiring item 160 or 317. Update the feature contract and record the production incident in this folder.
- [ ] **B4** Run focused Feature 144 unit and PostgreSQL integration coverage for primary-preferred selection, fallback selection, and quarter subtraction.
