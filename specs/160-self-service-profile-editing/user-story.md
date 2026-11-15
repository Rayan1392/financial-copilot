# User Story: Self-Service Profile Editing

## Feature Folder

`160-self-service-profile-editing`

## Layers / Area

Layers: backend | database | frontend | ops  
Area: Authentication

## Priority

P1

## Complexity

L

## Depends On

None. Reuses already-implemented profile, email-verification, Identity, and mobile-PWA components; their separate operational rollout follow-ups are not prerequisites for this implementation.

## References

- `specs/160-self-service-profile-editing/design-proposal.md`
- `specs/139-tahlilapp-session-exchange/changes/profile-get-without-tahlilapp-row/design-proposal.md`
- `specs/145-email-verification-and-password-recovery/spec.md`
- `specs/153-mobile-pwa-workspace/spec.md`
- `src/backend/FinancialCopilot.API/Controllers/ProfileController.cs`
- `src/backend/FinancialCopilot.Infrastructure/Authentication/OwnedIdentityService.cs`
- `src/backend/FinancialCopilot.Infrastructure/Authentication/Persistence/AuthDbContext.cs`
- `src/frontend/src/routes/_app.profile.tsx`
- `src/frontend/src/routes/m.account.profile.tsx`
- `docker-compose.yml`, root `.env.example`, and API `appsettings.json`

## Standards Source Of Truth

Obey `.claude/rules/*` and `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`.

## User Story

As an authenticated SapioAI user, I want to edit my profile from web or mobile, so that my account details and sign-in credentials stay current.

## Business Context

The current account surfaces display profile information but do not let a user maintain their local account details. Both the desktop web workspace and mobile PWA must support the same account capabilities.

## Scope

- Edit first and last name.
- Change email with verification before the new address becomes the login address.
- Change password after current-password validation and revoke refresh sessions after success.
- Add/change/remove a local contact mobile number, verified by SMS through MelliPayamak. It remains distinct from TahlilApp-sourced mobile data.
- Add/change/remove a profile photo with server-side image validation and a 1 MiB stored-image maximum.
- Provide responsive UI at `/_app/profile` and `/m/account/profile`.
- Add safe, documented SMS settings in API `appsettings.json`, root `.env.example`, and `docker-compose.yml`. Secrets remain external and untracked.

  Proposed deployment names: `AUTH_SMS_ENABLED`, `AUTH_SMS_BASE_ADDRESS`, `AUTH_SMS_OTP_PATH_TOKEN`, `AUTH_SMS_TIMEOUT_SECONDS`, `AUTH_SMS_CODE_LIFETIME_MINUTES`, `AUTH_SMS_COOLDOWN_SECONDS`, `AUTH_SMS_MAXIMUM_REQUESTS_PER_HOUR`, and `AUTH_SMS_MAXIMUM_CODE_ATTEMPTS`. The endpoint token is a secret and must not appear in committed configuration or logs.

## Out Of Scope

- Editing or syncing changes to TahlilApp-owned profile fields.
- Admin editing of a user's profile.
- Social login, account linking, or account merge.
- Avatar cropping/editor, SVG, video, or arbitrary document uploads.
- SMS delivery reports, bulk messaging, or general-purpose notification services.
- Changing login/session policy beyond the approved password-change behavior.

## Functional Requirements

1. Only an authenticated user can read or mutate their own local profile; the API derives user ID from the authenticated subject.
2. A user can save valid first and last names, and the updated values are returned on the next profile read.
3. A requested email change remains pending until a valid one-time email verification token is consumed. The previous verified address remains active before confirmation.
4. Password change validates the current password and the existing Identity password policy. Success revokes refresh sessions; password material is never returned or logged.
5. A user can request a verification code for an Iranian mobile number and activate that number only by confirming the unexpired, unused code. The code is purpose-bound, stored as a digest, and subject to resend and attempt limits.
6. Local mobile contact data is stored separately from provider-synchronized TahlilApp mobile data; update/remove operations do not overwrite the latter.
7. Avatar upload accepts only the approved raster formats, validates decoded content, normalizes it, strips metadata, and rejects oversized payloads. The user can remove the avatar.
8. The same fields and flows are available from desktop and mobile profile screens, with accessible labels, Persian RTL layout, and clear pending/verified/error states.
9. MelliPayamak endpoint token is supplied only through runtime environment/secrets; checked-in configuration contains no live token. SMS failure does not activate a new mobile number and is reported without leaking provider response data or phone/code values.
10. Automated verification uses a fake SMS sender and never sends live SMS.

## Data / Schema

Use `AuthDbContext` and existing Identity profile ownership. Add only the minimum local contact-mobile verification state and avatar fields needed by this feature; preserve TahlilApp profile columns and encryption contract. Use hashed, expiring, one-use verification-token persistence, with indexes and concurrency-safe consumption. Migration details are to be finalized in the Gate B plan after reviewing current Auth model constraints.

## API

Exact routes/DTOs are finalized in the Gate B-approved plan. Required operations: read/update local names, request/confirm email change, change password, request/confirm mobile verification, upload/delete avatar, and bounded avatar retrieval. All mutation operations are authenticated and self-scoped.

## UI

- Desktop: `/_app/profile`.
- Mobile PWA: `/m/account/profile`.
- Reuse validation and API integration where possible; retain mobile-specific navigation and touch-friendly controls.
- Persian RTL labels and accessible status announcements are required.

## Non-Functional Requirements

- Enforce authenticated ownership, request-size bounds, secure token handling, rate limits, outbound timeout, safe logging, cancellation, and generic provider-safe errors.
- Never persist or log plaintext verification codes, passwords, provider API key, or full mobile number in diagnostics.
- Do not call MelliPayamak from browser code.

## Acceptance Criteria

- [ ] AC-01 A signed-in user can edit and read back their own first and last name on desktop and mobile.
- [ ] AC-02 Another user cannot read or change the caller's profile by supplying a different user ID.
- [ ] AC-03 A pending email does not replace the current login email until verified; invalid, expired, or consumed tokens fail safely.
- [ ] AC-04 Password change requires the current password and Identity policy; on success refresh sessions are revoked.
- [ ] AC-05 A mobile number becomes active only after verification; code expiry, reuse, attempt limits, and resend limits are enforced.
- [ ] AC-06 Local mobile updates leave TahlilApp-owned mobile and provider synchronization fields unchanged.
- [ ] AC-07 Invalid, oversized, or unsupported avatar uploads are rejected; accepted images are normalized and removable.
- [ ] AC-08 Desktop and mobile UI present save, pending verification, success, validation, and provider-failure states accessibly.
- [ ] AC-09 MelliPayamak options are configured through `appsettings.json` and root `.env.example`/Docker Compose mappings, with the endpoint token sourced from external environment/secrets and no real secrets committed.
- [ ] AC-10 Automated checks use a fake SMS transport and produce no live outbound SMS.
- [ ] AC-11 User-facing documentation and the Unreleased changelog line are updated after implementation.

## Assumptions

- The user's mobile contact is local SapioAI account data, separate from TahlilApp-sourced mobile; the owner approved this at Gate A on 2026-11-14.
- SMS verification through MelliPayamak uses the owner-supplied HTTPS OTP endpoint and JSON `to` payload. Based on the supplied description, `code` is the OTP and `status` contains an error description when sending fails; implementation treats a missing code or non-empty error status as failure and never returns the code to the caller.
- Avatar bytes are stored in the Auth-owned PostgreSQL profile row with strict bounds unless the Gate B design review selects an existing shared storage facility.
- Successful password change revokes refresh sessions and requires re-authentication.
