# Feature 139 — Design Review (single bounded pass)

Scope: identity correctness, LaunchCode security, service authentication, account-linking safety, email verification security, subscription/permission consistency, AI credit correctness, session and revocation, PII protection, transactional consistency, operational recoverability, implementability.

Reviewed: `Design.md` Revisions 1–4 (§22 is authoritative), the repository evidence listed in `Design.md` §22.1, and the Billing and frontend/infrastructure exploration reports.

Severity: BLOCKER, MAJOR, MINOR, NOTE. Only BLOCKER and MAJOR gate progression.

## Findings

| ID | Severity | Location | Problem | Impact | Required correction | Verification | Disposition |
| --- | --- | --- | --- | --- | --- | --- | --- |
| F-01 | MAJOR | `Design.md` §8, §18; `ServiceCollectionExtensions.cs:241` | Design says reuse Identity email token providers and `UserManager.ChangeEmailAsync`. `AddDefaultTokenProviders` is not registered. Feature 145 already ships a separate email-action-token mechanism. | Two token mechanisms = a parallel auth system; S-11 would be built on an unregistered provider. | Reuse the Feature 145 mechanism (D1, §22.2). | grep: no `AddDefaultTokenProviders` in `src/backend`. | **Fixed in Design §22.2 D1.** |
| F-02 | MAJOR | `Design.md` §11, §17; `SubscriptionPlanRepository`, `PlanCapabilityService`, `WalletEntitlementService`, `SubscriptionService` | No read path honours `SubscriptionEffectiveTo` (billing report §2). Expired paid access persists until an admin edits the plan. | Violates AC-35; a stopped worker extends paid access indefinitely. | Read-side guard: effective plan = `Free` when `EffectiveTo <= now` (D3). | Billing report §2; grep of `SubscriptionEffectiveTo` writers only. | **Fixed in Design D3; implementation in S-8.** |
| F-03 | MAJOR | `Design.md` §10.1 step 1; `ServiceCollectionExtensions.cs:241` | Provisioning creates a user with `Email = null` under `UserManager.CreateAsync`. With `RequireUniqueEmail = true` the default validator may reject a null email. | Launch fails for every first-time TahlilApp user if the validator rejects it. | Prove with a test. If rejected, write the user row via `AuthDbContext` with explicit normalized name and security stamp, and cover it with a test. Never write a placeholder email. | Integration test `TahlilAppLaunch_FirstLaunch_CreatesOneUserWithoutEmail`. | **Deferred to implementation with mandatory test (D5).** |
| F-04 | MAJOR | `Design.md` §14 retry; `FinancialProviderResilienceHandler.cs:24-67` | The repository's only resilience handler retries 5xx. A default typed client would retry the exchange, which is non-idempotent. | A retry after a lost response returns `invalid` and reports a false failure; a retry also doubles outbound load. | The TahlilApp exchange client must **not** attach `FinancialProviderResilienceHandler`. Retry none. | Unit test: exchange handler invoked exactly once on 5xx. | **Fixed in D4; implementation constraint.** |
| F-05 | MAJOR | `Design.md` §13.2 vs §8 (attempt row) | §13.2 says a retry within TTL is possible, but the local unique attempt row blocks any second call. | Contradictory behaviour for operators and tests. | Attempt is final for a code; relaunch to obtain a new code (D4). AC-83 added. | Test: a second request with the same code, after a 5xx, returns `launch-invalid` and makes no exchange call. | **Fixed in D4 and AC-82/AC-83.** |
| F-06 | MAJOR | `OwnedIdentityService.cs:461`, `:483` | `CreateSessionAsync` and `CreateAccessToken` dereference `user.Email!`. An email-less TahlilApp user throws `ArgumentNullException` when a `Claim` is built. | No session for any TahlilApp-provisioned user without email (AC-30). | Omit the email claim when null; make `OwnedIdentityProfile.Email` nullable (implementation requirement, §22.2 D10 context). | Unit test: `CreateSessionForUserAsync` with null email yields a JWT without an `email` claim. | **Implementation required (S-6/S-7).** |
| F-07 | MAJOR | `Design.md` §7, §12 | Session fixation: launch must revoke a presented refresh cookie and issue a new refresh token. The design text is present but the endpoint and cookie handling did not exist. | An attacker-supplied session survives a victim's launch. | Revoke the presented token with reason `Replaced by TahlilApp launch.` (D13, AC-84). | Integration test: a launch with an existing cookie revokes the old row and sets a new cookie. | **Implementation required (S-7).** |
| F-08 | MAJOR | `ServiceCollectionExtensions.cs`, `Program.cs` (grep) | `AddDataProtection` is absent. Without a persistent ring, PII encrypted with the default ephemeral ring becomes unreadable after a restart. | AC-78 fails silently. | `AddDataProtection().SetApplicationName("FinancialCopilot").PersistKeysToFileSystem(KeyRingPath)`; fail startup when the feature is enabled and the path is missing or unwritable (D12). | Test: startup throws when enabled without a path; restart test: a value encrypted by one provider decrypts with a second provider sharing the ring. | **Implementation required (S-5/S-6).** |
| F-09 | MAJOR | `UseForwardedHeaders` absent (grep); `RateLimitPolicies.cs:12-21` | The public limiter partitions by `RemoteIpAddress`. Behind a reverse proxy every client may share one address, and the launch limiter would throttle all users together (or nobody). | Availability and brute-force protection both degrade (Q12). | Configure `UseForwardedHeaders` with known proxy addresses before enabling the flag. | Operational checklist; not a code change for a flag-off release. | **Operational prerequisite (§22.4); blocks enabling the flag in production.** |
| F-10 | MAJOR (pre-existing, out of scope) | `FinancialCopilot.API/appsettings.json` and `appsettings.Development.json` | A non-empty `SigningKey` is committed in both files (verified by redacted grep). Development also carries database and SMTP credentials (reported, not reproduced here). | Anyone with repository access can forge JWTs for any user, including launch sessions. | Rotate the signing key and every credential, and move them to the secret store. | Redacted grep: `"SigningKey" : "<non-empty>"` present in both files. | **Escalated to the user as a production blocker. Not changed here (secret rotation is an operator action and the files are outside this feature).** |
| F-11 | MAJOR (pre-existing, out of scope) | `src/frontend/src/routes/auth.tsx:6-11` (verified) | `redirect` validation accepts `/\host`: it starts with `/` and not `//`, but browsers normalise `\` to `/`, producing a protocol-relative redirect. Used at sign-in (`window.location.assign`). | Open redirect after login. | Reject any value whose second character is `/` or `\`, or parse with `new URL(value, origin)` and require the same origin. | Manual test with `/\evil.example`. | **Escalated to the user. Not changed (unrelated route).** |
| F-12 | MAJOR | `Design.md` §9.2 `POST credentials/email` | "The verification mail is not sent to a different account's detriment" is ambiguous. | An implementer could send a verification to the other account's inbox. | State explicitly: when the address is already owned, send nothing, persist nothing, return the same 202 (Diagram 3, Diagram 6). | Covered by S-11 test when implemented. | **Clarified in §22.6 Diagram 3 and 6; S-11 deferred.** |
| F-13 | MINOR | `Design.md` §10.2 | The PII storage table lists `NationalIdLast4` and `MobileLast4` as optional. | Plain last-4 digits are low-sensitivity but still PII; they must never be logged. | Keep as designed; log only field names. | Log-sink test (AC-72). | **Accepted.** |
| F-14 | MINOR | `EfCoreAdminManagementService.cs:342-348` (billing report §3) | Admin `SetSubscriptionAsync` accepts a null `PlanCode`, which leaves an account unassigned and skips capability gating. No idempotency key. | Pre-existing admin gap, not caused by this feature. | Out of scope; record for the Billing owner. | Billing report §3. | **Recorded in ImplementationReport follow-ups.** |
| F-15 | NOTE | Billing catalog (billing report §1) | Free grants 10,000 included credits while Pro grants 100. | Product anomaly; sync logic does not depend on the quantities, but paid activation with included credits must not be enabled until product confirms the numbers. | Product confirmation before any paid-credit change. | Catalog migration review. | **Noted; no catalog change made (prompt §12).** |
| F-16 | NOTE | `OwnedIdentityBillingProvisioner.cs:44-58`, `OwnedIdentityService.cs:434` | Billing provisioning runs after the identity transaction and in a separate context. A crash between them leaves a user without a wallet until the next session creation, which re-runs provisioning. | Self-healing; a short window with no wallet. | Accept; the existing `EnsureProvisioned` on every session creation covers it. | Test: a session creation after a missing wallet creates it exactly once. | **Accepted.** |
| F-17 | NOTE | `Design.md` §13.2 / Q8 | Access JWT lifetime is 7 days; logout does not end an issued access token. | Pre-existing debt; no entitlement or balance is in the token. | Post-release (Q8). | — | **Accepted and documented.** |

## Gate decision

- Open BLOCKER findings: **none in the code path**. F-10 is a BLOCKER for **production enablement** because it compromises every session. It is an operator action outside this repository's code and is escalated; it does not block implementing and testing the feature behind a disabled flag.
- Open MAJOR findings: F-01, F-02, F-03, F-04, F-05, F-06, F-07, F-08, F-09, F-10, F-11, F-12. All have a corrected design or an implementation requirement with a verification method. F-09, F-10 and F-11 are production gates; F-03 is confirmed by test during implementation.
- Iterations used: **one review pass and one fix pass** (design §22 is the fix). No second review is required because no further design changes are planned beyond the implementation constraints listed above.

**Verdict: PASS_WITH_PRODUCTION_GATES.** Progression to task breakdown and implementation is allowed. The feature must not be enabled in any environment until F-08, F-09, F-10, F-11 and Q13 are closed.

## Post-implementation disposition

| ID | Result |
| --- | --- |
| F-01 | Closed in design (§22.2 D1). S-11 is deferred, so the Feature 145 mechanism is not yet used for enrollment. |
| F-02 | Implemented. Billing expiry guard in `PlanCapabilityService` and `SubscriptionPlanRepository`. Capability-loss test; repository path untested directly. |
| F-03 | **Proven by test and resolved.** Identity rejects a null email (`InvalidEmail`). The row is written directly with explicit normalised fields and no placeholder email. Design §22.9 I-1. |
| F-04 | Implemented. The gateway client has no resilience handler; tests show one send for every failure class. |
| F-05 | Implemented. A failed attempt is final for that code; tested. |
| F-06 | Implemented. Email-less session and JWT tested; `/me` returns an empty email. |
| F-07 | Implemented. Presented refresh token revoked on launch; tested. |
| F-08 | Implemented. Fixed application name and persistent ring; startup validation fails on a missing or unwritable ring. The backup and restore drill remain open (T-06). |
| F-09 | **Open, operational gate.** `UseForwardedHeaders` not configured. |
| F-10 | **Open, production blocker, escalated.** Committed signing key and credentials. Not changed here. |
| F-11 | **Open, pre-existing, escalated.** `/auth` redirect validation accepts `/\host`. Not changed here. |
| F-12 | Deferred with S-11. |
| F-13 | Not changed (acceptable). Last-4 values are not logged. |
| F-14 | Recorded for the Billing owner. Not changed. |
| F-15 | Recorded. Catalog not changed. |
| F-16 | Accepted. Existing session-time provisioning recovers a missing wallet. |
| F-17 | Recorded as post-release debt (Q8). |

Gate status: F-09, F-10, F-11 and Q13 remain open, so the feature must stay disabled. F-08 is implemented, but its operational half (T-06) is still open. No new BLOCKER or MAJOR was found during implementation other than F-03, which was resolved in code and proven by test.
