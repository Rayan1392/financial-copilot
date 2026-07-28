# Tasks

## Module Foundation

- Add a `FinancialCopilot.Billing` bounded context/module to the backend solution and enforce dependency boundaries with architecture tests.
- Define billing aggregate roots, value objects, statuses, and repository interfaces for `CustomerAccount`, `Wallet`, `UsageLedger`, `SubscriptionPlan`, `CreditLine`, `InvoiceAccount`, and `UsageReservation`.
- Define billing configuration and authorization policies for organization and individual account administration.

## Customer and Tenant Billing

- Implement `IBillableAccountResolver` to map authenticated actors/API clients to the billed customer account and tenant.
- Implement organization-account support for prepaid, postpaid, and hybrid billing modes.
- Implement `ICreditLinePolicyService` with approved limits, warning thresholds, hard-stop enforcement, and no unlimited overdraft.
- Support partner-scoped `externalUserId` attribution for analytics, rate limits, reports, and optional sub-quotas without creating partner-user wallets.
- Implement individual-account policy with included allowances/top-ups and no overdraft by default.

## Ledger, Wallet, and Reservations

- Persist an immutable usage ledger and financial transaction ledger as accounting sources of truth.
- Implement a materialized wallet snapshot/read model that can be rebuilt from ledger entries.
- Define `ICreditReservationService` for reserve, commit, release, expiry, retry, refund, and reconciliation workflows.
- Implement idempotency and concurrency protection for query retries, provider retries, duplicate callbacks, and simultaneous requests.
- Use Redis only for fast reservation coordination/distributed locking where needed; persist authoritative reservation and ledger outcomes in PostgreSQL.

## Pricing and Usage Accounting

- Define `UsageUnit`, `ComputeCost`, `OperationCost`, `ProviderCost`, and versioned `PricingPolicy` models.
- Implement `IPricingPolicyProvider`, `IUsageChargeCalculator`, and `IUsageAccountingService`.
- Map normalized AI-provider usage facts from `014-ai-model-provider-abstraction` into provider/operation cost policy inputs without adding vendor dependencies to Billing.
- Add operation-based pricing tests for scanner, cached response, comparison, deep research, Codal analysis, summarization, and configurable future operation types.
- Define charging policies for successful, cached, failed, timed-out, cancelled, partially completed, and clarification-required executions.
- Include `creditsCharged`, `remainingBalance`, `pricingPolicyVersion`, and `cached` usage metadata in the AI facade response contract.

## AI Workflow Integration

- Add Microsoft Agent Framework workflow executor/function adapters for entitlement validation, reservation, actual-cost calculation, usage commit/release, and response metadata mapping.
- Ensure workflow ordering is mandatory around every billable AI operation and cannot be skipped by agent/model output.
- Add telemetry and audit correlation from `Conversation`, `Message`, agent tool execution, usage reservation, ledger transaction, and invoice/report entry.

## SaaS Organization Capabilities

- Implement `IPartnerAccountService` for organization billing profiles and charging modes.
- Implement `IApiUsageReportService` for tenant, API client, operation, and external-user attribution reports.
- Implement invoice-account contracts and `IInvoiceService` interface for postpaid/hybrid settlement.
- Add admin endpoints/contracts for organization balance, credit line, usage reporting, invoices, and manual adjustments.

## Direct Consumer Capabilities

- Implement `ISubscriptionService`, `ITopUpService`, and `IPaymentReconciliationService` interfaces.
- Define `IPaymentGatewayService` abstraction and webhook/callback idempotency contract.
- Add consumer endpoints/contracts for balance and usage history in the MVP slice; implement plans, subscription changes, top-up initiation, and payment status when direct payment delivery is scheduled.

## Verification

- Add unit tests for spending capacity, credit-line hard stops, individual no-overdraft, pricing policies, reservation transitions, refunds, and wallet projection rebuilding.
- Add integration tests for `POST /api/ai/v1/query` reservation/commit/release behavior for both organization and individual customers.
- Add integration tests for idempotent query retry, provider failure, cancellation, duplicate payment callback, tenant isolation, and partner external-user attribution.
- Add architecture tests preventing AI/Scanner orchestration from owning billing calculations or directly updating wallet balances.

## Implementation Status - 2026-05-26

Implemented in this story:

- Added the isolated `FinancialCopilot.Billing` module, dependency-boundary test coverage, customer/account/wallet/reservation/ledger/invoice/subscription models, repositories, and tenant-scoped authorization surfaces.
- Implemented billable account resolution for web users and API clients, organization prepaid/postpaid/hybrid rules, individual prepaid/no-overdraft rules, external-user attribution, and credit-line capacity assessment with warning threshold and hard stop.
- Implemented PostgreSQL/EF Core usage and financial ledgers, wallet projection handling, atomic reservation holds and finalization, abandoned-reservation expiry, idempotent currency accounting, manual credit adjustments, linked usage refunds, transactional outbox events, and processing diagnostics.
- Implemented versioned operation pricing primitives/services and configured outcomes for success, cached work, zero-charge failures/clarification, cancellation/timeout before execution, and partial completion.
- Implemented organization/self-service usage and transaction reads plus billing-admin wallet, usage, invoice-profile, adjustment, and refund APIs.
- Implemented consumer and settlement boundary contracts (`ISubscriptionService`, `ITopUpService`, `IPaymentGatewayService`, and `IPaymentReconciliationService`) and ledger/read support required by the foundation scope.
- Added scheduled worker maintenance for abandoned reservation expiry and optional outbox dispatch. Outbox events remain pending unless an external `IBillingOutboxDispatcher` transport is registered.
- Added unit and integration verification for Billing domain policies, pricing, reservation/finalization/refund/idempotency behavior, persistence/outbox handling, endpoint authorization/reporting, tenant boundaries, external-user attribution, and scheduled-maintenance coordination.

Explicitly deferred to dependent delivery stories or external integration scope:

- `010-usage-metering-and-billing-readiness`: invoke entitlement/reservation/finalization around `POST /api/ai/v1/query` and return final AI-facade usage metadata.
- `014-ai-model-provider-abstraction`: provide normalized provider execution facts consumed by Billing policy inputs.
- `018-ai-observability-and-telemetry`: correlate conversations, tool/model executions, reservations, ledgers, and reports in telemetry.
- Direct payment-gateway execution, webhook adapters, subscription-change/top-up initiation APIs, automated invoicing/settlement, and an external outbox transport/scale-out claiming strategy remain later commercial/operations increments as permitted by this story scope.
