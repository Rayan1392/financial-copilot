# Tasks

- Implement the Phase 1 subset of ledger, wallet projection, usage reservation, and plan/quota persistence defined by `013-billing-and-credits-domain`; do not create a separate usage-credit schema.
- Implement billable account resolution and ledger attribution for organization API clients, optional partner external-user references, and direct registered users.
- Define `IUsageChargeCalculator`, `IUsageAccountingService`, versioned charge-policy inputs, and usage result DTOs.
- Implement usage metering, quota/reservation, debit finalization, and failure/cancellation charging policy.
- Add Microsoft Agent Framework workflow function/executor adapters for pre-execution entitlement checks and post-execution usage finalization.
- Add function invocation middleware telemetry for routed tool execution without making middleware the billing source of truth.
- Add operation-based Usage Accounting output including charged credits, remaining balance, policy version, and cache status to the `POST /api/ai/v1/query` response.
- Add unit tests for credit charging policy and integration tests proving the displayed usage values are backend-calculated and recorded once per facade execution.
