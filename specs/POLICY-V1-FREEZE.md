# Policy: V1 Orchestration Freeze — V2-Only Feature Development

**Effective date:** 2026-09-21
**Approved by:** product decision, recorded via agent instruction

## Why

- Feature `047` introduced MAF V2 orchestration alongside legacy V1.
- Feature `056` replaced the manual V2 chain with native Microsoft Agent Framework Workflow primitives.
- Feature `122` is migrating remaining legacy routes off V1.

Several coherence rules across `specs/README.md` have required "V1 rollback and native MAF V2 must
share capability, slot, entity, outcome, and Billing semantics" and preservation of a V1/V2 config
toggle. Maintaining full behavioral parity between V1 and V2 for every new capability has repeatedly
cost double implementation, double testing, and double review effort for a path (V1) that is not the
delivery target. That parity requirement is revoked for new work as of this policy: V1 is frozen as a
historical fallback only, not a target for parity or new capability.

## Policy

1. No new feature, capability, metric, intent, or route may be implemented in the V1 orchestration
   path. Every new AI-facing capability ships on V2 (native MAF Workflow, Feature `056`) only.
2. No behavioral changes to V1 except:
   a. Security patches.
   b. Billing/accounting correctness fixes (money must never be wrong).
   c. Changes required because shared code (Domain/Application services consumed by both V1 and V2,
      e.g. `IScannerQueryParser`, `IExplainableAnswerBuilder`, `IBillingFacadeHook`) changed for a V2
      reason and V1 would otherwise fail to compile/run.
3. The `AiOrchestrationVersion` / V1↔V2 config toggle is kept ONLY for emergency rollback of the
   CURRENT feature set already migrated. It must not be treated as an ongoing parity contract for
   anything shipped after this policy date.
4. Any coherence rule, spec line, or checklist entry that currently implies "V1 must gain the same
   capability as V2" must be corrected to say V1 is frozen and V2-only is the delivery target,
   without deleting the historical record of why V1 existed.
5. If a task/spec is ambiguous about which path to target, default to V2 and flag the ambiguity
   instead of silently also touching V1.
6. This is a freeze, not a removal. Do not delete V1 code because of this policy. Removal/sunset of
   V1 is a separate future decision and out of scope here.

## What this does NOT mean

- V1 keeps running in production; it is not being shut off.
- Existing V1 tests still must pass.
- Security and Billing-correctness fixes still apply to V1 when needed.
- This is not a deletion or sunset of V1 code.

## How to detect a violation (run before merging)

- Does this PR add a new intent, capability, metric, or route to any V1-only class (e.g. an intent
  branch under `LlmAiIntentDetector`, a V1-only use case wiring)? If yes, reject or move the work to
  the V2 MAF Workflow path instead.
- Does this PR change V1 behavior for a reason other than (a) security, (b) Billing/accounting
  correctness, or (c) keeping V1 compiling/running against shared code that changed for V2? If yes,
  reject or scope the change down to V2 only.
- Does this PR treat the `AiOrchestrationVersion` toggle as a place to add new parity behavior rather
  than an emergency-rollback switch for already-migrated features? If yes, reject.
- Does this PR delete V1 code as part of implementing this policy? If yes, stop — that is out of
  scope; removal is a separate future decision.
