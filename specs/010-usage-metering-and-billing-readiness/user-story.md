# User Story — Usage Metering and Billing Readiness

## Story

As a product owner,  
I want usage metering from Phase 1,  
so that SaaS/API and owned web app monetization can be introduced without redesign.

## Acceptance Criteria

- Each `POST /api/ai/v1/query` execution records a Usage Accounting ledger entry.
- Usage can be attributed to user or API client.
- Usage contains operation type, cost units/credits, timestamp, and status.
- Failed validation should cost zero or reduced credits based on policy.
- A successful AI query charges configured credits based on the routed tool/use case.
- API can return quota remaining.
- Rate limits can be enforced by subscription/API plan.

## Technical Notes

- Do not block MVP on payment gateway integration.
- Implement ledger and quota model first.
- Store routed operation type such as `AiQuery.Scanner` so charging remains auditable without exposing tool-specific chat routes.
