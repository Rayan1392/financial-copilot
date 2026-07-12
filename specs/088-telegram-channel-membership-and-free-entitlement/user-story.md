# User Story — Telegram Channel Membership and Free Entitlement

## Status
`[ ]` Proposed

## Feature
Require membership in the configured Telegram channel for free bot access and grant five daily free AI credits.

## Story

As a TahlilApp-AI user,

I want to receive a limited free allowance when I am a member of the required channel,

so that I can try the bot at no cost while the product can grow its Telegram audience.

## Business Context

The free tier is a Billing entitlement. Membership verification only gates eligibility; it must not mutate balances outside Billing.

## Dependencies

- Feature 087 and Features 010/013.

## In Scope

- Channel membership verification through Telegram Bot API.
- Configurable required channel.
- Five daily free credits through existing Billing/UsageLedger boundaries.
- Graceful handling when Telegram cannot verify membership.
- Periodic re-verification at entitlement refresh boundaries.

## Out of Scope

- A new Telegram-specific wallet.
- Paid subscription purchase.
- Per-message membership checks.
- Manual credit mutation outside Billing.

## Acceptance Criteria

1. Channel membership verification through Telegram Bot API.
2. Configurable required channel.
3. Five daily free credits through existing Billing/UsageLedger boundaries.
4. Graceful handling when Telegram cannot verify membership.
5. Periodic re-verification at entitlement refresh boundaries.
6. All user-specific data is isolated by canonical actor and tenant context where applicable.
7. All responses and notifications expose source freshness and evidence when financial facts are shown.
8. Failure of this feature must not silently consume credits or create duplicate ledger entries.
9. The capability is protected by explicit plan/entitlement checks and auditable authorization.

## API / Integration Proposal

```text
POST /api/v1/telegram/membership/verify
GET /api/v1/telegram/entitlement/me
```

## Data Model Proposal

```csharp
ChannelMembershipVerification { ActorId; TelegramUserId; ChannelId; Status; VerifiedAtUtc; ExpiresAtUtc; FailureReason?; }
```

## Security, Billing, and Compliance Rules

- Reuse ASP.NET Core Identity actor context and existing authorization policies.
- Reuse Billing reservation/finalization and immutable UsageLedger semantics.
- Never place secrets, bot tokens, payment credentials, or provider credentials in source-controlled configuration.
- Treat Telegram usernames as display metadata, never as stable identity.
- Avoid financial-advice wording; state that outputs are informational and evidence-based.
