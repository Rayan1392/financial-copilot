# BUG-045 — Customer 360 availability in conversation logs

Status: Gate A: Approved by owner on 2026-11-23

## Findings

- The conversation log derives its row identity from the conversation actor ID. A missing Identity match yields a row with the actor ID and empty display fields.
- In repository configuration, the reported ID identifies the Telegram gateway API client. Customer 360 requires a local user with active-tenant membership. The reported 404 and 403 are consistent with an actor ID that cannot be resolved to a customer in that tenant.
- The log currently makes every row's user cell clickable. The dialog concurrently requests both user endpoints, so the displayed error depends on which request rejects first.

## Proposed correction

- Resolve log identities against user accounts in the active tenant in the existing batched enrichment. Expose an explicit account-availability flag derived from that match. Use the same tenant boundary when enriching both the list and the turn detail.
- Keep all question and answer rows and their detail action. With a verified account, keep the user cell clickable even when the name, email and masked phone are empty. Without a verified account, display «حساب کاربری در دسترس نیست» as plain text and skip both Customer 360 requests.
- If an account becomes inaccessible after the list loads, show a localized unavailable-account message in the dialog for the corresponding 403 or 404. Preserve both user endpoints' tenant checks. This change requires no schema migration.

## Verification

Cover a gateway actor, an account with no display name, an actor associated only with another tenant, and an account that becomes inaccessible after listing. Verify that question details and row counts remain available and that a verified account can still open Customer 360.
