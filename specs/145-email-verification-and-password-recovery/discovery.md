# Feature 145 — Authentication Discovery

## Scope and evidence

This document records repository evidence gathered on 2026-10-14 from the indexed source graph and the current source/configuration files. No production database connection or live schema was available in the repository, so database findings describe EF Core mappings and migrations, not a running database.

## Existing authentication flow

- `POST /api/auth/v1/register`, `/login`, `/refresh`, `/logout`, `/revoke`, `/bootstrap-superadmin`, and `/me` are implemented in `src/backend/FinancialCopilot.API/Controllers/AuthController.cs`.
- Registration and login are handled by `IOwnedIdentityService` / `OwnedIdentityService` in `src/backend/FinancialCopilot.Infrastructure/Authentication/OwnedIdentityContracts.cs` and `OwnedIdentityService.cs`.
- The service uses `UserManager<FinancialCopilotUser>` and `RoleManager<FinancialCopilotRole>`, so ASP.NET Core Identity is in use. It normalizes email through Identity, stores an Identity password hash, checks passwords with `CheckPasswordAsync`, and uses Identity lockout counters.
- Current registration creates an enabled user, assigns the `User` role and default tenant membership, then immediately issues an access JWT and persisted refresh token. Current login checks enabled/lockout/password but does not check `EmailConfirmed`.
- Access JWT lifetime is configured by `Authentication:OwnedIdentity:AccessTokenMinutes` (class default 15 minutes; checked-in `appsettings.json` currently overrides it to 10,080 minutes / 7 days). Refresh tokens are 64 cryptographically random bytes; only SHA-256 hashes are stored in `auth_refresh_tokens`. Refresh tokens rotate and replay revokes the token family.
- Refresh credentials are returned in an HttpOnly, SameSite=Strict cookie by `AuthController`; the access token is returned in the response body. JWT validation is configured in API middleware. Access tokens are stateless and currently have no per-token database revocation check.
- Frontend auth is in `src/frontend/src/routes/auth.tsx` and `src/frontend/src/integrations/financial-copilot/auth.ts`. The current screen supports sign-in/sign-up, assumes registration returns a session, and has no verification or recovery pages.
- Password policy and Identity service registration are configured in `src/backend/FinancialCopilot.Infrastructure/ServiceCollectionExtensions.cs` around the `AddIdentityCore<FinancialCopilotUser>` setup. Login and registration do not have dedicated endpoint rate limits in `AuthController`; API rate limiting currently targets authenticated actors.

## Persistence and schema evidence

- `AuthDbContext` extends `IdentityDbContext<FinancialCopilotUser, FinancialCopilotRole, Guid, ...>` and maps Identity tables to `auth_users`, `auth_roles`, `auth_user_roles`, `auth_user_claims`, `auth_user_logins`, `auth_role_claims`, and `auth_user_tokens`.
- The Identity user has the standard `Id`, `Email`, `NormalizedEmail`, `EmailConfirmed`, `PasswordHash`, `SecurityStamp`, `ConcurrencyStamp`, access-failure, lockout, and username fields; `FinancialCopilotUser` adds `IsEnabled`.
- `auth_refresh_tokens` stores token hash, user/tenant IDs, issue/expiry/revocation timestamps, rotation link, and reason. It has unique token-hash and user/expiry indexes.
- Existing custom authentication token patterns exist for Telegram account linking in `auth_telegram_link_tokens`, with purpose, status, expiration, and SHA-256 token hash. This is useful as a persistence convention but is bound to Telegram behavior and should not be overloaded.
- EF migrations and `AuthDbContextModelSnapshot` exist under `src/backend/FinancialCopilot.Infrastructure/Authentication/Persistence/Migrations`. No live PostgreSQL schema was inspected.
- Email uniqueness is enforced via Identity's normalized username/email configuration and Identity validators; exact production constraint state remains unverified without a database connection.

## Email and operations infrastructure

- Repository-wide backend source/config search found no SMTP client, MailKit, `IEmailSender`, SendGrid, or equivalent email delivery adapter/package. Existing RabbitMQ and worker infrastructure is unrelated to email and does not justify a new service for this feature.
- `TimeProvider` is already injected into the identity service. DI is composed in `ServiceCollectionExtensions.cs`; API rate limiting is configured in `FinancialCopilot.API/Program.cs`.
- No authentication audit table/event was found in the owned identity flow. `auth_security_admin_audits` covers administrative actions, not account security events.

## Verified gaps

1. New users can log in and receive tokens immediately without confirming email.
2. There are no verification or password-reset operations, one-time email tokens, or email transport.
3. Registration presently returns a live session, which must change for new registrations to avoid token issuance before verification.
4. Refresh-token revocation is available, while existing access JWTs remain valid until expiry (currently configured to 7 days in the checked-in API settings).
5. Public auth operations lack dedicated IP throttling and account/email cooldown controls.
6. Frontend has no verification or password recovery flow and its registration behavior assumes immediate login.

## Proposed extension points

- Extend `FinancialCopilotUser` with `EmailVerificationRequired`; new registrations set it true. Login and refresh gate only accounts where this flag is true and `EmailConfirmed` is false.
- Add a purpose-specific auth email token table in `AuthDbContext`, storing only SHA-256 token digests and expiry/consumption metadata. Consume with a conditional database update inside a transaction.
- Extend `IOwnedIdentityService` and `AuthController` with verify, resend, request-reset, and confirm-reset operations. Public forgot/resend responses are generic.
- Add a shared `IEmailSender` and SMTP adapter in Infrastructure with strongly validated configuration, trusted frontend origin, HTML and text RTL templates, bounded timeout, and structured logs that exclude recipient/token/credentials.
- Use persisted account/email cooldown for known accounts and an API IP rate-limit policy for public endpoints; document shared-store requirements for multi-instance IP limiting.
- Add frontend verification and recovery routes and adjust registration UX to show a check-email state without calling `applySession`.

## Compatibility and migration risks

- Existing accounts may have `EmailConfirmed=false` because current registration never required confirmation. The migration must leave their confirmation values untouched and default `EmailVerificationRequired=false`; this preserves established access without misrepresenting their email as verified. Users can opt into verification by requesting a verification link. New accounts are explicitly gated.
- Registration's response changes from a session response to an accepted/check-email response. The owned frontend is updated in the same change; external clients relying on automatic registration login must adapt.
- The repository has no confirmed live data or deployment configuration, so migration application, SMTP reachability, DNS/domain setup, and actual email delivery require operational verification.
- `SmtpClient` would introduce an unsuitable legacy transport choice; use a maintained provider library if present/approved. Discovery found no email package, so the design should use a maintained SMTP client dependency and avoid custom SMTP protocol code.
