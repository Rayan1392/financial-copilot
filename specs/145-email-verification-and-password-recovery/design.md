# Feature 145 — Design

## Components and boundaries

Keep identity orchestration in `OwnedIdentityService` and HTTP contracts in `AuthController`. Add a dedicated persisted email action token row to the existing `AuthDbContext`, not a separate authentication system. Add shared email message contracts and `IEmailSender` at the application boundary, with SMTP implementation and templates in Infrastructure. API owns public endpoint IP throttling; account cooldown is enforced against shared PostgreSQL state.

## Registration sequence

1. Existing request validation and Identity user/password validators run.
2. Create user with `EmailConfirmed=false`, `EmailVerificationRequired=true`; preserve role and tenant membership creation.
3. Persist a 24-hour verification token digest and enqueue nothing.
4. Send the email directly. Failure is logged as a sanitized delivery category; the accepted response remains generic. Resend can issue a fresh token after cooldown.
5. Do not create a refresh cookie or access JWT until a later successful login.

User plus membership creation should be transactional to avoid partial accounts. The token is committed before SMTP send so delivery failure leaves a recoverable account. Old active tokens of the same purpose are consumed when a new token is issued.

## Verify/reset sequence

Lookup token by digest and purpose, then in a transaction atomically consume with expiration predicate. On verification, set `EmailConfirmed=true` and `EmailVerificationRequired=false`. On reset, apply `UserManager` password validators, hash with `UserManager.PasswordHasher`, update through Identity, rotate security stamp, and revoke active refresh tokens. Commit token consumption and user/session changes together. Do not change email-confirmation fields on reset.

Concurrency is guarded by a conditional consume update; affected-row count must be one. UserManager's concurrency stamp protects competing user updates. The database is authoritative across API instances.

## Token table

`auth_email_action_tokens` fields: `Id`, nullable `UserId`, `Purpose` (`email_verification` or `password_reset`), unique `TokenHash`, keyed `EmailKeyHash` used only for cooldown accounting, `CreatedAtUtc`, `ExpiresAtUtc`, `ConsumedAtUtc`. Keep short-lived used/expired rows long enough to support cooldown and abuse investigation; cleanup can be a bounded opportunistic delete, never needed for correctness.

Token raw values exist only in request memory and the outbound email. Tokens never enter logs, exceptions, audit payloads, queues, or analytics. Email key hashing uses HMAC with an application secret and allows cooldown checks for addresses without storing plaintext email in token rows.

## API and validation

- `POST /api/auth/v1/email/verify`: token required, bounded length.
- `POST /api/auth/v1/email/resend`: valid email; identical 202 response for known, unknown, confirmed, or cooldown-suppressed address.
- `POST /api/auth/v1/password/forgot`: valid email; same generic 202 response.
- `POST /api/auth/v1/password/reset`: token and new password; Identity password validators authoritative; generic invalid-token response.
- Registration returns 202 with check-email guidance, without auth cookie/session. Existing duplicate-email status is kept to avoid broad registration contract changes.
- Public routes receive named IP rate-limit policy. Identity account lockout remains in place for login. Persisted per-email cooldown/max-hour cap protects multi-instance resend/reset email volume.

## Email transport

Use a maintained SMTP package (MailKit) behind a small adapter. Validate host, port, TLS mode, sender, and absolute frontend origin at startup. Require an HTTPS frontend origin in non-Development environments; support only fixed `/verify-email` and `/reset-password` paths. Never construct links from request Host or accept caller redirects. Add plain-text and HTML templates with `lang="fa"`, `dir="rtl"`, UTF-8. Bound connection/send timeout; no automatic retry that can issue another token. Sender failure does not undo account state or leak a different public outcome.

## Legacy and session policy

Migration adds `EmailVerificationRequired` default false. Do not backfill `EmailConfirmed`. New registrations require confirmation; existing false-confirmed users continue to log in under grandfathered policy and may opt in via resend. This policy is explicit and reversible through a later announced enrollment policy.

Reset revokes all refresh tokens and rotates the Identity security stamp. Newly issued owned-user access JWTs carry that stamp; JWT validation compares it with the current Identity row and rejects the JWT after a reset. This adds one Identity lookup for these web-app JWTs. Older JWTs without a stamp claim cannot be revoked this way and remain usable until expiry; checked-in settings currently permit up to 10,080 minutes (7 days). This compatibility window is documented for operations.

## Failure handling and observability

Safe categories: invalid/expired token, cooldown suppressed, SMTP timeout, SMTP rejected. Logs include category, operation, correlation ID and known user GUID, with no raw email or token. Unknown account, known account and delivery failure all return the same public reset/resend response. Database failure returns a server error; SMTP failure leaves token/account state intact for retry. Metrics must not use raw email/token as labels.
