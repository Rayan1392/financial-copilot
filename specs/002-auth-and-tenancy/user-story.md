# User Story — Authentication and Tenant Context

## Story

As a platform owner,  
I want the backend to support both web app users and SaaS API clients,  
so that the same backend can serve our own UI and external partners.

## Acceptance Criteria

- JWT bearer authentication is supported for web app users.
- API key authentication is supported for SaaS clients.
- Request context includes actor type, actor id, tenant id, API client id when applicable, and auth mode; a SaaS API call does not require a FinancialCopilot end-user id.
- Unauthorized requests return 401.
- Authenticated but forbidden requests return 403.
- Rate limiting can be applied per user and per API client.
- Tenant/client context is not optional in protected business endpoints.
- The public `POST /api/ai/v1/query` endpoint supports the authenticated web app user and SaaS API client authentication models.
- Authentication resolves the caller and tenant only; `FinancialCopilot.Billing` resolves the billed `CustomerAccount` according to organization or direct-consumer policy.

## Technical Notes

- Initial implementation may use local user, tenant, and API-client tables.
- Keep design compatible with future OAuth2 client credentials.
- A partner-provided `externalUserId` is tenant-scoped attribution data, not authentication identity or a directly billed consumer account.
