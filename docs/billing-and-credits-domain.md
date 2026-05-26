# Billing and Credits Domain

## Decision

Create a dedicated bounded context/module:

```text
FinancialCopilot.Billing
```

This is initially part of the modular monolith and not a separately deployed microservice. Its domain model, interfaces, ledger, reservation flow, and pricing engine are isolated from AI orchestration. The module can be extracted later without redesigning charging contracts.

## Why Billing Is Separate

AI execution has variable cost, retries, provider failures, partial work, refunds, disputes, invoice requirements, and multiple customer models. An AI orchestrator may request billable work, but it must not own balance mutation or pricing calculations.

## Unified Customer Model

| Concept | Responsibility |
| --- | --- |
| `CustomerAccount` | Billable legal or individual customer responsible for payment. |
| `Actor` | Authenticated caller performing a request. |
| `Tenant` | Logical isolation boundary for policies, credentials, data, and reporting. |
| `Wallet` | Materialized available-balance snapshot for reads. |
| `UsageLedger` | Immutable source of accounting truth for AI usage. |
| `SubscriptionPlan` | Entitlements, included usage, limits, and presentation rules. |
| `CreditLine` | Explicit approved overdraft exposure for eligible organizations. |
| `InvoiceAccount` | Settlement profile, invoice cycle, taxation, and payment terms. |
| `UsageReservation` | Temporary authorized capacity held before AI execution. |

```text
Actor executes work
-> CustomerAccount is resolved
-> CustomerAccount is charged
```

## Accounting Principle

```text
Wallet balance is not the source of truth.
Immutable UsageLedger and financial transaction ledger are the source of truth.
```

Wallet state is a materialized projection used for performant entitlement and balance reads. It must be rebuildable from ledger records. Every reservation, commit, release, refund, adjustment, invoice attribution, and payment reconciliation is idempotent and auditable.

Manual usage-credit adjustments are administrator-authorized, tenant-scoped ledger entries with required audit reason and idempotency key. They are separate from currency-denominated payment or invoice-settlement transactions; currency is never converted into credits without an explicit future settlement policy.

## Customer Types

### SaaS Organization

Examples include TahlilAPP, financial platforms, brokerages, and investment firms.

- The organization is billed; its end users are not FinancialCopilot wallet holders by default.
- Authentication uses isolated API keys or OAuth client credentials.
- Usage is metered per tenant, API client, operation, and optional partner-scoped `externalUserId`.
- `externalUserId` enables analytics, abuse detection, partner reports, rate limiting, and optional sub-quotas without debiting an end-user wallet.

Supported billing modes:

| Mode | Behavior |
| --- | --- |
| `Prepaid` | Usage spends prepaid available capacity. |
| `Postpaid` | Usage is accumulated for invoicing under approved payment terms. |
| `Hybrid` | Prepaid capacity is used first, followed by an explicitly approved credit line. |

Recommended initial organization policy:

```text
Prepaid Wallet + Approved Credit Line

Available Spending Capacity =
Wallet Balance + Credit Line - Reserved Amount
```

Unlimited negative balance is prohibited. A configured warning threshold and hard stop apply when approved spending capacity is exhausted.

### Direct Consumer

Direct consumers are users registered through the FinancialCopilot web or mobile product.

- FinancialCopilot manages subscription, wallet, top-up, payment gateway, usage history, and credit consumption.
- Included plan allowances or prepaid wallet balance authorize execution.
- Default policy is no overdraft: no available allowance means no billable execution.

## Pricing Model

Do not use a permanent `1 query = 1 credit` rule. Use operation-based pricing through versioned policy.

| Example operation | Illustrative displayed credits |
| --- | ---: |
| Simple scanner query | 1 |
| Cached response | 0.2 |
| Financial comparison | 3 |
| Deep research | 15 |
| Codal analysis | 8 |
| AI summarization | 4 |

These numbers are configuration examples, not fixed domain invariants.

Internal pricing abstractions:

```text
UsageUnit
ComputeCost
OperationCost
ProviderCost
PricingPolicy
```

