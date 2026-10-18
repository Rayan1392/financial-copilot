# Feature 139 — Task Review (single bounded pass)

Checks: dependency ordering, missing tasks, duplicate responsibilities, test coverage, external blockers, incorrect ownership, AC traceability.

| ID | Severity | Finding | Disposition |
| --- | --- | --- | --- |
| TR-01 | MAJOR | A TahlilApp test double could leak into production as a fallback (prompt §22: no mock identities or permissions in production). | Double lives only in `tests/FinancialCopilot.IntegrationTests` and the unit test project. Production DI binds only `TahlilAppIdentityGateway`; `ValidateOnStart` fails when `Enabled` and the base URL is missing. **Fixed in T-07/T-29.** |
| TR-02 | MAJOR | T-11 depends on the migration (T-10), but the first-launch test needs the null-email decision (F-03). If the default validator rejects null email, T-11 changes shape. | Ordering kept; T-11 has an explicit proof test before the provisioner is finalised. **Fixed by ordering.** |
| TR-03 | MAJOR | Launch endpoint (T-09) needs refresh-token revocation and session creation (T-15), but T-15 depends on T-13 (null email). Missing edge. | Dependency list in T-09 already includes T-15; T-15 includes T-13. **Verified ordering.** |
| TR-04 | MINOR | Concurrent first-launch race (AC-28) cannot be proven on PostgreSQL here (Testcontainers unavailable in this environment). | Recorded in T-11 as best-effort on SQLite and as an operational verification item. |
| TR-05 | MINOR | T-17 (switch-account confirmation) and T-22 (direct-login TTL) are both frontend-or-login work deferred to a follow-up. | Both kept as explicit deferrals with reasons; not duplicated. |
| TR-06 | MAJOR | Billing guard (T-19) changes behaviour for admin-set windows that have lapsed, across all accounts, not only TahlilApp users. | Intended by D3 and AC-87; the behaviour change is listed in the implementation report and in a test. **Recorded.** |
| TR-07 | MINOR | T-06 (backup and restore) is Operations-owned and cannot be verified here. | Explicitly `Operations` and not claimed. |
| TR-08 | MINOR | AC-54 (native login honours lockout and rate limit) has no task in this increment because native enrollment is deferred. | Recorded as not verified; not claimed. |

## Ownership and external blockers

- TahlilApp: S-2 and S-3 are external. The gateway implements the proposed contract only; production verification requires TahlilApp's contract confirmation (Q1).
- Operations: T-06, proxy topology (F-09), secret rotation (F-10), legal basis (Q13).
- No task marks an external item as done.

## Traceability

Every implemented task names its ACs. ACs not covered by an implemented task in this increment: AC-01–AC-04 (external), AC-05/06 TahlilApp half (external), AC-19–AC-23 partially (AC-22/AC-23 verified by code inspection; AC-19 verified by log test), AC-44–AC-53 (native credentials, deferred T-26/T-27), AC-54, AC-55 (logout re-launch: verified by T-29 scenario), AC-66 (verified by absence of index/unique in migration review), AC-70–AC-71 (deferred T-24), AC-76–AC-77 (T-12/T-14 partially), AC-78–AC-81 (T-05 for AC-78/AC-80; AC-79/AC-81 operational T-06).

**Verdict: PASS.** No open MAJOR or BLOCKER remains in the task plan. TR-01–TR-02 are fixed by ordering and test placement; TR-06 is recorded as an intended behaviour change.
