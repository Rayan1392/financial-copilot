# Local Feature 139 TahlilApp mock

This is a Development-only external service simulator for the SapioAI Feature 139 gateway. It lives outside production projects and the backend deployment build context. It refuses to start unless `ASPNETCORE_ENVIRONMENT=Development`.

The service implements the v2 exchange contract used by `TahlilAppIdentityGateway`: `X-Api-Key`, `X-Correlation-Id`, and `X-SapioAI-Contract-Version: 2`; exchange response profile fields; and PII-free status response fields. Contract source: `specs/139-tahlilapp-session-exchange/TahlilAppIntegrationContract.md`, section “Contract version 2: authoritative paid activation timestamp”.

See [`../local-e2e/README.md`](../local-e2e/README.md) for the isolated database, service startup, browser scenario, and HTTP contract smoke test.
