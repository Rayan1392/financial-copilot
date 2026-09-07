# Feature 133 Story and Tasks Review

## 1. Verdict

NEED_CHANGES

## 2. Review Scope

Reviewed together:

- `Story.md` — one story, 33 acceptance criteria, configuration matrix, compatibility and non-goals.
- `Tasks.md` — three implementation slices, ten tasks, traceability table, test matrix and definition of done.
- Approved `design.md` and approved `design-review.md` as the authoritative upstream artifacts.

This is a review-only assessment. No implementation or architecture redesign is proposed.

## 3. Story Verification

The Story faithfully describes the approved bounded scenario: an original Telegram channel post is recognized deterministically, the existing company/month ingestion is reused, freshness is checked, and the existing billed trend capability may publish to the originating channel.

The Story preserves the approved boundaries: shared Feature 130 credential and tenant, existing billing/account resolver, no caller-selected identity, no new financial calculation engine, and no new gateway, broker, database, workflow engine, or parser architecture.

The four configuration combinations are explicitly stated and their downstream suppression behavior is consistent with the approved design. Defaults are explicitly false.

## 4. Acceptance Criteria Verification

All 33 ACs were reviewed individually.

- AC1–AC4 cover the original `channel_post` contract, channel type, signed IDs, absent `From`, captions, edits, media-only input, and same-channel destination.
- AC5–AC10 cover bounded recognition, one-month context, symbol extraction, Persian/Arabic normalization, Shamsi year/month validation, fiscal-date exclusion, conflicts, multi-month rejection, exact/unique company resolution, and no guessing.
- AC11–AC15 cover both options, defaults, startup validation, and all four configuration states.
- AC16–AC20 cover direct-ingestion reuse and the exact fresh requested-period snapshot guard without `SourceReportId`.
- AC21–AC24 cover the exact trend query, narrow routing correction, result validation, and isolated conversation.
- AC25–AC30 cover unchanged identity, Feature 130 linking, authenticated tenant/API-client authority, billing-account resolution, insufficient capacity, and no backfill-only billing/conversation.
- AC31–AC33 cover post/reply idempotency, failure suppression, bounded retries, deployment defaults, funding, and compatibility.

The ACs are generally atomic and testable. AC32 is intentionally broad, but its concrete failure areas are distributed across the tasks as permitted by the review instructions.

## 5. Tasks and Slice Verification

The three slices remain coherent and match the approved design:

1. Telegram input, recognition, authorization, and options binding.
2. Direct refresh, readiness, existing trend execution, routing, billing, and conversation.
3. Idempotency, delivery, configuration matrix, compatibility, and integration verification.

T133-1 through T133-6 provide concrete ownership for the primary trigger, parser, backend entry, refresh, readiness, routing, billing, conversation, and renderer behavior. T133-7 and T133-8 cover durable replay and same-channel delivery. T133-9 and T133-10 provide matrix and end-to-end verification.

T133-10 is broad, but it is framed as verification and does not replace implementation ownership for the main behavior; every AC is also mapped to an earlier implementation or verification task.

## 6. Configuration Matrix Verification

The Story and Tasks explicitly cover all four combinations:

| Backfill | Publish | Result |
|---|---|---|
| Off | Off | No ingestion, readiness, query, billing, conversation, rendering, or send. |
| On | Off | Direct refresh and readiness only; no query, billing, conversation, rendering, or send. |
| On | On | Refresh, readiness, existing billed trend query, validation, and same-channel publication. |
| Off | On | Safe skip with no stale-data query, stored-response replay, billing, conversation, rendering, or send. |

Defaults false, startup binding, disabled replay, and suppression assertions are present in the Story/Tasks test matrix.

However, the task set does not explicitly assign implementation ownership for the handler’s runtime evaluation of the two gates. T133-3 owns binding, T133-4 owns refresh/checkpoints, and T133-9 owns verification; none clearly states that the handler implements the four-state gate decision before side effects and on replay. This is recorded as F2 below.

## 7. Traceability Verification

The traceability table contains entries for AC1 through AC33, with no AC lacking a task mapping.

Each task has a clear upstream AC/design basis. T133-10 maps to all ACs, but the earlier tasks also own the implementation areas it verifies, so the table does not create a task-only implementation gap for the core behavior.

## 8. Test Matrix Verification

The matrix covers the required minimum scenarios:

