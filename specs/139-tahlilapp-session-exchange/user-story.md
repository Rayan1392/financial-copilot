# Feature 139 — User Stories

## Revision 8 — Temporary Closed Registration policy

Public self-registration is disabled. Previously provisioned users retain direct email/password login and password recovery, and Feature 145 email verification plus credential enrollment remains available to externally provisioned users. New users enter through trusted external identity providers; TahlilApp is the first provider. Provider-to-external-user identity mapping remains provider-neutral. Native public registration may be enabled later only as a separate product decision.

Product name: **SapioAI** (internal namespaces and API contracts stay `FinancialCopilot`). Acceptance criteria are defined in `Design.md` §19 and refined in §22.8; this file maps stories to them. Status reflects `ImplementationReport.md`.

## Launch from TahlilApp

**US-139-01 — As a signed-in TahlilApp user, I enter SapioAI without a second login.**
ACs: AC-07, AC-09, AC-13, AC-14, AC-15, AC-40, AC-41. Status: **implemented behind the feature flag (off by default)**; the TahlilApp side of the exchange is external and unverified.

**US-139-02 — As a user who has already launched once, I get the same SapioAI account every time.**
ACs: AC-10, AC-11, AC-27–AC-29. Status: implemented and tested (second launch resolves the same user; concurrent identical codes create one user; concurrent different codes for one external identity also resolve to one local user).

**US-139-03 — As a user who opens a launch link twice, the second attempt is refused without contacting TahlilApp.**
ACs: AC-05, AC-16, AC-17, AC-82. Status: implemented and tested.

**US-139-04 — As a user whose launch failed, I see a clear message and I am not redirected in a loop.**
ACs: AC-16, AC-18, AC-20, AC-40. Status: implemented (`/launch-failed`, closed reason enum, no automatic redirect); the user can return to TahlilApp through a link when the validated `VITE_TAHLILAPP_RETURN_URL` is configured.

**US-139-05 — As a suspended or disabled user, I am refused and receive no session.**
ACs: AC-31. Status: implemented and tested (suspended → 403, no cookie).

**US-139-06 — As a user who launches again in the same browser, my previous SapioAI session is replaced.**
ACs: AC-39, AC-84. Status: implemented and tested (a session confirmation appears before exchange; cancel returns to `/chat`; confirmation exchanges once, revokes the presented refresh token, and issues a new token).

## Profile and personal data

**US-139-07 — As a user without an email address, I can still use SapioAI.**
ACs: AC-30, AC-73. Status: implemented and tested (JWT omits `email`; `/me` returns an empty email; launch and session succeed).

**US-139-08 — As a user, my name, national id and mobile come from TahlilApp and stay current when I launch.**
ACs: AC-57–AC-64. Status: implemented and tested (encrypted at rest; absent or null values keep stored data; a changed valid value overwrites).

**US-139-09 — As a user viewing my profile, my national id and mobile are masked by default.**
ACs: AC-68, AC-74. Status: implemented and tested (`GET /api/auth/v1/profile`, `no-store`).

**US-139-10 — As a user, I can reveal my own full national id or mobile on request, and no one else can.**
ACs: AC-69. Status: implemented and tested (self-only reveal; no user id parameter exists). Admin reveal (AC-70, AC-71) is **deferred**.

**US-139-11 — As an operator, personal data never appears in logs, tokens, or AI context.**
ACs: AC-72, AC-73, AC-74. Status: verified by token-claim checks and an actual API log-sink integration test using unique first name, last name, national ID and mobile values; none appear in captured application log messages.

**US-139-12 — As a user, I am never matched to another account by my national id or mobile.**
ACs: AC-66, AC-67. Status: implemented by design (no index, no unique constraint, no lookup path). Not separately tested.

## Subscription and credits

**US-139-13 — As a paid TahlilApp subscriber, my plan applies in SapioAI until it expires.**
ACs: AC-12, AC-20 (T-20), AC-86. Status: implemented and tested (active paid plan with a future expiry is synced with that window).

**US-139-14 — As a lapsed subscriber, I drop to the Free plan and keep access.**
ACs: AC-35, AC-36, AC-87. Status: implemented and tested (sync writes Free; the Billing read path treats a lapsed window as Free). **Behaviour change for all accounts with a lapsed admin-set window.**

