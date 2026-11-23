# Feature 146 Change — Shared Customer 360 and Manual Credit Grants

Status: Gate A: Approved by owner on 2026-11-23

## Context

Feature 164's conversation log already returns the user ID, display name, email and masked mobile with each row. Feature 146's Customer 360 dialog is embedded in the Users panel. The Billing panel already grants manual credits through an authorized, tenant-scoped operation with an idempotency key, reason and audit trail.

## Approved Design

- Open the same Customer 360 dialog from Users and the user cell in «تاریخچه پرسش و پاسخ کاربران». Use the row's user ID for lookup. Show the display name, email, masked mobile or «بدون نام» in that order.
- Present a Persian RTL header with name, ID and status. Group account details, linked identities, subscription, credits and purchases into labeled sections. Use a responsive grid with a single column on narrow screens. Preserve all existing displayed fields, actions, history tabs and pagination.
- Place «افزودن اعتبار» in the credits section for an administrator with the existing adjustment permission when the user has a Billing account. Ask for a positive whole number of credits and a reason. Use the existing adjustment operation and refresh the balance and any opened credit history after success.
- Keep the tenant boundary, field-level read permissions, existing Billing ledger and audit trail. No schema migration is required.

## Verification

Verify both entry points, unnamed users, credit permissions and refresh behavior, data preservation, and the desktop and mobile layouts. See the implementation plan for test coverage and rollout dependencies.
