# Design Proposal: Price All Report Discussions at Two Credits

Status: Gate A: Approved by the owner on 2026-11-16

## Context

Feature 162 currently specifies a two-credit answered discussion for cash-flow reports and a one-credit answered discussion for income-statement, balance-sheet, and monthly-sales reports. Its billing task T13 is still incomplete. The owner requested that all answered report discussions cost two credits.

## Scope

- Charge two credits for a grounded `Answered` Report Discussion reply for every supported report family, including quarterly income statement, balance sheet, and monthly sales.
- Reserve two credits before the Report Discussion model call. If the wallet cannot reserve two credits, return the existing insufficient-credit outcome without calling the model.
- Keep zero-credit outcomes at zero. A hand-back remains billed only by the normal route; the Report Discussion call remains absorbed.
- Keep the existing `AiQuery.Scanner` operation code, API contract, plan entitlements, and database schema.
- Apply this in the V2 Report Discussion path only. Do not change V1 or enable the feature flag.
- Do not add cash-flow report discussion support; Feature 161 remains its dependency for that family. When another report family is supported by Report Discussion, the shared answered status will apply the same two-credit price.

## Proposed Design

1. Give `IBillingFacadeHook` a targeted reservation-capacity overload. Legacy and Shadow policies reserve the requested two credits; Enforced usage-based billing retains its configured per-question reservation cap because actual usage replaces the fixed price in that mode.
2. Expose that operation through the V2 `BillingFunctions` adapter. The workflow passes a reservation callback into `ReportDiscussionHandler`; the handler invokes it immediately before the model call, after confirming the active report and prompt fit. The workflow retains the upgraded reservation handle for finalization.
3. Let an insufficient-credit exception escape Report Discussion's fail-open handler boundary. The workflow's existing billing error handling then returns the insufficient-credit response without starting the model call. Other unexpected Report Discussion failures continue to hand back to normal routing.
4. Set one `ReportDiscussionAnswered` completion status for successful answers from all report families and price that status at two times the existing one-credit `AiQuery.Scanner` operation. Permit non-negative completion multipliers above one while retaining the cached multiplier's existing 0–1 validation.
5. Keep no-data, provider-failure, timeout, clarification, and other existing zero-charge statuses unchanged. Keep hand-back finalization on the normal route's completion status.

## Data, API, and Operations

- No database migration or public API change.
- No subscription-plan capability or seed change.
- No configuration or feature-flag change.
- No release or deployment authorization is included.

## Verification Plan

- Verify the billing calculator returns two credits for `ReportDiscussionAnswered` and rejects negative completion multipliers.
- Verify reservation is upgraded to two before a model call, the insufficient-credit case skips the call, and finalization charges exactly two for answered discussions.
- Verify zero-credit outcomes, hand-backs, idempotent replay, V1 routing, and flag-off behavior remain unchanged.

## Risks and Trade-offs

- Conversations with an active supported report require capacity for two credits before the discussion model can determine whether to answer or hand back. This preserves the pre-call funding guarantee and matches the existing cash-flow policy.
- The feature flag remains off in the checked-in configuration, so this change does not make Report Discussion live by itself.
