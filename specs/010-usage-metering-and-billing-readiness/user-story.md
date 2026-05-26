# User Story — Usage Metering and Billing Readiness

## Story

As a product owner,  
I want usage metering from Phase 1,  
so that SaaS/API and owned web app monetization can be introduced without redesign.

## Acceptance Criteria

- Each `POST /api/ai/v1/query` execution is accounted for by the `FinancialCopilot.Billing` ledger, including zero-charge policy outcomes where applicable.
- Usage records identify the actor/API client that initiated work and the billed `CustomerAccount`; for SaaS organization requests these may be different parties.
- Usage contains operation type, cost units/credits, timestamp, and status.
- Failed validation should cost zero or reduced credits based on policy.
- A successful AI query charges configured credits based on the routed tool/use case.
- API can return remaining balance or available allowance where permitted by account type and policy.
- Rate limits can be enforced by subscription/API plan.
- Credits consumed and remaining quota shown beside an assistant Message are calculated by backend services and returned from `POST /api/ai/v1/query`; the React UI and AI model never calculate or alter them.
- An `IUsageChargeCalculator` applies versioned charging policy from the authenticated actor, plan/quota configuration, routed operation, completion status, and billable execution facts.
- Usage recording is enforced for each facade execution even when an agent tool fails, is cancelled, or requests clarification, with zero, partial, committed, or released outcomes recorded according to policy.
- Phase 1 metering uses the `FinancialCopilot.Billing` bounded context specified in `013-billing-and-credits-domain`; it must not establish a separate competing credit model.
- Phase 1 supports resolving both an organization customer billed for SaaS API calls and an individual customer billed for owned-product use.
- `UsageLedger` is immutable accounting truth and `Wallet` is only a materialized balance snapshot.
- Every potentially billable operation creates or resolves a `UsageReservation` before expensive workflow execution and commits or releases it once the operation outcome is known.
- Pricing is operation-based and may include fractional displayed credits for cached or lower-cost work; the design does not assume `1 query = 1 credit`.

## Technical Notes

- Do not block MVP on payment gateway integration.
- Implement ledger and quota model first.
- Store routed operation type such as `AiQuery.Scanner` so charging remains auditable without exposing tool-specific chat routes.
- Implement usage charging behind SOLID Application-layer abstractions; agent-framework integration must depend on interfaces rather than billing persistence details.
- In the Microsoft Agent Framework orchestration, entitlement checking/reservation and usage finalization are mandatory workflow functions/executors around routed tool execution. They must not depend on the LLM deciding to request a billing tool.
- Tool/function middleware should record tool invocation telemetry and correlation data, while `IUsageAccountingService` remains the source of truth for debiting credits and persisting ledger entries.
- Defer payment gateway and invoice automation if necessary, while preserving interfaces and ledger structures required by `013-billing-and-credits-domain`.
- This story delivers only facade metering/reservation integration required by the scanner MVP; organization settlement, direct payment flows, and reporting beyond query usage are owned by `013`.
