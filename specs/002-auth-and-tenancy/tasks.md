# Tasks

- Create User, ApiClient, Tenant/Client domain models.
- Implement JWT auth.
- Implement API key auth handler.
- Implement current actor/tenant/API-client context service without assuming a user id for SaaS API-client requests.
- Add authorization policies.
- Add integration tests for auth modes.
- Protect the AI facade and generic Conversation endpoints using current actor/tenant/API-client context.
- Expose authenticated context required by `IBillableAccountResolver` without placing billing decisions in the authentication layer.
