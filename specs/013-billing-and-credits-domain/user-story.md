# User Story - Billing and Credits Domain

## Story

As the product owner,  
I want a dedicated billing and credits bounded context for organization partners and direct consumers,  
so that every AI operation is evaluated, reserved where applicable, accounted for, reconciled, and reported without coupling commercial accounting to AI orchestration.

## Acceptance Criteria

- A logical `FinancialCopilot.Billing` bounded context/module is isolated from AI orchestration, Scanner, and data-ingestion business logic.
- Billing is implemented as a module in the backend solution initially and is not deployed as an independent microservice for the MVP.
- A unified `CustomerAccount` model supports `Organization` customers such as TahlilAPP and `Individual` customers using the owned web/mobile product.
- `Actor` identifies the authenticated caller, while `CustomerAccount` identifies the billed entity. A partner end user may execute a query while the organization account is charged.
- Each SaaS organization has isolated tenant context, API credentials, entitlements, limits, pricing policy, analytics, and usage reporting.
- Organization accounts support `Prepaid`, `Postpaid`, and `Hybrid` charging modes.
- The recommended initial organization mode is prepaid balance plus an approved `CreditLine`; unlimited negative balances are not allowed.
- Spending capacity is evaluated as `Wallet Balance + Credit Line - Reserved Amount`.
- A SaaS request may include a stable `externalUserId` for analytics, abuse detection, rate limiting, reporting, and optional sub-quota enforcement; FinancialCopilot does not debit partner end-user wallets.
- Direct consumer accounts are designed to support subscriptions, included allowances, wallet top-ups, payment-gateway settlement, usage history, and credit consumption; the scanner MVP requires contracts and ledger support, while live gateway operation may be delivered later.
- Direct consumer execution is rejected when no applicable allowance or wallet balance is available; individual overdraft is not allowed by default.
- The immutable `UsageLedger` is the accounting source of truth. `Wallet` balance is a materialized read snapshot for fast availability checks only.
- The billing model includes `CustomerAccount`, `Wallet`, `UsageLedger`, `SubscriptionPlan`, `CreditLine`, `InvoiceAccount`, and `UsageReservation`.
- Charging is operation-based and policy-versioned. The design must not assume that every query costs one credit.
- Internal accounting supports `UsageUnit`, `ComputeCost`, `OperationCost`, `ProviderCost`, and `PricingPolicy`, while user-facing responses may display credits and remaining balance.
- Pricing policy can distinguish scanner queries, cached answers, financial comparisons, deep research, Codal analysis, summarization, embeddings, RAG/vector search, background AI jobs, and future model providers.
- Pricing policy consumes normalized provider execution facts supplied by `014-ai-model-provider-abstraction`, whether execution used a cloud model or a local runtime.
- Before billable AI work begins, the backend validates entitlement and creates a `UsageReservation`; after completion it commits actual usage and releases unused reservation.
- Provider failure, timeout, cancellation, retry, partial execution, and clarification flows use explicit idempotent release/charge/refund policy.
- `POST /api/ai/v1/query` returns backend-produced usage metadata including charged credits, remaining balance where permitted, pricing-policy version, and cache status.
- Payment, credit adjustments, refunds, invoice generation, and reconciliation are auditable through immutable financial/usage transactions.
- Billing services follow SOLID boundaries and remain independently testable without an LLM, Microsoft Agent Framework runtime, or payment gateway.
- Billing must not depend on any hosted/local model-provider SDK and must not assume all AI providers expose identical token or monetary cost metadata.
- Microsoft Agent Framework workflow executors invoke entitlement/reservation before routed AI execution and usage finalization afterward; the LLM cannot decide whether billing runs or alter charge values.
- `010-usage-metering-and-billing-readiness` implements the scanner MVP's facade-metering slice of this bounded context and must reuse these domain rules rather than duplicate them.

## Technical Notes

- Start as a modular monolith bounded context; extract into a separately deployed service only when operational or organizational requirements justify it.
- Use append-only ledger records with idempotency keys and transactional/outbox handling for charge, release, refund, invoice, and payment events.
- Keep currency/payment amounts distinct from internal usage units and displayed credits.
- Store external partner user identifiers as partner-scoped references, not FinancialCopilot consumer identities.
- Reserve credits with bounded expiry and recovery for abandoned executions; Redis may coordinate short-lived reservations, but PostgreSQL ledger records remain authoritative.
- Do not block the scanner MVP on full automated invoicing or a live banking gateway; define interfaces and implement ledger/reservation first.
- Payment gateway automation, invoice delivery automation, and extensive partner billing reporting are later delivery increments unless explicitly promoted into the MVP.
