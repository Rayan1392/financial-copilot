# Feature 139 — User Stories

Product name: **دستیار هوشمند بازار سرمایه** (internal namespaces stay `FinancialCopilot`). Acceptance criteria are defined in `Design.md` §19 and refined in §22.8; this file maps stories to them. Status reflects `ImplementationReport.md`.

## Launch from TahlilApp

**US-139-01 — As a signed-in TahlilApp user, I enter Sapino without a second login.**
ACs: AC-07, AC-09, AC-13, AC-14, AC-15, AC-40, AC-41. Status: **implemented behind the feature flag (off by default)**; the TahlilApp side of the exchange is external and unverified.

**US-139-02 — As a user who has already launched once, I get the same Sapino account every time.**
ACs: AC-10, AC-11, AC-27–AC-29, AC-28. Status: implemented and tested (second launch resolves the same user; concurrent identical launches create one user). Concurrent launches with *different* codes are covered only by the unique-index logic, not by a test.

**US-139-03 — As a user who opens a launch link twice, the second attempt is refused without contacting TahlilApp.**
ACs: AC-05, AC-16, AC-17, AC-82. Status: implemented and tested.

**US-139-04 — As a user whose launch failed, I see a clear message and I am not redirected in a loop.**
ACs: AC-16, AC-18, AC-20, AC-40. Status: implemented (`/launch-failed`, closed reason enum); the "return to TahlilApp" button is not rendered (no configured return URL).

**US-139-05 — As a suspended or disabled user, I am refused and receive no session.**
ACs: AC-31. Status: implemented and tested (suspended → 403, no cookie).

**US-139-06 — As a user who launches again in the same browser, my previous Sapino session is replaced.**
ACs: AC-39, AC-84. Status: implemented and tested (presented refresh token revoked; new token issued). The switch-account confirmation prompt is **not** implemented.

## Profile and personal data

**US-139-07 — As a user without an email address, I can still use Sapino.**
ACs: AC-30, AC-73. Status: implemented and tested (JWT omits `email`; `/me` returns an empty email; launch and session succeed).

**US-139-08 — As a user, my name, national id and mobile come from TahlilApp and stay current when I launch.**
ACs: AC-57–AC-64. Status: implemented and tested (encrypted at rest; absent or null values keep stored data; a changed valid value overwrites).

**US-139-09 — As a user viewing my profile, my national id and mobile are masked by default.**
ACs: AC-68, AC-74. Status: implemented and tested (`GET /api/auth/v1/profile`, `no-store`).

**US-139-10 — As a user, I can reveal my own full national id or mobile on request, and no one else can.**
ACs: AC-69. Status: implemented and tested (self-only reveal; no user id parameter exists). Admin reveal (AC-70, AC-71) is **deferred**.

**US-139-11 — As an operator, personal data never appears in logs, tokens, or AI context.**
ACs: AC-72, AC-73, AC-74. Status: partly verified. Token claims are tested; logs are redacted by code (`ToString` overrides, field names only) but **no log-sink test exists yet**.

**US-139-12 — As a user, I am never matched to another account by my national id or mobile.**
ACs: AC-66, AC-67. Status: implemented by design (no index, no unique constraint, no lookup path). Not separately tested.

## Subscription and credits

**US-139-13 — As a paid TahlilApp subscriber, my plan applies in Sapino until it expires.**
ACs: AC-12, AC-20 (T-20), AC-86. Status: implemented and tested (active paid plan with a future expiry is synced with that window).

**US-139-14 — As a lapsed subscriber, I drop to the Free plan and keep access.**
ACs: AC-35, AC-36, AC-87. Status: implemented and tested (sync writes Free; the Billing read path treats a lapsed window as Free). **Behaviour change for all accounts with a lapsed admin-set window.**

**US-139-15 — As a user who launches or logs in repeatedly, my credits are never topped up by that.**
ACs: AC-38. Status: implemented and tested (repeated identical sync: no revision bump, no audit row, wallet balance unchanged; sync grants no credits).

**US-139-16 — As a user with an unknown or suspicious plan name, I am never granted a higher plan.**
ACs: AC-37. Status: implemented and tested (unknown → Free; inactive → Free; paid without expiry → Free).

**US-139-17 — As a product owner, TahlilApp permission names never grant features in v1.**
ACs: AC-85. Status: implemented (`permissions[]` is counted and logged only).

## Email and native login (not in this increment)

**US-139-18 — As a TahlilApp user, I can add an email address and password to sign in directly.**
ACs: AC-44–AC-53. Status: **not implemented** (`T-26`, `T-27`). Designed in `Design.md` §22.6 Diagram 3, reusing the Feature 145 token mechanism. Until this is delivered, TahlilApp-only users cannot sign in without TahlilApp.

**US-139-19 — As a user, an email address already used by another account never merges accounts.**
ACs: AC-51, AC-67. Status: prohibited by design; enforced by the absence of any merge code. Not yet exercised by an enrollment test because enrollment is deferred.

## Operations and rollout

**US-139-20 — As an operator, I can enable the feature only where the key ring is durable.**
ACs: AC-78, AC-80. Status: implemented (startup fails if enabled without a writable key ring). Backup and restore drill (AC-79, AC-81) are **operational and not done** (`T-06`).

**US-139-21 — As an operator, I can turn the feature off instantly.**
ACs: §18 feature flag. Status: implemented (`404` when disabled; covered by test).
