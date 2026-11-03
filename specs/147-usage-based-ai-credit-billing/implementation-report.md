# Feature 147 — Implementation Report

> **Historical snapshot:** this report records the initial Feature 147 implementation and its then-current hold sizing. The hardening pass changed reservation upgrades, Feature 141 fallback settlement, and operation idempotency. For the current behavior, test evidence, and readiness verdict, see [hardening-report.md](hardening-report.md).

**Verdict:** `USAGE_BASED_BILLING_IMPLEMENTED`. The default mode is `Legacy`, so production behavior is unchanged. Enforced mode is **not** production-approved: the shipped token prices are placeholders, and the pricing decisions in `spec.md` are still open.

## What changed

| Layer | File | Change |
|---|---|---|
| Billing (domain) | `FinancialCopilot.Billing/Pricing/UsageBasedCreditPolicy.cs` | New: billing mode, per-model token prices, validated policy, model-call usage, charge result. |
| Billing (domain) | `FinancialCopilot.Billing/Services/UsageBasedCreditCalculator.cs` | New: deterministic decimal cost → integer credits (cached tokens, ceiling, minimum, fallback, cap). |
| Application | `AI/ModelProviders/AiModelContracts.cs` | `AiExecutionUsageFacts.CachedInputTokens`; `IAiExecutionUsageAccumulator.ClaimUnbilled`; `AiExecutionUsageSummary.From`. |
| Application | `AI/Orchestration/AiOrchestrationContracts.cs` | `BillingReservationRequest.UsageCorrelationId`; `BillingReservationHandle.ReservedCredits` and `UsageCorrelationId`. |
| Infrastructure | `Billing/UsageBasedCreditBillingOptions.cs` | New: `Billing:UsageBasedCredits` options, mapped to the domain policy and validated at startup. |
| Infrastructure | `Billing/AiFacadeBillingHook.cs` | Enforced hold sizing, usage-based charge capped at the hold, replay reuse, Shadow/Enforced log and metric. |
| Infrastructure | `AI/ModelProviders/InMemoryAiExecutionUsageAccumulator.cs` | Per-call storage, idempotent claims, distinct model-key summary, 30-minute eviction (previously unbounded). |
| Infrastructure | `AI/ModelProviders/{OpenAi,DeepSeek}HostedAiModelTransport.cs`, `ConfiguredHostedAiModelClient.cs` | Parse provider-reported cached input tokens. |
| Infrastructure | `AI/OrchestrationV2/Functions/BillingFunctions.cs`, `Workflow/FinancialCopilotWorkflowDefinition.cs` | The Feature 141 derived reservation reads usage under the turn's correlation id. |
| Infrastructure | `ServiceCollectionExtensions.cs` | Options and calculator registration. |
| API | `appsettings.json` | `Billing:UsageBasedCredits` (Mode `Legacy`). |

No migration. The chat response contract, persistence and frontend are unchanged.

## Exact formula

```
ActualCost = Σ [(Input − Cached) × InputPrice + Cached × CachedInputPrice + Output × OutputPrice] / 1,000,000
RawCredits = round(ActualCost × PricingMultiplier / CostPerCredit, 6)
Credits    = min(Maximum, max(Minimum, ceiling(RawCredits), unmeasured ? Fallback : 0))
Charged    = min(Credits, ReservedCredits)           (Enforced)   |   operation price (Legacy, Shadow)
```

## Token usage collection

Every completed provider call records its provider-reported tokens under the request correlation id. Cached tokens are recorded as a subset of input. A chargeable finalization claims the calls no earlier finalization of the turn claimed. The claim is idempotent per reservation key, so replays bill the same calls, and calls made before a free outcome roll forward to the answer that bills them. Failed attempts, and calls after finalization, are not billed.

## Reservation and finalization changes

- **Enforced:** hold = `max(1, min(5, floor(available)))`, using the configured minimum and maximum. Below the minimum, the question is refused before execution with the existing `InsufficientCreditException` / HTTP 402.
- The commit charges `min(credits, hold)` through the existing `UsageFinalizationService`. That service already releases the unused part of the hold atomically with the ledger entry and outbox event.
- Replays: an existing reservation key reuses its hold. An existing claim key reuses its calls, so the ledger replay check sees identical data and returns `AlreadyFinalized`.
- Ordering is unchanged: finalization precedes conversation persistence on every chat path.

## Example charges (deterministic test prices: standard $2 / $0.50 / $8, premium $10 / $1 / $40 per M tokens; $0.01 per credit)

| Scenario | Usage | Charged |
|---|---|---|
| Low-cost answer | 1 standard call, 1,000 / 100 tokens ($0.0028) | 1 |
| Two models, one turn | standard 2,000 / 500 + premium 1,000 / 200 ($0.026) | 3, from a hold of 5; wallet 10 → 7, hold released |
| Feature 141 after a free clarification | intent call + engine call under one correlation id | 0 then 3; each call billed once |
| Missing provider token counts | 1 unmeasured call | 2 (configured fallback) |
| Low balance (2 credits), $0.04 usage | proposes 4 | 2 (capped at the hold) |
| Balance 0.5 | — | refused before execution |
| Clarification / unsupported / failed / cancelled | any | 0 |
| Shadow mode, $0.04 usage | proposes 4 | 1 (legacy), version `v1` |

