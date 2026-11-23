# Feature 146 Change — Shared Customer 360 and Manual Credit Grants

- Layers: backend, frontend
- Area: Administration, Billing
- Priority: Owner-requested
- Complexity: M
- Depends On: Feature 146 base Customer 360, Feature 164 conversation logs, Feature 035 manual credit adjustment
- References: `specs/146-unified-user-management-and-customer-360/Design.md`, `specs/164-admin-ai-conversation-logs/user-story.md`, `specs/035-admin-identity-and-entitlement-management/`, `docs/admin-management-api.md`

## User Story

As an administrator, I want to open a user's account from a question-and-answer log row, find their account information quickly, and grant credits when authorized.

## Acceptance Criteria

1. The Users list and conversation log open one shared dialog in the account tab. Each entry point targets the row's user ID. The conversation log uses name, email, masked mobile, then «بدون نام» as label fallbacks.
2. Closing the dialog returns the administrator to the originating row with the list's filters and page intact. Missing and failed account loads have a clear state. A slow response for one user cannot populate another user's dialog.
3. At desktop and mobile widths, the RTL dialog shows the user's name or fallback, identifier and status. Existing displayed fields, section states, pagination, chat history, and permitted account actions remain available. Keyboard navigation and focus restoration work on both entry points.
4. Only an administrator with `admin.credits.adjust` can use the grant action, and only when an existing Billing account is associated with the selected user in the active tenant. Existing read permissions govern visibility of balances and histories independently of the write permission.
5. The form accepts a positive whole number and a nonblank reason. An in-flight submission is disabled, and retrying the same request after an uncertain outcome does not grant credits twice. The existing Billing command records the ledger entry and audit trail.
6. A successful grant refreshes visible credit data and any previously loaded credit history. A failed refresh after a confirmed grant reports that distinction. A failed grant shows an error for the selected account and allows a retry.

## Assumptions

- Credits are added to the user's existing Billing account through the current manual adjustment policy.
- Existing Customer 360 and conversation logs already use `admin.users.read`. Their existing permission-aware sections remain authoritative.
