# Feature 145 — Implementation Tasks

## Discovery and design

- [x] Read repository README, auth implementation, frontend auth, Identity schema/migrations, and email/rate-limit infrastructure.
- [x] Record findings, evidence limits, compatibility policy, and extension points.
- [x] Define feature scope, API contract, schema, token/session policy, and frontend contract.
- [x] Complete pre-implementation critical design review and resolve security blockers.

## Persistence and identity

- [x] Add explicit new-account verification-required state with safe legacy default.
- [x] Add purpose-bound digest-only email action token persistence and migration/indexes.
- [x] Make registration transactional and stop issuing a session before verification.
- [x] Gate login and refresh for new unverified accounts.
- [x] Implement atomic verify/reset consumption, cooldown, password reset, refresh revocation, and safe audits.

## API and email

- [x] Add verification, resend, forgot, and reset request/response contracts and endpoints.
- [x] Add public IP throttling and persisted email/account limits.
- [x] Add shared sender contract, validated SMTP adapter/options, and RTL HTML/text templates.
- [x] Configure trusted HTTPS frontend origin and document secrets/environment settings.

## Frontend

- [x] Update registration to check-email state without applying a session.
- [x] Add verification, forgot-password, and reset-password routes/forms.
- [x] Keep tokens in URL fragments and strip them from browser location after read.

## Tests and final review

- [x] Add focused Identity/service/API/email frontend tests using fake sender and database test conventions.
- [x] Run relevant tests and full solution suite; report environmental blockers distinctly.
- [x] Perform implementation review against acceptance criteria and write `ImplementationReport.md`.

## SMTP local and production configuration follow-up

- [x] Keep shared email defaults and non-secret SMTP defaults in `appsettings.json`; leave the SMTP password out.
- [x] Preserve tracked `appsettings.Development.json` and load an ignored Development-local overlay without changing ASP.NET Core environment/CLI precedence.
- [x] Add all local SMTP and frontend settings to the overlay template; keep it out of Git and API publish output.
- [x] Add production SMTP placeholders to `.env.example` for manual VPS transfer; leave the populated local `.env` and production server untouched.
- [x] Map production email/SMTP environment variables to `Authentication:Email` and `Authentication:Smtp` for the API service in Docker Compose.
- [x] Validate Development loading, environment-variable precedence, options binding, production Compose mappings, backend build, and focused auth tests.
- [x] Populate the ignored Development-local overlay with the operator-approved Gmail SMTP values, enable Development delivery, detect the Vite URL (`http://localhost:5173`), and confirm configuration values without exposing the app password. No message was sent.
- [ ] Populate production `.env` with approved production credentials during the manual VPS configuration process.
