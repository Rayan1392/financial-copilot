# Feature 145 — Email Verification and Password Recovery

## Status

Implementation is complete and passed focused email-flow checks. Operational verification remains pending: the migration has not been applied to a live database, and SMTP delivery was exercised only with a fake sender.

## User stories

### Registration verification

New users are created with `EmailConfirmed=false` and `EmailVerificationRequired=true`. Registration creates no access or refresh token and returns the same accepted/check-email response regardless of mail delivery outcome. A purpose-bound, cryptographically random 256-bit verification token is emailed and only its SHA-256 digest is persisted. Verification marks the Identity email confirmed and clears the required flag. Invalid, expired, used, or wrong-purpose tokens are rejected. Resend is generic and subject to account and IP limits.

### Password recovery

Any submitted email receives the same public accepted response. Only an enabled eligible account receives a purpose-bound 256-bit reset token. A successful reset validates the existing Identity password policy, updates the password through the configured Identity hasher/manager, consumes the token atomically, rotates the security stamp, and revokes all active refresh tokens. It does not verify email or issue a session. Already-issued access JWTs remain usable until their configured expiry; the checked-in API settings currently set this to 10,080 minutes (7 days), despite the options class default of 15 minutes. This current stateless-JWT limit is documented for deployment.

### Email delivery

One `IEmailSender` supports verification and recovery messages. SMTP host/port/TLS/credentials/sender and the frontend origin are configuration/secrets. Messages have UTF-8 Persian RTL HTML and plain text. Delivery is direct from the API; no new service/queue is introduced. Delivery failures are logged without credentials, recipient, or token and do not roll back account/token creation; generic public responses permit safe retry. SMTP timeout is bounded; retries are user initiated through resend/request endpoints and cooldowns.

## Legacy rollout

Migration adds `EmailVerificationRequired` with a database default of `false`, leaving every existing `EmailConfirmed` value unchanged. Thus legacy users remain login eligible without being labeled as verified. New registrations set the flag to true and require confirmation. Legacy users may opt into confirmation through resend; no blanket lockout or false verification is performed. Operators must apply the migration before enabling the new API/frontend build.

## API contract

All routes are under `/api/auth/v1`. Requests are JSON. Public endpoints use IP throttling, generic reset/resend responses, HTTPS-only configured frontend links, and no redirect URL supplied by the client.

| Method and route | Request | Success | Other outcomes |
|---|---|---|---|
| `POST register` (existing) | `{email,password}` | `202 {message}`; no session/cookie | existing validation/duplicate behavior; Identity password policy |
| `POST email/verify` | `{token}` | `204` | `400` invalid/expired/used token |
| `POST email/resend` | `{email}` | `202 {message}` for every email; cooldown may be silently applied | IP limit `429` |
| `POST password/forgot` | `{email}` | `202 {message}` for every email | IP limit `429` |
| `POST password/reset` | `{token,newPassword}` | `204`, no authentication/session | `400` invalid/expired/used token or password rejected; IP limit `429` |

The public success message for resend and forgot is identical. Tokens are sent only in email links. The frontend reads token fragments from the URL so browser referrers do not transmit tokens; it posts the token in the request body and removes the fragment after extraction.

## Persistence and concurrency

- Add `EmailVerificationRequired BOOLEAN NOT NULL DEFAULT FALSE` to `auth_users`.
- Add `auth_email_action_tokens`: UUID primary key, nullable user FK, token digest unique, purpose, normalized-email keyed digest for cooldown, created/expiry/consumed timestamps. Index `(UserId, Purpose, CreatedAtUtc)` and `(EmailKeyHash, Purpose, CreatedAtUtc)`; token digest has a unique index.
- For resend/reset, apply cooldown using persisted recent token rows; token rows are shared across instances. API IP throttling uses ASP.NET Core rate limiting. Deployment with multiple API instances must use a shared limiter store or edge gateway policy; the framework's in-memory limiter is per process.
- Consume with a conditional `UPDATE ... WHERE ConsumedAtUtc IS NULL AND ExpiresAtUtc > now`; perform user mutation and token consumption in one DB transaction. Exactly one concurrent consumer can succeed.
- Successful reset sets `PasswordHash` via the configured `UserManager` password hasher, increments Identity security stamp, and marks all unrevoked refresh token rows revoked. It leaves `EmailConfirmed` and `EmailVerificationRequired` unchanged.

## Security and operations

- 32 random bytes from `RandomNumberGenerator`; URL-safe base64 without padding; SHA-256 digest stored; no token in logs/telemetry.
- Purpose isolation in token row plus validation; verification lifetime 24 hours, reset lifetime 30 minutes; one-use atomic consumption.
- Trusted HTTPS frontend origin from configuration; reject non-HTTPS outside Development and never use request Host or arbitrary return URL.
- Account/email cooldown: 60 seconds; max 5 account requests/hour; public endpoint IP partition: 10 requests/minute (configurable). Registration/login brute-force protections remain Identity lockout plus IP throttling.
- Generic enumeration-safe responses for resend/reset. Registration's existing duplicate-email response remains for compatibility and should be tracked as a later hardening item.
- Audit logs record action outcome category, correlation ID, and user ID only when known; never include email/token/password.
- SMTP credentials are external secrets. No production send verification is claimed until SMTP is configured and operationally tested.

## Frontend integration

- Registration displays a Persian check-email state and does not attempt to establish an authenticated session.
- Add verification and forgot/reset pages using existing React Router/TanStack patterns. Use `VITE_FINANCIAL_COPILOT_API_BASE_URL`; no separate frontend service is created.
- Verification and reset tokens are carried in URL fragments and submitted once. Password confirmation and existing minimum-length UX are retained; backend Identity validators remain authoritative.
- After reset, return user to sign-in. Email links use fixed configured routes and no user-controlled redirect.

## Acceptance criteria

1. New registration creates an unconfirmed verification-required account and issues no tokens/session.
2. Verification tokens are random, purpose-specific, stored as digests, expiring, one-use, and safe under concurrent submission.
3. Unverified new accounts cannot login or refresh into authenticated access.
4. Legacy users keep their existing confirmed value and access policy; migration does not mark them verified.
5. Resend and reset request responses do not reveal account existence and honor cooldown/IP limits.
6. Reset uses Identity password policy and hash mechanism, consumes token once, revokes every refresh session, and does not verify email or create a session.
7. Email transport/configuration/templates are shared, secret-based, UTF-8 RTL, and log-safe.
8. Frontend supports registration check-email, verification, forgot password, and reset.
9. API, persistence, and frontend tests use fakes; tests never send real email.
10. Migrations, required settings, operational SMTP dependency, actual tests, and remaining risks are reported.