**US-139-15 — As a user who launches or logs in repeatedly, my credits are never topped up by that.**
ACs: AC-38, AC-95. Status: implemented and tested for repeated login/status events and same-process worker retries; the database has unique period/grant keys and revision tokens. New eligible periods receive one grant; identical syncs and login do not refill an existing period. Cross-process PostgreSQL race verification remains open.

**US-139-18 — As a SapioAI user, my monthly AI allowance is available for one 30-day period and unused credits expire.**
ACs: AC-88…AC-91, AC-94. Status: implemented and unit-tested using the existing Billing wallet and append-only ledger. Initial grants are Free `25`, Plus `260`, Pro `2600`; renewal has no rollover; Free→paid forfeits tracked remaining Free credits and starts a full paid allowance. A paid period closes at its 30-day boundary and Free starts at that boundary after expiry. Existing production wallet balances are held for reconciliation rather than being reset or automatically granted against.

**US-139-19 — As a Product owner, paid-plan transitions follow approved credit rules.**
ACs: AC-92, AC-93. Status: Pro→Plus is rejected. Plus→Pro external eligibility synchronizes but neither transfers the remaining Plus balance nor grants Pro credits until Product approves the proposed balance and period semantics. Staging remains gated on that decision.

**US-139-16 — As a user with an unknown or suspicious plan name, I am never granted a higher plan.**
ACs: AC-37. Status: implemented and tested (unknown → Free; inactive → Free; paid without expiry → Free).

**US-139-17 — As a product owner, TahlilApp permission names never grant features in v1.**
ACs: AC-85. Status: implemented (`permissions[]` is counted and logged only).

## Email and native login

**US-139-18 — As a TahlilApp user, I can add an email address and password to sign in directly.**
ACs: AC-44–AC-53. Status: implemented and tested (`T-26`, `T-27`) through the existing Identity email-token flow. A TahlilApp-provisioned user can enroll and verify an email, set a password, and later sign in directly; the same local user, conversations and Billing account are retained.

**US-139-19 — As a user, an email address already used by another account never merges accounts.**
ACs: AC-51, AC-67. Status: prohibited by design and exercised by native enrollment tests; an existing email collision does not merge accounts.

## Operations and rollout

**US-139-20 — As an operator, I can enable the feature only where the key ring is durable.**
ACs: AC-78, AC-80. Status: implemented (startup fails if enabled without a writable key ring). Backup and restore drill (AC-79, AC-81) are **operational and not done** (`T-06`).

**US-139-21 — As an operator, I can turn the feature off instantly.**
ACs: §18 feature flag. Status: implemented (`404` when disabled; covered by test).

**US-139-22 — As a user whose TahlilApp identity is revoked, my profile data is removed and I cannot launch again.**
ACs: AC-31, AC-77. Status: implemented and tested; revocation deletes the encrypted profile in the same Auth save while retaining the external-identity denial tombstone. Local-user deletion cascades to the external identity and profile.

## Verification scope

SapioAI-owned automated coverage is reported separately from external service and operational acceptance. Focused Feature 139 suites pass; the full regression has unrelated failures and does not establish staging readiness. TahlilApp launch issuance/exchange/status behavior, approved permission codes, database migration execution, durable key-ring restore, proxy/SMTP setup, and real end-to-end round trips remain unverified.

## Revision 6 requirements

- AC-96: Active paid periods use TahlilApp's UTC `effectiveFrom`. Legacy payloads remain accepted for eligibility but never create a paid grant using SapioAI observation time.
- AC-97: Plus→Pro transfers only unused Plus credits into the new Pro period alongside 2600 new Pro credits, through paired auditable ledger operations; repeated or concurrent events transfer once and the transfer expires with that period.
- AC-98: Stale and conflicting equal subscription revisions cannot overwrite newer subscription state; concurrent account transitions cannot duplicate grants.
- AC-99: Missing, malformed, non-UTC, future or post-expiry timestamps cannot create paid periods. Late launch, renewal, Free→paid, expiry and prohibited Pro→Plus behavior remain deterministic.
## Revision 7 — Official TahlilApp contract and approved independent subscriptions

This revision supersedes conflicting assumptions in Revisions 1–6. Those sections remain as implementation history only.

