# Tasks — Fund Portfolio Workbook Upload and Processing Experience

## 1. Product and Information Architecture

- [ ] Confirm the dedicated admin route, navigation label, authorization boundary, and redirect/link behavior from `/admin/data-management/fund-reports`.
- [ ] Define the canonical stage taxonomy and mapping from Feature 100–105 backend statuses to user-facing Persian labels.
- [ ] Define terminal, non-terminal, retryable, and review-required states and the next action for each.
- [ ] Document the post-upload flow in the product/operator guide, including the distinction between manual upload and optional configured-source discovery.

## 2. Backend Readiness and Progress Contracts

- [ ] Audit the existing Feature 101 upload/run/item/report contracts against the dedicated page requirements without breaking current clients.
- [ ] Add a versioned readiness contract covering manual upload, raw storage, database, queue/worker, optional discovery source, and analytics dependencies.
- [ ] Add a versioned run-progress/timeline contract with stage status, timestamps, safe messages, issue counts, coverage, correlation id, and next actions.
- [ ] Persist or derive stage transitions deterministically from existing run/report/normalization/recalculation evidence; do not invent completion states in the frontend.
- [ ] Expose bounded item-level progress for single and future multi-file runs, including retry/cancel/reprocess eligibility.
- [ ] Ensure readiness failures have stable codes and operator remediation guidance; keep secrets and unrestricted exception details out of responses.
- [ ] Ensure manual upload readiness is independent from the optional configured local/object-storage discovery status.
- [ ] Replace or harden the process-local manual upload handoff so uploaded bytes and download tokens are durable and readable by a separate Worker process before `202 Accepted` is returned.
- [ ] Add restart/cross-process tests proving a queued manual item can be downloaded and processed after the API request has completed.

## 3. Manual Upload and Processing Page

- [ ] Create the dedicated Persian RTL upload page and route with DataAdmin authorization.
- [ ] Implement file selection, drag/drop, extension/MIME/size validation, optional fund hint, preview, submit, cancel, and duplicate-submit protection.
- [ ] Show `202 Accepted` as queued acknowledgement and immediately navigate to the run-progress view.
- [ ] Render the complete processing stepper with active, completed, skipped, partial, blocked, and failed states.
- [ ] Add bounded polling or an approved live-update mechanism with stale-update detection, cleanup on unmount, and retry/backoff.
- [ ] Add run header, item table, progress counters, timestamps, attempt/correlation data, and last-update indicator.
- [ ] Add deep-link/reload reconstruction for run ids and report ids.

## 4. Readiness, Error, and Recovery UX

- [ ] Render separate readiness cards for manual upload, raw storage, Worker/queue, database, discovery source, and analytics inputs.
- [ ] Show actionable states for storage misconfiguration, disabled Worker, queue lag, parser failure, partial parse, mapping review, reconciliation failure, and unavailable analytics.
- [ ] Link issue counts to report issues and mapping-review detail; never expose unrestricted workbook cells.
- [ ] Add authorized retry, cancellation, reprocess, and corrected-revision navigation with explicit confirmation where required.
- [ ] Distinguish duplicate, corrected revision, imported, partial, needs review, retryable failure, poisoned/final failure, and cancelled outcomes.
- [ ] Show report/intelligence result only after the backend confirms persisted output; show confidence, coverage, freshness, source revision, and unavailable reasons.

## 5. Integration with Features 105–110

- [ ] Link successful Feature 105 analytics to the fund intelligence result and preserve calculation version/comparable-period evidence.
- [ ] Show Features 106–108 outputs only when their backend contracts report eligible, persisted results; otherwise show explicit unavailable/skipped reasons.
- [ ] Prepare a typed handoff context for Feature 109 fund detail and Feature 108 AI follow-up without copying calculations into React.
- [ ] Preserve Feature 110’s future event boundary; do not emit notifications directly from the upload page.
- [ ] Keep report revisions, source evidence, delayed monthly-disclosure wording, confidence, and non-recommendatory semantics visible throughout the handoff.

## 6. Accessibility, Localization, and Security

- [ ] Implement RTL Persian labels, numeric/date/unit formatting, semantic progress/status announcements, keyboard navigation, and responsive layouts.
- [ ] Ensure status is not conveyed by color alone and stale/live-update state is accessible to screen readers.
- [ ] Enforce DataAdmin authorization for page, progress, report, issue, mapping, source-evidence, retry, cancel, and reprocess actions.
- [ ] Avoid logging raw workbook content, secrets, unrestricted cells, or full exception details in browser-visible responses.
- [ ] Define bounded polling, file-size, page-size, and timeline retention behavior.

## 7. Tests and Acceptance Scenarios

- [ ] Unit-test status/stage mapping, readiness capability mapping, next-action selection, duplicate-submit prevention, stale-update handling, and Persian formatting.
- [ ] Component-test upload validation, queued/running/partial/review/duplicate/corrected/failure/cancelled/unauthorized states.
- [ ] Integration-test upload `202` response, run/item progress, report detail, issue/review links, retry/cancel/reprocess, and readiness failures.
- [ ] End-to-end-test one accepted workbook from upload through analytics result and one partial/failed workbook through remediation guidance.
- [ ] Test browser reload/deep-link progress reconstruction and bounded polling cleanup.
- [ ] Regression-test the existing DataAdmin console, Feature 101 APIs, Feature 105 read APIs, and existing frontend routes.
- [ ] Given the optional configured source is unavailable but manual storage/worker are ready, allow manual upload and explain the source warning as informational.
- [ ] Given raw storage or Worker readiness is unavailable, show the exact blocked stage and next action instead of a silent queue.
- [ ] Given a corrected workbook, show a new revision and preserve the earlier report/result as immutable evidence.

## Completion Gate

- [ ] Keep tasks unchecked until dedicated upload/progress/recovery UX, backend contracts, readiness diagnostics, RTL accessibility, and accepted/partial/failed end-to-end scenarios pass.
