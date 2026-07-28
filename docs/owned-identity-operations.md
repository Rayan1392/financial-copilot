# Owned Identity Operations

## Configuration

The web application authenticates through ASP.NET Core Identity. JWT signing secrets must be
provided outside source control:

```text
Authentication__JwtBearer__Issuer
Authentication__JwtBearer__Audience
Authentication__JwtBearer__SigningKey
Authentication__OwnedIdentity__DefaultTenantId
Authentication__OwnedIdentity__DefaultTenantName
Authentication__OwnedIdentity__AccessTokenMinutes
Authentication__OwnedIdentity__RefreshTokenDays
```

Access tokens are short-lived JWTs. Opaque refresh tokens are stored as SHA-256 hashes in
PostgreSQL and returned only as `HttpOnly`, `SameSite=Strict` cookies scoped to
`/api/auth/v1`. Passwords, raw refresh tokens, wallet balances, plan limits, and credit amounts
must not be stored in JWT claims.

## Local Frontend Connectivity

The local .NET API launch profile listens on `http://localhost:5074`. Browser-side frontend
requests require the Vite-exposed API origin:

```text
VITE_FINANCIAL_COPILOT_API_BASE_URL="http://localhost:5074"
```

Copy the non-secret values from `src/frontend/.env.example` into `src/frontend/.env` when
setting up a local environment. Browser code can read only the `VITE_` variable. TanStack
server functions use `FINANCIAL_COPILOT_API_BASE_URL` when it is defined, then fall back to the
Vite-exposed value and finally to `http://localhost:5074`.

Development API CORS configuration explicitly allows `http://localhost:8080` and
`http://localhost:5173` with credentials. Keep these as explicit origins; wildcard origins
must not be combined with credentialed cookies.

## Migration Commands

From the repository root:

```powershell
$env:FINANCIAL_COPILOT_CONNECTION_STRING = "<PostgreSQL connection string>"

dotnet ef database update `
  --project src/backend/FinancialCopilot.Infrastructure `
  --context AuthDbContext

dotnet ef database update `
  --project src/backend/FinancialCopilot.Infrastructure `
  --startup-project src/backend/FinancialCopilot.API `
  --context BillingDbContext
```

The authentication migration creates Identity, permission, tenant-membership, and refresh-token
tables. The Billing migration creates versioned `billing_plan_capabilities` and seeds the
initial `Free`, `Pro`, `Plus`, and `Premium` policy matrix for scanner, stock analysis, reports,
watchlist, portfolio, deep research, and AI-credit enforcement.

## Seed Strategy

Registration and login idempotently ensure the baseline tenant, stable permission catalog, and
baseline `User`, `DataAdmin`, and `BillingAdmin` role mappings exist. No production passwords
are seeded. Roles organize assignments; ASP.NET Core authorization policies enforce permission
claims.

## Session Revocation

`POST /api/auth/v1/logout` revokes the current refresh session. `POST /api/auth/v1/revoke`
supports explicit refresh-session revocation. Reusing an already rotated refresh token revokes
the remaining active token family and returns an authentication ProblemDetails response with a
correlation id.
