# User Story — Fund Portfolio Workbook Upload and Processing Experience

## Status

`[ ]` Planned — specification only; implementation is intentionally deferred.

## Feature

Provide a dedicated DataAdmin experience for uploading investment-fund portfolio Excel workbooks, following asynchronous processing, reviewing every processing stage, and navigating to the resulting report, issues, mappings, and analytics.

## Problem Statement

The current route `/admin/data-management/fund-reports` is a general data-management surface. It accepts an upload and displays a short queue message, but it does not provide a dedicated workflow, continuous progress, stage-level status, failure guidance, or a clear next action. The current source warning can also be misunderstood: an unavailable configured local/object-storage source must not be presented as if it blocked a manual browser upload unless manual upload is actually unavailable.

The implementation plan must also audit the upload handoff across processes. The current manual source keeps uploaded bytes in an in-memory process-local collection, while import processing is hosted by the Worker process. A queued manual descriptor must therefore be converted to a durable, worker-readable object before the API returns `202`; otherwise the run can remain queued or fail with a source-object-not-found error after the browser has already been told that it was queued. This is a backend reliability requirement, not a frontend workaround.

This feature is an operational UX and contract-alignment feature. It must preserve the existing asynchronous import model and must not calculate portfolio analytics in the frontend.

## Goals

- Give the administrator one focused page for manual `.xlsx` upload and processing supervision.
- Explain the complete lifecycle from browser upload through parsing, normalization, analytics recalculation, and final review.
- Make every known state visible: queued, running, partial, needs review, duplicate, corrected revision, retryable failure, poison/final failure, cancelled, and completed.
- Always show the current step, completed steps, blocked step, last update, correlation/run identifiers, and next action.
- Make source-storage and worker readiness actionable instead of displaying a generic warning.
- Make the queued-file handoff durable across API/Worker process boundaries before acknowledging the upload.
- Preserve immutable source revisions, evidence, confidence, delayed-data semantics, and non-recommendatory wording from Features 100–110.

## Dependencies and Related Features

- Feature 100: workbook validation, safe parsing, source evidence, and report lifecycle.
- Feature 101: manual upload API, import runs/items, worker processing, issue/review queue, retry, cancellation, and reprocessing.
- Features 102–104: normalized holdings, non-equity/derivative, income, and valuation-quality stages.
- Feature 105: comparable-period analytics, signals, read APIs, and recalculation orchestration.
- Features 106–108: consensus, conviction quality, AI context, and explainability when those outputs are available.
- Feature 109: shared RTL design system, fund report/detail surfaces, evidence presentation, and accessibility conventions.
- Feature 110: future event/notification handoff; this feature must not create a second notification system.
- Existing Features 055 and 058: DataAdmin authorization, admin API bridge, and live-operation monitoring patterns.

## Proposed Routes

```text
/admin/fund-portfolio/upload                 Dedicated upload and live processing page
/admin/fund-portfolio/runs/:runId            Shareable run/progress detail page
/admin/fund-portfolio/reports/:reportId     Report result, issues, mappings, and analytics handoff
```

The existing `/admin/data-management/fund-reports` route should either redirect to the dedicated upload page or become a clearly labeled entry point with a prominent link. It must not remain the only upload experience.

## User Journey

1. The DataAdmin opens the dedicated page and sees readiness cards for manual upload, raw-file storage, queue/worker processing, database, and optional configured discovery source.
2. The administrator selects one `.xlsx` file. The page validates extension, size, MIME, and client-side obvious errors without opening or recalculating the workbook.
3. The page shows a pre-submit summary: file name, size, optional fund-name hint, upload provider `ManualUpload`, and the fact that processing is asynchronous and may require review.
4. After submission, the API returns `202 Accepted` with run id, item id/count, correlation id, and initial `Queued` status. The page immediately changes to the run timeline; it must not treat `202` as completion.
5. The page polls a bounded run-progress contract, or subscribes to a supported live update channel, and updates the current step without requiring a manual refresh.
6. When parsing or normalization produces issues, the page shows the affected stage, issue severity/count, report id if available, and an action such as `مشاهده خطاها`, `بررسی نگاشت`, `تلاش مجدد`, or `پردازش مجدد`.
7. When analytics completes, the page links to the persisted report/intelligence result and clearly labels coverage, confidence, reconciliation warnings, source revision, import freshness, and comparable period.
8. On duplicate or corrected revision, the page explains the outcome and links to the existing or newly created immutable report revision.
9. On a terminal failure, the page keeps the run evidence and presents the reason, retry eligibility, and exact next action; it must never leave the user with only a silent or generic toast.

