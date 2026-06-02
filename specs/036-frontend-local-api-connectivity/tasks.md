# Tasks

## Configuration Contract

1. Define the local frontend `VITE_FINANCIAL_COPILOT_API_BASE_URL` as
   `http://localhost:5074` in an appropriate non-secret development environment file or
   documented startup command.
2. Audit browser-side and server-side API URL helpers and centralize or align their semantics:
   trim one trailing slash, append route paths consistently, and prevent silent browser
   fallback to the frontend SSR origin.
3. Add development-time validation or an explicit deterministic browser fallback for missing
   or malformed API-base configuration.
4. Document the distinction between the browser-exposed
   `VITE_FINANCIAL_COPILOT_API_BASE_URL` and any server-only
   `FINANCIAL_COPILOT_API_BASE_URL` override.

## Local CORS And Cookies

5. Add `http://localhost:8080` to the Development API CORS allowed-origin configuration while
   retaining `http://localhost:5173` where it is still used.
6. Verify the API keeps explicit origins, allowed headers, allowed methods, and credentials
   enabled without introducing wildcard-origin credential handling.
7. Verify register, login, refresh, logout, and revoke preserve the HttpOnly refresh cookie
   path `/api/auth/v1` and work across the documented localhost frontend/API origins.

## Verification

8. Add or extend backend integration tests for credentialed CORS preflight from
   `http://localhost:8080`.
9. Add a frontend-focused test or deterministic helper test proving browser-side auth URLs do
   not resolve to the frontend origin when local API configuration is missing or malformed.
10. Run a local smoke test from `http://localhost:8080`:
    - `POST /api/auth/v1/register` reaches `http://localhost:5074` and does not return `404`.
    - `POST /api/auth/v1/refresh` sends the refresh cookie and succeeds.
    - `POST /api/auth/v1/logout` revokes the session and clears the cookie.
11. Run relevant backend tests and frontend lint/build checks.

## Documentation

12. Add local startup documentation listing the frontend URL, API URL, required environment
    variables, and expected credentialed CORS origins.
13. Record the browser-side `404` root cause and verification evidence in the implementation
    checklist completion log after the remediation is implemented.

## Implementation Status

Completed on 2026-06-02. Browser-side and server-side FinancialCopilot clients now share a
validated URL builder with the deterministic local API fallback `http://localhost:5074`.
Development CORS explicitly allows `http://localhost:8080` and `http://localhost:5173` with
credentials. Restart existing frontend and API development processes after pulling the change
so they load the updated environment and CORS configuration.
