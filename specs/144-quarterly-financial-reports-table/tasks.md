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
- [x] **T12** Implement the approved EPS policy for SourceItemId 160: Q1 = YTD3M and Q2/Q3/Q4 = current YTD minus adjacent prior YTD; include fixture `24,967 - 10,155 = 14,812`.
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
- [x] **T28** Unit-test EPS fixtures: Q1 `10,155`; Q2 `24,967 - 10,155 = 14,812`.
- [x] **T29** Unit-test reported share count `200,000` remains unchanged and is typed as thousand shares.
- [x] **T30** Unit-test normalized Gross Margin, zero revenue, and missing gross-profit/revenue outcomes.
- [x] **T31** Assert all 15 direct SourceItemIds and catalog identities match `spec.md`; Gross Margin has no SourceItemId; result has exactly 16 rows in prescribed order.
- [x] **T32** Unit-test numeric zero remains Available and displays `-`, including SourceItemIds 12 and 278, zero-to-zero and equal-YTD differences, and zero after normalization.
- [x] **T33** Unit-test missing-vs-zero behavior: missing YTD3M with YTD6M zero yields unavailable/null and never computes `0 - 0`.
- [x] **T34** Unit-test signed negative flow subtraction, including Q2 cost `-10,000,000 - (-4,751,954) = -5,248,046`.
- [x] **T35** Test fiscal calendar boundaries, non-Esfand year ends, adjacent-period requirements, and unsupported/missing fiscal evidence.
- [x] **T36** Repository-test provider/source isolation, separate/consolidated isolation, compatible report versions, conflicting revisions/duplicates, row zeros, and full provenance. Prior isolated PostgreSQL evidence is retained; the repository test was skipped in this run because the disposable-database admin connection was not configured.
- [x] **T37** Test units, currency/storage scale, full precision, renderer rounding, RTL labels, and accounting negatives. Backend rendering, frontend 16-row RTL component output, and the typed API boundary were verified; browser pixel layout was not captured.
- [x] **T38** Test QoQ positive, negative, sign-change, zero-base, available-zero, missing, and unavailable cases, including distinct percent and percentage-point margin changes.
- [x] **T39** Integration-test V2 structured response, direct Application invocation, conversation persistence/replay, authorization, and exactly-once Billing behavior.
- [x] **T40** Run supported PostgreSQL integration coverage and regressions for existing statement table, Feature 141, Feature 142, and Feature 143 behavior. The read-only Development smoke and 17 focused V2/Feature 142/143 integration tests passed; the isolated PostgreSQL repository suite was skipped because its admin connection was unset. The three Feature 143 handler failures were rerun and classified in `implementation-report.md`.

## Delivery notes

- [x] **T41** Unit-test all four report-priority levels, audited/not-restated over unaudited/restated, independent scope selection, cross-period variant/revision compatibility, missing Currency, and unresolved conflicting top-priority values.

- Implementation task count: **41**; **41 completed with evidence**. See `implementation-report.md` and `real-data-validation.md` for limits and remaining acceptance blockers.
- Product Owner mapping discovery/approval tasks are intentionally absent because those decisions are resolved.
- The Feature 144 report records backend/frontend verification boundaries and does not claim clean full-suite results or full acceptance completion.
