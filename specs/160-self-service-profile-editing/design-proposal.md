# Self-Service Profile Editing

Status: Gate A: Approved by the owner on 2026-11-14

## Layers / Area

Layers: backend | database | frontend | ops  
Area: Authentication

## Goal

Let an authenticated user manage their SapioAI account profile from both the desktop web workspace and the mobile PWA.

## Existing implementation

- `GET /api/auth/v1/profile` returns a read-only view of TahlilApp-sourced profile information. Its mobile value is protected at rest and explicitly revealed only on request.
- Owned Identity already supports email enrollment/verification, password reset, refresh-token revocation, and Identity password validation. There is no authenticated change-password flow.
- Desktop profile is at `/_app/profile`; the mobile PWA profile is at `/m/account/profile`.
- There is no existing profile-avatar storage or SMS sender abstraction.

## Proposed scope

- Edit the user's local first and last name.
- Change the account email through a pending-email flow. Keep the current email as the login address until the new address is verified; reuse the existing email token and delivery patterns where compatible.
- Change password after validating the current password. Follow Identity password rules and revoke refresh sessions after a successful change, requiring sign-in again.
- Manage a local contact mobile number distinct from the TahlilApp-sourced mobile. Send and verify a one-time code through MelliPayamak before replacing the verified local number. Do not write user changes into TahlilApp-owned profile fields.
- Upload, replace, or remove a profile photo. Validate and normalize raster images server-side, with a proposed 1 MiB stored-image cap. Store bytes and media type in the Auth-owned profile record; do not expose an arbitrary file path or accept SVG.
- Provide the same capability in desktop and mobile profile experiences, reusing API contracts and shared frontend logic while preserving each workspace's layout.

## Proposed API and persistence

- Extend the authenticated own-profile API for safe local profile data and avatar retrieval/update.
- Add authenticated endpoints for starting and confirming email and mobile changes, plus password change. Never return one-time tokens or passwords in profile reads.
- Persist local contact mobile and its verification state separately from the protected TahlilApp mobile. Persist the normalized avatar and content type with the user's local profile. Add an EF Core migration only for fields that are not already represented by the authoritative Identity model.
- Hash and purpose-scope mobile verification codes; enforce expiration, one-time use, cooldown, attempt limits, and generic responses where account enumeration is possible. Never log codes, API credentials, or full mobile values.

## MelliPayamak configuration

- The supplied MelliPayamak contract uses HTTPS `POST https://console.melipayamak.com/api/send/otp/{token}` with JSON `{ "to": "09123456789" }`. The token is a path credential and must never be logged or checked in.
- Add a typed `Authentication:Sms` section to API `appsettings.json` with non-secret defaults for enabled state, provider, base address, timeout, code lifetime, cooldown, request limits, and attempt limits. Keep the endpoint token out of checked-in appsettings.
- Document environment variables `AUTH_SMS_ENABLED`, `AUTH_SMS_BASE_ADDRESS`, `AUTH_SMS_OTP_PATH_TOKEN`, `AUTH_SMS_TIMEOUT_SECONDS`, `AUTH_SMS_CODE_LIFETIME_MINUTES`, `AUTH_SMS_COOLDOWN_SECONDS`, `AUTH_SMS_MAXIMUM_REQUESTS_PER_HOUR`, and `AUTH_SMS_MAXIMUM_CODE_ATTEMPTS` in the root `.env.example`; bind them in `docker-compose.yml` as `Authentication__Sms__*`. Keep the token blank/placeheld in the sample and source production value from the ignored deployment `.env` or secret manager.
- The owner-provided response contract defines `code` as the OTP and `status` as an error description when sending fails. Never return the provider `code` in the API response. Store only its keyed digest and compare the user's submitted code in constant time. A missing code, non-empty status, or unsuccessful HTTP response is treated as failure. Do not retry a send automatically.

## Security and reliability

- All mutation routes are authenticated, scoped to the subject user, rate-limited, validated server-side, and return safe problem details.
- Verify ownership on every mutation; do not accept user IDs from clients.
- Email/mobile changes become active only after successful verification. Existing verified values remain usable until then.
- Bound image request/body sizes before buffering; validate file signatures and decode/re-encode accepted formats; strip metadata.
- Bound outbound SMS timeouts and avoid automatic retries that could send duplicate verification messages. Log only outcome category, request/correlation ID, and local user ID.
- Existing TahlilApp identity data remains provider-owned and unchanged.

## Verification approach

Plan unit and integration coverage for ownership, validation, token lifecycle/rate limits, duplicate email/mobile handling, password/session behavior, image limits/content validation, and SMS provider failures using a fake sender. Plan frontend coverage for desktop and mobile forms, loading/error/success states, and verification flows. No live SMS is used by automated checks.

## Trade-offs and risks

- Keeping avatar bytes in Auth PostgreSQL avoids introducing a new object-storage service, at the cost of database growth and requiring a strict image cap.
- MelliPayamak transport and response handling follow the owner-supplied path-token OTP contract; deployment still requires a valid provider account token and a separate code-hash secret.
- Email verification is implemented, but changing an already-confirmed account email may require changes to token purpose/consumption semantics; detailed planning will preserve existing registration and recovery behavior.
- Requiring sign-in after password change is predictable and aligns with the existing password-reset session-revocation policy, but interrupts other active devices.

## Gate A decision

Approved by the owner on 2026-11-14, including a distinct local contact mobile and SMS verification through MelliPayamak. The owner supplied the OTP endpoint, request shape, and example response on 2026-11-14. The meaning and success/error behavior of the response fields must be confirmed before the provider adapter is finalized.
