# Frontend Local API Connectivity

## User Story

As a local web-app developer, I want browser-side frontend requests to reach the configured
FinancialCopilot API host so registration, login, refresh, logout, and authenticated API calls
work when the frontend and .NET API run on different localhost ports.

## Current Gap

The browser-side auth bridge builds requests from `VITE_FINANCIAL_COPILOT_API_BASE_URL`, but the
frontend local environment does not define that variable. Its empty-string fallback sends
registration to the frontend SSR origin:

```http
POST http://localhost:8080/api/auth/v1/register
```

The implemented ASP.NET Core endpoint is hosted by the .NET API launch profile:

```http
POST http://localhost:5074/api/auth/v1/register
```

The server-side TanStack API client already falls back to `http://localhost:5074`, so browser
and server requests currently resolve the same backend routes differently. The API CORS
configuration also allows `http://localhost:5173` by default but does not include the observed
frontend development origin `http://localhost:8080`.

## Scope

- Define an explicit local browser-side API base URL for the frontend.
- Keep browser-side and server-side FinancialCopilot API URL resolution aligned.
- Allow the actual local frontend origin through credentialed API CORS configuration.
- Preserve HttpOnly refresh-cookie behavior across localhost origins.
- Document the local startup contract and verify registration from the browser-facing
  frontend.

## Acceptance Criteria

1. Local browser-side `register`, `login`, `refresh`, and `logout` requests resolve to
   `http://localhost:5074/api/auth/v1/*` when the documented development configuration is used.
2. Frontend server functions and browser-side auth use the same configured FinancialCopilot
   API base URL semantics. Browser code reads only Vite-exposed configuration; server code may
   prefer a server-only override.
3. The API allows the active local frontend origin, including `http://localhost:8080`, through
   credentialed CORS configuration.
4. CORS remains explicit-origin based. Do not use `AllowAnyOrigin()` together with
   `AllowCredentials()`.
5. Refresh tokens remain HttpOnly cookies scoped to `/api/auth/v1`; raw refresh tokens are not
   written to frontend storage.
6. Missing or malformed local API-base configuration fails clearly during development or is
   covered by a documented deterministic fallback. It must not silently route protected API
   requests to the frontend SSR origin.
7. A smoke test launched from the frontend origin verifies register, refresh, and logout
   against the .NET API without `404`, CORS, or cookie-path failures.
8. Frontend build checks and relevant backend integration tests pass.

## Out Of Scope

- Changing owned Identity domain rules, token claims, or refresh-token rotation behavior.
- Adding a production reverse proxy, ingress controller, or deployment topology.
- Replacing explicit API-base configuration with a Vite development proxy unless deployment
  requirements separately justify that architecture.
- Reworking Supabase remnants unrelated to the FinancialCopilot API bridge.

