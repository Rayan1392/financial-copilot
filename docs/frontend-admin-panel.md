# Frontend Administration Panel

The React administration panel is available at `/admin`. It consumes the backend-owned Admin
Management API documented in [admin-management-api.md](./admin-management-api.md).

## Bootstrap Prerequisite

Assign the initial `SuperAdmin` through the controlled backend bootstrap process before using
the panel. The frontend does not create privileged bootstrap users and does not bypass backend
lockout protection.

## Permission Matrix

| Screen | Read permission | Mutation permission |
| --- | --- | --- |
| Users | `admin.users.read` | `admin.users.manage`, `admin.roles.manage` |
| Roles | `admin.roles.read`, `admin.permissions.read` | `admin.roles.manage`, `admin.permissions.manage` |
| Tenants | `admin.tenants.read` | `admin.tenants.manage` |
| Billing plans | `admin.plans.read` | `admin.plans.manage` |
| Customer subscriptions | `admin.subscriptions.read` | `admin.subscriptions.manage` |
| Usage ledger | `admin.usage-ledger.read` | None |
| Credits | Customer account lookup | `admin.credits.adjust` |
| Security audits | `admin.security-audit.read` | None |
| Billing audits | `admin.billing-audit.read` | None |

The UI hides modules and mutation controls when the current JWT lacks the corresponding
permission. These checks improve usability only. ASP.NET Core permission policies remain the
security boundary for every request.

## Supported Workflows

- Search and inspect users, update status, unlock accounts, revoke refresh sessions, and assign
  roles.
- Inspect roles, create roles, change role status, and assign stable permission codes.
- Inspect tenant membership, add members, and remove members.
- Inspect and publish plans, inspect capabilities, inspect and update subscriptions, read usage
  ledger entries, and apply idempotent credit adjustments.
- Read security and Billing audit events with correlation identifiers.

Sensitive mutations require confirmation and a reason. Credit adjustment idempotency keys are
generated in the browser for a single submission and sent to the Billing-owned backend
workflow. The panel never edits wallet projections or immutable ledger history directly.

## Verified Backend Contract Gap

The current Admin Management API does not expose a customer-account search endpoint. Billing
operations therefore accept an explicit `CustomerAccountId` GUID. Add a bounded,
tenant-scoped customer-account lookup endpoint in a follow-up backend story before replacing
the GUID entry field with search. Do not query Billing persistence directly from the frontend.
