# User Story — Usage Metering and Billing Readiness

## Story

As a product owner,  
I want usage metering from Phase 1,  
so that SaaS/API and owned web app monetization can be introduced without redesign.

## Acceptance Criteria

- Each scanner request records usage ledger entry.
- Usage can be attributed to user or API client.
- Usage contains operation type, cost units/credits, timestamp, and status.
- Failed validation should cost zero or reduced credits based on policy.
- Successful scanner query charges configured credits.
- API can return quota remaining.
- Rate limits can be enforced by subscription/API plan.

## Technical Notes

- Do not block MVP on payment gateway integration.
- Implement ledger and quota model first.
