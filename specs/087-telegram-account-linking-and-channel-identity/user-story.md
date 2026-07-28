# User Story — Telegram Account Linking and Channel Identity

## Status
`[x]` Implemented 2026-07-12

## Feature
Link a Telegram user to a canonical TahlilApp actor without creating a second identity or billing model.

## Story

As a TahlilApp-AI user,

I want to securely use my existing account in Telegram,

so that my identity, permissions, credits, and history remain consistent across web and Telegram.

## Business Context

Telegram is a delivery channel, not a separate product identity. Account linking must preserve the canonical actor and tenancy model.

## Dependencies

- Features 002
- 013
- 031
- and 035.

## In Scope

- Authenticated account-linking flow using a short-lived one-time token.
- Telegram user/chat identity persistence.
- Revocation and relinking.
- Actor and tenant isolation.
- Audit trail for link/unlink operations.

## Out of Scope

- Telegram-only shadow accounts with no canonical actor.
- Channel membership enforcement.
- Payments or subscription activation.
- Market alerts.

## Acceptance Criteria

1. Authenticated account-linking flow using a short-lived one-time token.
2. Telegram user/chat identity persistence.
3. Revocation and relinking.
4. Actor and tenant isolation.
5. Audit trail for link/unlink operations.
6. All user-specific data is isolated by canonical actor and tenant context where applicable.
7. All responses and notifications expose source freshness and evidence when financial facts are shown.
8. Failure of this feature must not silently consume credits or create duplicate ledger entries.
9. The capability is protected by explicit plan/entitlement checks and auditable authorization.

## API / Integration Proposal

```text
POST /api/v1/telegram/link-token
POST /api/v1/telegram/link/confirm
DELETE /api/v1/telegram/link/me
Telegram: /start <one-time-token>
```

## Data Model Proposal

```csharp
TelegramAccountLink { Id; ActorId; TenantId?; TelegramUserId; TelegramChatId; Username?; LinkedAtUtc; RevokedAtUtc?; LastVerifiedAtUtc; }
```

## Security, Billing, and Compliance Rules

- Reuse ASP.NET Core Identity actor context and existing authorization policies.
- Reuse Billing reservation/finalization and immutable UsageLedger semantics.
- Never place secrets, bot tokens, payment credentials, or provider credentials in source-controlled configuration.
- Treat Telegram usernames as display metadata, never as stable identity.
- Avoid financial-advice wording; state that outputs are informational and evidence-based.