The confirmed API uses HTTP Basic Auth with client ID `financial-copilot` and an environment supplied secret. The base is `https://api.tahlilapp.com/api`; exchange is `POST /partners/exchange` with `{ "code": "..." }`; status is `GET /partners/users/{user_id}/status`. Responses identify the account by root `user_id`, allow nullable `name`, and contain `subscription.plans[]` entries with `slug`, `starts_at`, and `expires_at`. Supported slugs are `sapio_plus` and `sapio_pro`. The contract does not supply `effectiveFrom`, subscription IDs, or revisions. Prior `effectiveFrom` and revision requirements are superseded. The new `starts_at` field is pending external contract delivery; no historical activation time may be inferred.

Each paid plan is an independent subscription. Plus and Pro can be active together; the effective plan is Pro whenever Pro is active, otherwise Plus when Plus is active, otherwise Free. Both valid paid purchases contribute grants. A Pro activation transfers only remaining eligible Plus credits, paired in the append-only ledger; consumed Plus credits are never granted again. Each plan-period grant is idempotent by account plus `slug + starts_at + expires_at` identity and 30-day period number. Consumption is attributed to source periods and uses earliest-expiry-first ordering. The wallet remains an aggregate projection; source period rows, grant references, charge allocations, refunds, expiry, and transfer ledger rows preserve accounting provenance. No migration rewrites historical wallet or ledger entries.

Paid access is computed from the active per-plan windows, independently from credit accounting. The last paid subscription expires at the greatest authoritative `expires_at` among the user's paid subscriptions when no paid window remains active. At that timestamp effective plan becomes Free and the 30-day Free-credit cooldown begins, even if SapioAI observes expiry later. The durable cooldown row is separate from authentication and plan state; repeat login/status sync cannot extend it. Reservation authorization denies AI usage during cooldown even if a balance remains. Authentication stays available. Reactivation permits paid reservations immediately; a later last-paid expiry uses the later authoritative expiry. Tracked Free credits are forfeited on paid activation; unexpired paid balances remain held during cooldown and expire by their own source period. No Free grant occurs during cooldown. Free allocation resumes after it, with a durable period identity to prevent duplicate historical grants.

Implementation adds per-plan subscription state, credit-period identity, immutable usage-source allocations, and cooldown persistence. The EF migration is `20261028104343_Feature139OfficialPlanSubscriptionsAndCooldown`; it has not been applied. A legacy period-key backfill preserves existing period uniqueness without altering wallet totals or ledger history. Existing legacy wallets still require a separately approved reconciliation before new subscription grants can be attributed.

Verification: focused Feature 139/Billing tests pass (121 unit tests; 20 integration tests pass and one PostgreSQL concurrency test is skipped because its database infrastructure is unavailable). The backend Release solution build passes with zero warnings and errors. The full unit suite has 20 unrelated failures: 18 match failures in the saved baseline TRX and two are in Nadpco/Telegram tests; details are in `ImplementationReport.md`. No production API call, deployment, or database migration was performed. TahlilApp delivery of `starts_at`, joint staging exchange/status verification, migration application, and operational readiness remain unverified.

## Revision 7 product acceptance criteria

This revision supersedes conflicting earlier assumptions; task implementation statuses are maintained in `tasks.md`.

- **AC-100:** Use the confirmed Basic Auth exchange/status endpoints and root `user_id`, nullable `name`, and `subscription.plans[]` (`slug`, `starts_at`, `expires_at`). `starts_at` delivery is pending; do not fabricate history or require undeployed `effectiveFrom`, revision, or IDs.
- **AC-101:** Plus and Pro are independent simultaneous subscriptions. Pro wins effective-plan selection while active; Plus remains effective when it alone is active. Each plan retains its own activation, expiry, renewal and credit source.
- **AC-102:** Allocate each eligible purchase once; include legitimate credits from both, apply Plus-to-Pro transfer only to unused Plus credit, and preserve append-only ledger and source attribution.
- **AC-103:** When all paid windows have expired, start a persistent 30-day cooldown at the greatest authoritative paid `expires_at`; authentication continues, but no AI-credit reservation succeeds.
- **AC-104:** Existing Free balances cannot bypass cooldown or be restored after it. No Free grant occurs during cooldown; keyed Free grants resume afterward. Paid reactivation permits paid use immediately.
