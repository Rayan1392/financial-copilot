## Feature Folder

`162-context-aware-financial-report-follow-up/changes/price-all-report-discussions-at-two-credits`

## Layers / Area

Layers: backend
Area: AI · Conversations · Billing

## Priority

P1

## Complexity

M

## Depends On

162-context-aware-financial-report-follow-up

## References

- `specs/162-context-aware-financial-report-follow-up/design-proposal.md`
- `specs/162-context-aware-financial-report-follow-up/user-story.md`
- `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`
- `src/backend/FinancialCopilot.Application/AI/ReportDiscussion/ReportDiscussionHandler.cs`
- `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs`
- `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Functions/BillingFunctions.cs`
- `src/backend/FinancialCopilot.Infrastructure/Billing/AiFacadeBillingHook.cs`
- `src/backend/FinancialCopilot.Infrastructure/Billing/DefaultPricingPolicy.cs`

## User Story

As a user discussing a financial report already shown in the conversation,
I want every grounded answer about that report to cost two credits,
so that the charge is consistent across monthly sales, income statements, and balance sheets.

## Business Context

The approved Feature 162 design set a two-credit price only for cash-flow discussions and one credit for other report families. This change makes the answered-discussion price uniform across supported report families.

## Scope

- Charge two credits for a successful Report Discussion answer across supported report families.
- Reserve two credits before the Report Discussion model call.
- Preserve zero-charge outcomes and normal-route billing for hand-backs.
- Keep the existing operation code, subscription capabilities, and public contracts.

## Out Of Scope

- Enabling Report Discussion in any environment.
- Adding cash-flow discussion extraction or changing Feature 161's dependency status.
- Changing the price of normal AI queries, scanner operations, or other billing operations.
- Database migrations, subscription-plan changes, API changes, release, or deployment.

## Functional Requirements

1. An answered report discussion uses `ReportDiscussionAnswered` and costs two credits under the operation-based pricing policy.
2. The workflow reserves two credits before invoking the Report Discussion model. If capacity cannot be reserved, it returns the existing insufficient-credit outcome without invoking the model.
3. `NoData`, provider failure, timeout, validation failure, and other existing zero-charge outcomes remain zero credits.
4. A hand-back does not add a Report Discussion charge; the normal route determines the turn's charge and releases unused reserved capacity.
5. Usage-based Enforced billing continues to charge its measured proposal under its existing policy instead of applying the fixed two-credit price.
6. V1 and feature-flag-off routing remain unchanged.

## Data / Schema

No schema changes or migrations.

## API

No public API change.

## Acceptance Criteria

- [ ] AC-1: An answered discussion after a monthly-sales report charges exactly two credits.
- [ ] AC-2: An answered discussion after a quarterly income-statement report charges exactly two credits.
- [ ] AC-3: An answered discussion after a balance-sheet report charges exactly two credits.
- [ ] AC-4: With only one available credit, a report discussion does not call the model and returns the existing insufficient-credit outcome.
- [ ] AC-5: Missing-in-report, narrow-request, unverified, provider-failure, and timeout outcomes charge zero credits.
- [ ] AC-6: A hand-back is charged only by the normal route; unused held capacity is released.
- [ ] AC-7: A retry using the same billing operation identity does not create a second charge.
- [ ] AC-8: V1 routing and feature-flag-off behavior are unchanged.
- [ ] AC-9: The operation code, plan entitlements, API contract, and database schema are unchanged.

## Assumptions

- “Sales” means the supported `MonthlySales` Report Discussion family.
- The existing one-credit `AiQuery.Scanner` operation remains the pricing base; the Report Discussion completion multiplier sets the fixed successful charge to two.
- Feature 161's cash-flow Report Discussion dependency remains deferred until that feature meets its definition of done.