The backend can account for different LLM providers, local models, caching, embeddings, retrieval/vector search, and background AI work while exposing understandable credits and remaining balance to users.

AI model integrations are defined by `014-ai-model-provider-abstraction`. Billing consumes normalized execution facts from cloud adapters such as OpenAI or Anthropic/Claude, a future contract-defined Abravran adapter, or local runtimes such as Ollama; Billing does not reference their SDKs or assume equivalent provider-reported cost data.

## Reservation-Based Charging

```text
Authenticate Actor
-> Resolve CustomerAccount and Tenant
-> Resolve Pricing Policy and Entitlements
-> Estimate and Reserve Spending Capacity
-> Execute Microsoft Agent Framework Workflow
-> Calculate Actual Billable Usage
-> Commit or Release UsageReservation
-> Append UsageLedger Entry and Update Wallet Projection
-> Return Usage Metadata
```

Execution outcomes must have defined policies:

| Outcome | Required treatment |
| --- | --- |
| Successful operation | Commit actual usage and release unused reservation. |
| Cached operation | Commit policy-defined cached cost. |
| Validation/clarification before billable work | Release reservation and apply configured zero/minimal charge. |
| Provider/backend failure | Release reservation; do not charge unless contract explicitly permits a recorded partial charge. |
| Timeout or cancellation | Apply explicit completion/partial-work policy and record outcome. |
| Retry or duplicate request | Enforce idempotency; do not double charge. |

The persistence implementation must apply a reservation hold and wallet-reserved amount together. Successful finalization must apply reservation commit status, usage-ledger charge, and wallet debit in one persistence operation. Failure finalization must apply reservation release reason and wallet reserved-capacity release in one persistence operation, without a charge ledger row for zero-charge failure policy. These contracts are exposed through deterministic `ICreditReservationService` and `IUsageFinalizationService` boundaries for later workflow invocation.

## AI Orchestration Boundary

`POST /api/ai/v1/query` remains the only frontend chat-query endpoint. Microsoft Agent Framework coordinates AI capabilities, but Billing controls charges through deterministic Application services.

```text
AI Query Facade
-> Billing Entitlement / Reservation Workflow Executor
-> Intent Detection and Routed AI Tool Workflow
-> Actual-Cost Calculation Workflow Executor
-> Billing Commit / Release Workflow Executor
-> Assistant Message + Usage Metadata Response
```

Recommended interfaces:

```csharp
IBillableAccountResolver
IWalletService
IUsageAccountingService
ICreditReservationService
IUsageChargeCalculator
IPricingPolicyProvider
IInvoiceService
IEntitlementService
IPaymentGatewayService
IBillingReportService
IPartnerAccountService
IApiUsageReportService
ICreditLinePolicyService
ISubscriptionService
ITopUpService
IPaymentReconciliationService
```

The LLM cannot reserve credits, calculate charges, debit balances, approve overdrafts, issue refunds, or alter returned usage metadata.

## Response Metadata

The facade returns billing data calculated by the Billing module:

```json
{
  "creditsCharged": 2.4,
  "remainingBalance": 183.5,
  "pricingPolicyVersion": "v1",
  "cached": false
}
```

Confidence score is calculated by the explainability policy, not Billing, but may appear alongside usage metadata in the assistant response.

## Infrastructure Guidance

- PostgreSQL persists immutable ledgers, reservations, wallet projections, accounts, plans, credit lines, invoice/payment records, and audit linkage.
- Redis supports short-lived reservation coordination, distributed locks, rate limits, query cache, AI session/stream state, and conversation-context cache. Redis is not the accounting source of truth.
- RabbitMQ supports settlement processing, invoicing, reconciliation, data ingestion, metric recalculation, Codal parsing, summarization, cache warming, and other asynchronous work.
- PostgreSQL plus appropriate indexing is sufficient for Phase 1 scanner data and billing records.
- Add Elasticsearch and vector storage later for large-scale textual/research retrieval, Persian full-text requirements, semantic or hybrid search, not for core billing correctness.