## Processing Flow Shown to the User

The timeline should use these logical stages. A stage is `در انتظار`, `در حال انجام`, `انجام شد`, `هشدار/ناقص`, `متوقف شد`, or `ناموفق` and includes a machine-readable code, timestamp, and bounded message.

```text
1. دریافت فایل در مرورگر
2. اعتبارسنجی اولیه و محدودیت‌های فایل
3. ذخیره امن فایل خام و محاسبه checksum
4. ایجاد Import Run و قرار دادن Item در صف
5. دریافت توسط Worker / lease
6. خواندن امن workbook و inventory شیت‌ها
7. استخراج دوره، صندوق، ساختار شیت و source evidence
8. نرمال‌سازی holdings و activity (Feature 102)
9. نرمال‌سازی دارایی‌های غیرسهامی و مشتقه (Feature 103)
10. نرمال‌سازی درآمد، تعدیلات و کیفیت NAV (Feature 104)
11. تطبیق‌ها، reconciliation و ایجاد review items
12. انتخاب دوره مشابه و اجرای analytics/strategy signals (Feature 105)
13. آماده‌سازی خروجی‌های consensus/quality/AI در صورت وجود داده کافی (Features 106–108)
14. ثبت وضعیت نهایی، freshness، confidence، evidence و لینک نتیجه
```

The UI may collapse stages that are not registered or not applicable, but it must show why a stage is skipped or unavailable. It must not claim that analytics or later features completed merely because the workbook parser completed.

## Readiness and Failure Semantics

The page must distinguish these capabilities:

| Capability | Meaning | User-facing behavior |
|---|---|---|
| Manual browser upload | `POST /api/v1/admin/fund-portfolio-reports/uploads` can accept the file | Allow upload when true |
| Raw workbook storage | Uploaded bytes can be retained immutably | Block submission with configuration action when false |
| Import worker | Queued items can be claimed and processed | Allow queueing only if policy permits, but show `در صف باقی می‌ماند تا Worker فعال شود` and an operational action |
| Database/migrations | Run and report state can be persisted | Block submission and show administrator remediation |
| Configured discovery source | Optional local/object-storage discovery is available | Informational badge; never imply manual upload is blocked unless the API says so |
| Analytics dependencies | Normalized sections and market inputs are available | Show partial/unavailable analytics with reason and confidence; do not fabricate values |

For manual uploads, the API must persist the bytes (or an equivalent durable upload object) before creating the queued item. The queued descriptor must contain a durable source object id/download token that the Worker can resolve after restart or when running in a separate process. An in-memory registration may remain only as a local test adapter and must not be the production queue handoff.

The backend should expose or extend a readiness/health contract so the frontend does not infer readiness from the optional source-status warning. Readiness errors must include a stable code, safe summary, severity, affected capability, and recommended operator action.

## Frontend Requirements

