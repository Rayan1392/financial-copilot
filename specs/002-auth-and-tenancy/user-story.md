# User Story — Authentication and Tenant Context

## Story

As a platform owner,  
I want the backend to support both web app users and SaaS API clients,  
so that the same backend can serve our own UI and external partners.

## Acceptance Criteria

- JWT bearer authentication is supported for web app users.
- API key authentication is supported for SaaS clients.
- Request context includes user id, tenant id/client id, and auth mode.
- Unauthorized requests return 401.
- Authenticated but forbidden requests return 403.
- Rate limiting can be applied per user and per API client.
- Tenant/client context is not optional in protected business endpoints.
- The public `POST /api/ai/v1/query` endpoint supports the authenticated web app user and SaaS API client authentication models.

## Technical Notes

- Initial implementation may use local users/API clients tables.
- Keep design compatible with future OAuth2 client credentials.
