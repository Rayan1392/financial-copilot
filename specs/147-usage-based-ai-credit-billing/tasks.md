# Feature 147 — Tasks

## Implementation

- [x] **T01** Inspect the existing reservation, finalization, refund, ledger, usage accumulator, provider usage reporting, MAF V2 / Feature 141 billing paths and duplicate-charge guards (findings in `spec.md`).
- [x] **T02** Add the domain policy and calculator in the Billing context: `UsageBasedCreditPolicy`, `ModelTokenPrice`, `ModelCallUsage`, `UsageBasedCreditCalculator` (decimal, per-million prices, cached tokens billed once, integer ceiling, minimum, fallback, cap).
- [x] **T03** Add `Billing:UsageBasedCredits` options with `Legacy`/`Shadow`/`Enforced` modes, operation-code scope, versioned policy and startup validation. Default to `Legacy`.
- [x] **T04** Parse provider-reported cached input tokens (OpenAI `input_tokens_details.cached_tokens`, DeepSeek `prompt_cache_hit_tokens`) into `AiExecutionUsageFacts.CachedInputTokens`.
- [x] **T05** Extend the usage accumulator with idempotent `ClaimUnbilled`, a bounded model-key summary and 30-minute eviction.
- [x] **T06** In `AiFacadeBillingHook`, begin with the configured minimum hold in Enforced mode, atomically upgrade the same reservation before expensive execution, charge the measured whole-number usage amount only when covered, reuse the reservation on replay, and log/measure proposals in Shadow and Enforced modes.
- [x] **T07** Route the Feature 141 derived reservation's usage to the turn's correlation id (`UsageCorrelationId`).
- [x] **T08** Keep the chat response contract, persistence and displayed usage unchanged. Add an optional request idempotency key generated once per submitted question so transport retries reuse the same logical billing operation.

## Verification

- [x] **T09** Unit tests: low cost → 1, higher cost → multiple, multi-call aggregation, per-model prices, rounding (1.01 → 2), cached tokens, missing usage fallback, provider-reported cost, cap, multiplier, policy and option validation, accumulator claim/replay/eviction.
- [x] **T10** Integration tests on the registered billing composition cover V2 usage charging, progressive reservation upgrades and remainder release, refusal before an expensive path when funds are insufficient, retries and transient pre-commit persistence failure, missing usage fallback, zero-charge outcomes, one-charge Feature 141 fallback, out-of-scope operation pricing, and Legacy/Shadow isolation. PostgreSQL concurrency tests are included but could not run without Docker.
- [x] **T11** Focused regressions: billing persistence and endpoint tests pass; the architecture suite retains one documented baseline failure in `SemanticCapabilityExecutors.cs`. Focused suite results and the baseline comparison are recorded in `hardening-report.md`.
- [x] **T12** Calibration from measured Feature 141 token usage (`implementation-report.md`).

## Rollout (operations; not done by this change)

- [ ] **R1** Replace the illustrative `ModelPrices` with contract prices.
- [ ] **R2** Run `Shadow` on production traffic and review `billing.usage_based.proposed_credits` and the evaluation logs.
- [ ] **R3** Product Owner approves `CostPerCredit`, `PricingMultiplier` and the open decisions in `spec.md`.
- [ ] **R4** Switch to `Enforced`. Rollback is `Mode = Legacy` plus a restart.