- Dedicated Persian RTL responsive page with a clear title such as `آپلود و پردازش گزارش پرتفوی صندوق`.
- Upload card with file picker, drag/drop, file constraints, optional governed fund hint, checksum/size preview, and submit/cancel states.
- Persistent run header showing run id, file/item name, started/last-updated/completed times, correlation id, overall status, attempt number, and current action.
- Vertical or horizontal stepper for the full flow, with accessible labels and status icons that are not color-only.
- Progress summary with counts: queued/running/completed/partial/review/duplicate/failed and item-level rows for future bounded multi-file uploads.
- Polling/live-update indicator and stale-update warning when no server update is received within the configured threshold.
- Detailed error drawer/page using report issues and mapping-review APIs; never render unrestricted raw workbook contents.
- Terminal result cards linking to report detail, source evidence, analytics/intelligence, reprocess, retry, cancellation, or mapping review as authorized.
- Separate informational card for optional configured-source availability; wording must not block or confuse manual upload.
- Explicit delayed-disclosure wording for monthly fund reports and no buy/sell recommendation language.
- Skeleton, empty, partial, stale, unauthorized, validation-error, worker-unavailable, storage-unavailable, and server-failure states.
- Browser reload and deep-link support for `/admin/fund-portfolio/runs/:runId`.

## Backend Contract Plan

Reuse existing Feature 101 endpoints where sufficient and add a versioned progress/readiness contract where the current run view is not enough:

```http
GET  /api/v1/admin/fund-portfolio-reports/readiness
POST /api/v1/admin/fund-portfolio-reports/uploads
GET  /api/v1/admin/fund-portfolio-reports/runs/{runId}
GET  /api/v1/admin/fund-portfolio-reports/runs/{runId}/items
GET  /api/v1/admin/fund-portfolio-reports/runs/{runId}/timeline
GET  /api/v1/admin/fund-portfolio-reports/{reportId}/detail
GET  /api/v1/admin/fund-portfolio-reports/{reportId}/issues
POST /api/v1/admin/fund-portfolio-reports/{reportId}/reprocess
```

The progress response should include:

- overall run/item status and stable status codes;
- stage list with status, timestamps, duration, issue count, and safe message;
- report id, source revision, parser/calculation versions, and supersession relationship;
- normalized-section completion/coverage;
- analytics completion, confidence, and unavailable reasons;
- retry/cancel/reprocess permissions and next actions;
- last event/update timestamp and correlation id.

The backend must remain the authority for stage state, counts, calculations, confidence, and eligibility. The frontend only renders server contracts.

## Acceptance Criteria

1. An authorized DataAdmin can open a dedicated upload page without navigating through the general data-management dashboard.
2. A valid manual `.xlsx` submission returns a visible run/item id and transitions the user to a live progress view; a success toast alone is insufficient.
3. The page distinguishes browser upload, raw storage, queue/worker, optional configured source, and analytics readiness.
4. Every processing stage listed in the flow is represented as completed, active, skipped-with-reason, partial, blocked, or failed.
5. Refreshing or deep-linking to a run preserves and reconstructs the current progress state from the backend.
6. A queued item that cannot advance because the Worker or required storage is unavailable explains the reason and next operator action.
7. Parser, normalization, reconciliation, mapping, and analytics issues link to the correct report/issues/review surfaces.
8. Duplicate and corrected-revision outcomes are visibly different and link to the correct immutable report revision.
9. Partial or low-confidence analytics display unavailable reasons, coverage, reconciliation warnings, and confidence; no fabricated precision is shown.
10. At least one accepted report and one partial/failed report are demonstrable end-to-end in an admin acceptance fixture.
11. The page is Persian RTL, keyboard accessible, responsive, actor-authorized, and does not use recommendation language.
12. Existing Feature 101 upload/reprocess/cancel behavior and Features 105–110 backend contracts remain backward compatible.

## Out of Scope

- Changing workbook parsing rules or repairing source workbooks.
- Moving calculations into React or recomputing analytics in the browser.
- Implementing an unverified external source adapter.
- Public access to raw workbooks or unrestricted source cells.
- Real-time market/order processing.
- Notification delivery; Feature 110 owns that handoff.

## Completion Gate

- [ ] Keep tasks unchecked until the dedicated page, readiness diagnostics, asynchronous stage tracking, issue/review navigation, terminal recovery actions, RTL accessibility, and accepted/partial/failed end-to-end fixtures are verified.
