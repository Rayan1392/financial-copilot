# User Story — Telegram Subscriptions, Credit Purchases, and Entitlements

## Status
`[ ]` Proposed

## Feature
Allow Telegram users to buy plans or credits while keeping Billing as the single source of truth.

## Story

As a TahlilApp-AI user,

I want to buy a plan or additional AI credits from the bot,

so that I can continue using premium features without leaving the Telegram journey unnecessarily.

## Business Context

Billing remains authoritative. Telegram only creates checkout intent, shows status, and receives entitlement updates.

## Dependencies

- Features 013
- 035
- 087-089.

## In Scope

- Plan/catalog presentation.
- Credit pack and subscription checkout intents.
- Payment gateway callback and idempotent fulfillment.
- Optional MVP receipt-review workflow with unique payment reference and admin audit.
- Entitlement propagation to bot capabilities.
- Invoice, reconciliation, refund, and failed-payment states.

## Out of Scope

- Direct card data collection in Telegram.
- Manual balance edits outside Billing.
- A second subscription ledger.

## Acceptance Criteria

1. Plan/catalog presentation.
2. Credit pack and subscription checkout intents.
3. Payment gateway callback and idempotent fulfillment.
4. Optional MVP receipt-review workflow with unique payment reference and admin audit.
5. Entitlement propagation to bot capabilities.
6. Invoice, reconciliation, refund, and failed-payment states.
7. All user-specific data is isolated by canonical actor and tenant context where applicable.
8. All responses and notifications expose source freshness and evidence when financial facts are shown.
9. Failure of this feature must not silently consume credits or create duplicate ledger entries.
10. The capability is protected by explicit plan/entitlement checks and auditable authorization.

## API / Integration Proposal

```text
GET /api/v1/billing/catalog
POST /api/v1/billing/checkouts
POST /api/v1/billing/payment-callback/{provider}
POST /api/v1/admin/billing/receipt-reviews (optional MVP)
```

## Data Model Proposal

```csharp
CheckoutIntent { Id; ActorId; ProductType; ProductCode; Amount; Currency; PaymentReference; Status; ExpiresAtUtc; FulfilledAtUtc?; }
```

## Security, Billing, and Compliance Rules

- Reuse ASP.NET Core Identity actor context and existing authorization policies.
- Reuse Billing reservation/finalization and immutable UsageLedger semantics.
- Never place secrets, bot tokens, payment credentials, or provider credentials in source-controlled configuration.
- Treat Telegram usernames as display metadata, never as stable identity.
- Avoid financial-advice wording; state that outputs are informational and evidence-based.