## Calibration (Phase 7)

The token counts are **measured**: Feature 141 engine E2E runs on OpenAI `gpt-5.6-luna`, from `specs/141-…/evaluation/engine-e2e-results.json`. The prices are **illustrative placeholders** ($1.25 input, $0.125 cached, $10 output per M tokens). There are no repository measurements for specialist routes, so those rows are illustrative too.

| Question | Calls | Input / output tokens | Basis | Cost (USD) | Credits @ $0.0125 | Credits @ $0.01 |
|---|---|---|---|---|---|---|
| Simple monthly sales trend | ~1 | ~2,000 / ~150 | illustrative | 0.0040 | 1 | 1 |
| Quarterly financial statement | 1–2 | ~2,000–4,000 / ~150–300 | illustrative | 0.004–0.008 | 1 | 1 |
| F141 single-value retrieval (E141-02) | 2 | 7,426 / 1,242 | measured | 0.0217 | 2 | 3 |
| F141 cross-company comparison (8 cases × 2 runs) | 2 | 7,642–7,748 / 643–1,068 | measured | 0.0161–0.0202 | 2 | 2–3 |
| F141 ranking (E14 × 2) | 2 | 7,611 / 980–995 | measured | 0.0193–0.0195 | 2 | 2 |
| F141 threshold filtering (slice 3b, 6 cases) | 2 | 7,656–7,891 / 799–1,356 | measured | 0.0178–0.0234 | 2 | 2–3 |
| F141 typed refusal | 1 | ~2,600 / 345–705 | measured | — | 0 (`ValidationFailed`) | 0 |

Findings:

- Feature 141 variants cost almost the same, because each is dominated by two calls with a ~7.7k-token prompt. Category barely matters; output length does. At $0.01 per credit the same kind of question lands on either side of the 2.0 boundary (2 or 3 credits). At **$0.0125** every measured Feature 141 case is 2 credits and specialist queries stay at 1, so ordinary questions are predictable. The hard per-question cap remains 5.
- A real chat turn that reaches Feature 141 also includes the semantic-interpretation call (≤ 384 output tokens), so expect slightly higher costs than the engine-only measurements.
- The ~7k-token Feature 141 prompts are largely identical across calls, so provider prompt caching (billed at the cached price) can lower real costs substantially. Shadow data will show the actual cache hit rate.

**Recommended initial parameters (for Shadow, not production-approved):** `CostPerCredit` 0.0125 USD, `PricingMultiplier` 1.0, `MinimumCredits` 1, `MaximumCreditsPerQuestion` 5, `MissingUsageFallbackCredits` 1. Recompute after contract prices replace the placeholders.

## Test results

| Suite | Result |
|---|---|
| New unit tests (`UsageBasedCreditBillingTests`, unit project) | 25 / 25 passed |
| New integration tests (`UsageBasedCredit*`, integration project) | 15 / 15 passed |
| Focused existing unit tests (billing, AI model, semantic dispatcher, professional scanner, Feature 141 chat, subscription lifecycle) | 121 / 121 passed |
| Focused existing integration tests (V1/V2 chat endpoints, Feature 128, scanner execution, billing persistence and endpoints, provider adapters, observability) | 210 passed, 16 failed. The **same 16 fail on a baseline without this feature**: verified by temporarily restoring the touched files to HEAD and comparing the failure sets (identical). They assert financial-data content (EPS cells, price columns, YTD values) and routing text. |
| Architecture tests | 11 / 12. The failure flags `FinancialStatementTableIntentRules` in `SemanticCapabilityExecutors.cs`, a file this feature does not modify. A vendor name this feature had put in a Billing comment was caught by the same suite and removed. |

Not verified here:

- PostgreSQL-backed billing. The tests use the in-memory EF store. The reservation concurrency guards are unchanged.
- A live-LLM Feature 141 chat turn. Its exact reservation sequence is replayed against the registered billing services instead.
- Real provider cached-token payloads beyond the documented field names.

## Rollout and rollback

1. Deploy with `Billing:UsageBasedCredits:Mode = Legacy` (the shipped default).
2. Set contract prices, then switch to `Shadow`. Review the `Usage-based credit charge evaluated` logs and `billing.usage_based.proposed_credits` against the legacy charge.
3. After Product Owner approval, set `Enforced`.
4. **Rollback:** set `Mode = Legacy` and restart. Any hold interrupted by the restart expires after 5 minutes.

## Unresolved business pricing decisions

1. Contract token prices per model; `CostPerCredit` and `PricingMultiplier`.
2. Discounts for `PartiallyCompleted` and cached-scanner answers. These are fractional today and cannot be kept with integer credits.
3. Pre-existing double minimum charge when a semantic `NoData` result is followed by a definitive Feature 141 fallback.
4. Whether to absorb overage above the hold for low-balance accounts, or refuse such questions.
5. Under Enforced, a low-balance account's concurrent questions share up to 5 held credits each. A second concurrent question can be refused until the first settles, which usually takes seconds.