- all four configuration states;
- irrelevant, malformed, unresolved/ambiguous, refresh failure/NoDataYet, and stale snapshot cases;
- duplicate delivery;
- query/wrong-result failure;
- Telegram transient failure and bounded retry;
- successful full path;
- Feature 130 link compatibility;
- tenant/account override attempts.

It also includes expected side effects for backfill, query, billing, conversation, and Telegram delivery. Routing coverage names V1, V2, semantic trend, and explicit Feature 129 comparison behavior in T133-6/T133-10.

The matrix is substantively complete, but it does not add rows for the approved first-time trigger-age rejection, retention behavior, processing deadline/ambiguous-work timeout, or state-file corruption disabling the channel branch. These omissions are part of F1.

## 9. Findings

### F1 — Required trigger-age, retention, timeout, and fail-closed state protections have no concrete implementation owner

Severity: Major

Source: Approved `design.md` sections 11, 16, and 17; `Tasks.md` T133-7, T133-8, and T133-10.

Issue: The approved design requires rejecting first-time posts older than 30 days, retaining channel records for 90 days, enforcing a bounded processing deadline below the primary request budget, treating expired in-flight work as review-required without re-execution, and disabling the channel branch when gateway state is corrupt or unavailable. The tasks mention generic corrupt/ambiguous-state tests and bounded send retries, but do not assign these runtime behaviors, configuration/settings, persistence/retention handling, or corresponding test rows to a concrete implementation task.

Why it matters: An implementation can satisfy the current task text while processing stale historical posts, allowing a hung request to block polling, automatically repeating ambiguous external work, or silently continuing with unsafe state. These are freshness, duplicate-work, and operational-safety requirements from the approved design, not optional observability details.

Required correction: Add explicit implementation ownership—preferably to T133-7/T133-8 or a narrowly scoped task within Slice 3—for age rejection, 90-day retention, processing deadline and terminal review-required transition, state-corruption fail-closed behavior, and the associated integration/test-matrix cases. T133-10 may verify these behaviors but must not be their only implementation owner.

### F2 — Runtime ownership of the independent four-state gates is underspecified

Severity: Major

Source: Approved `design.md` sections 16–17; `Story.md` AC11–AC15; `Tasks.md` T133-3, T133-4, and T133-9.

Issue: T133-3 explicitly owns options binding and T133-9 explicitly owns matrix verification, but no implementation task clearly states that the Feature 133 handler evaluates `AutoBackfillEnabled` before readiness/acquisition and `AutoPublishTrendEnabled` before query, billing, conversation, rendering, and send—including replay/resume. T133-4 only says “persist conditional stages,” which does not define the gate decision or all required side-effect suppression.

Why it matters: Binding and tests can pass while the handler still queries stale data, creates a conversation, reserves billing, renders, or replays stored analysis in an enabled-publication/disabled-backfill state. The requested independent toggles must be implementation-visible, not only configuration-visible and test-visible.

Required correction: Make a concrete handler task explicitly own the four-state decision table, gate ordering, terminal disabled outcomes, and gate checks during resume/replay. Keep T133-9 as verification, not the sole owner.

## 10. Implementation Readiness

Not ready for implementation as written. The core business flow and most safety requirements are sufficiently specified, but F1 and F2 leave material runtime behavior open. Correcting those task ownership gaps should be sufficient; the approved architecture does not need to be reopened.

## 11. Summary

| Measure | Result |
|---|---:|
| Story count reviewed | 1 |
| AC count reviewed | 33 |
| Slice count reviewed | 3 |
| Task count reviewed | 10 |
| ACs without task mapping | 0 |
| Tasks without design/AC ownership | 0 |
| Blockers | 0 |
| Majors | 2 |
| Minors | 0 |
| Notes | 0 |

Configuration matrix result: all four combinations are documented and tested, but explicit runtime gate ownership must be added.

Feature 130 compatibility result: covered and consistent with the approved design; unchanged credential, tenant validation, account linking, callbacks, ordinary messages, and mismatched-tenant rejection are explicitly protected.

The existing `design.md`, `design-review.md`, `Story.md`, and `Tasks.md` files were not modified. Only this review file was created in the requested feature directory.

Scoped status may show the feature directory as untracked because that is the repository’s pre-existing status for this directory; no existing specification file was edited.

STORY_TASKS_REVIEW_NEEDS_CHANGES
