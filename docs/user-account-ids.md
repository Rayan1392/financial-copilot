# User → CustomerAccount IDs

Operational note recording the `CustomerAccountId` GUIDs used for manual billing operations
(e.g. credit top-ups). See [adding-credit-to-a-user.md](adding-credit-to-a-user.md) for how to
resolve a GUID and how to add credit.

This is **not** a credential file — it only maps a user to their billing-account GUID. Do not
store tokens, passwords, or API keys here.

| User (email) | UserId | TenantId | CustomerAccountId | Notes |
| --- | --- | --- | --- | --- |
| _example@domain.com_ | _<user-guid>_ | _<tenant-guid>_ | _<customer-account-guid>_ | e.g. QA top-up target |

> Replace the example row with the real values once you resolve them with the SQL query in
> [adding-credit-to-a-user.md](adding-credit-to-a-user.md#step-1--find-the-users-customeraccountid-guid).
