# Tasks: Price All Report Discussions at Two Credits

Status: Gate B: Approved by the owner on 2026-11-16

## Feature Folder

`162-context-aware-financial-report-follow-up/changes/price-all-report-discussions-at-two-credits`

## Before Starting

- [x] Gate A design approved by the owner on 2026-11-16.
- [x] Gate B implementation plan approved by the owner on 2026-11-16.
- [x] Existing Feature 162 code and billing policy inspected.
- [x] No database change or plan-entitlement change is required.

## Implementation Tasks

### 1. Billing capacity and price

- [x] Add a targeted reservation-capacity overload to `IBillingFacadeHook` and `BillingFunctions`; keep the existing usage-based reservation behavior in Enforced mode.
- [x] Make completion multiplier validation accept values above one while rejecting negative values; retain cached multiplier validation at 0–1.
- [x] Price `ReportDiscussionAnswered` at 2× the existing one-credit operation price.

### 2. V2 Report Discussion integration

- [x] Pass a targeted capacity callback to `ReportDiscussionHandler` and invoke it before the model call.
- [x] Retain the updated reservation handle for settlement; let insufficient-credit errors propagate without triggering the fail-open hand-back path.
- [x] Use `ReportDiscussionAnswered` for all successful answers; preserve zero-charge statuses and normal-route hand-back billing.

### 3. Specification alignment

- [x] Update Feature 162 D2, FR-9, AC-7, and T13 to specify two credits for every supported answered Report Discussion family.
- [x] Record this approved scope change in this change folder.

## Verification

- [ ] Add/run focused billing and V2 integration tests for two-credit pricing, pre-call reservation, insufficient-credit handling, zero-charge outcomes, and hand-back billing. Not run or added in this task under the active instruction not to add or run tests unless asked.
- [ ] Review the final diff for V1, schema, API, and feature-flag isolation.

## Definition Of Done

- [ ] Acceptance criteria in `user-story.md` verified.
- [ ] No database migration or plan-capability update.
- [ ] User-visible changelog entry is added only when the Report Discussion feature flag is enabled and the change becomes user-visible; current configuration remains disabled.
- [ ] No release, tag, deployment, or shared-environment flag change.
