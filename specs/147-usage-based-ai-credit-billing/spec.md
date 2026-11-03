# Feature 147 — Usage-Based AI Credit Billing

## Goal

Charge chat questions integer credits based on what their AI processing actually cost, instead of one credit per answered question. The change is a small extension of the existing Billing context. It adds no new wallet, ledger, subscription or billing service.

## Billing lifecycle findings

| Question | Finding |
|---|---|
| 1. Where is the charge applied? | `AiFacadeBillingHook.FinalizeAsync` commits through `UsageFinalizationService.CommitAsync`. Legacy and Shadow use `OperationUsageChargeCalculator` (policy `v1`: `AiQuery.Scanner` = 1 credit); Enforced uses the usage calculator for configured operations. Every chat route goes through this hook: MAF V2 workflow, V2 runner, semantic coordinator, Feature 141 fallback and V1 orchestrator. Professional scanners use the hook under a different operation code. |
| 2. Reserved vs finalized | Enforced mode starts with the minimum hold (normally one credit) before execution. The same reservation is atomically upgraded before an expensive MAF or Feature 141 path. Finalization runs once after the logical question completes. |
| 3. Can finalization charge more than one credit? | Yes, when the wallet successfully reserves the required amount first. Enforced mode charges the whole-number usage proposal and releases unused capacity. It does not cap the bill at an underfunded hold. |
| 4. Are tokens recorded per call? | Provider-reported usage from completed calls reaches `IAiExecutionUsageAccumulator` under the turn correlation id. It retains per-provider/model calls, parses cached-token fields, expires idle entries after 30 minutes, and prevents duplicate claims. Feature 141 now shares the turn's usage correlation and reservation. |
| 5. Can one question invoke several models? | Yes: semantic interpretation, MAF agent turns, and the Feature 141 intent extractor (≤ 2 calls) plus schema navigation. Workloads can route to different providers. |
| 6. Failures, timeouts, retries | Zero-charge statuses (`ValidationFailed`, `ClarificationRequired`, `ProviderFailed`, `CancelledBeforeExecution`, `TimedOutBeforeExecution`) commit 0 credits. Exceptions finalize as `ProviderFailed`. Abandoned holds expire after 5 minutes (maintenance job). Provider fallback and rate-limit retries are bounded inside the model layer. |
| 7. Duplicate-charge prevention | A client-supplied `IdempotencyKey`/billing operation id is stable across retries; correlation ids are trace-only. The API retains compatibility for callers that omit the body field by assigning a new key and returning it in the `Idempotency-Key` response header. Omitted-key callers must preserve that response key for subsequent retries; a lost first response cannot be safely deduplicated. A SHA-256 fingerprint of stable request fields rejects a reused key with a changed payload. A per-account semaphore, unique idempotency constraints and wallet-revision fencing guard reservations and settlement. |

## Credit calculation policy

Deterministic and server-side. The LLM never influences the number of credits.

```
For each completed model call c:
  cached(c)   = min(CachedInputTokens, InputTokens)          -- provider-reported subset of input
  cost(c)     = (InputTokens − cached) × InputPrice
              + cached × CachedInputPrice
              + OutputTokens × OutputPrice                    -- prices per 1,000,000 tokens

ActualCost   = Σ cost(c)                                      -- decimal arithmetic
RawCredits   = round6(ActualCost × PricingMultiplier / CostPerCredit)
Credits      = max(MinimumCredits, ceiling(RawCredits))
if any call is unmeasured:  Credits = max(Credits, MissingUsageFallbackCredits)
Credits      = min(Credits, MaximumCreditsPerQuestion)
Charged      = Credits                                        -- Enforced, only after reservation covers it
```

- Cached tokens are part of `InputTokens` in both the OpenAI and DeepSeek usage reports. They are billed once, at the cached price.
- `round6` (6 decimal places) only removes decimal-division artifacts before `ceiling`. A raw value of `1.01` becomes 2 credits.
- **Unmeasured call** means the provider reported no token counts, or no price is configured for the model. Such a call is never estimated (no token counts are invented, no text-length heuristics). When a model has no configured price, a provider-reported cost in the policy currency is used if one exists.
- **Failed attempts** (invalid structured output, timeouts) are not billed, even when the provider reported tokens. This keeps the existing accumulator semantics and favors the customer.
- **Scope:** only operation codes listed in `OperationCodes` (`AiQuery.Scanner`, the chat operation) are usage-priced. Every other operation keeps its operation price.
- **Outcomes:** zero-charge statuses stay free. Every other status, including `PartiallyCompleted` and cached-scanner results, is charged by usage in whole credits. The legacy fractional discounts (0.5 and 0.2) do not apply in Enforced mode. See open decisions.

## Configuration

Section `Billing:UsageBasedCredits` (`UsageBasedCreditBillingOptions`). It is validated at startup whenever `Mode` is not `Legacy`.

| Key | Default | Meaning |
|---|---|---|
| `Mode` | `Legacy` | `Legacy`: operation price charged. `Shadow`: operation price charged, usage charge computed and reported. `Enforced`: usage charge committed. |
| `PolicyVersion` | `usage-v1` | Written to the ledger `PricingPolicyVersion` in Enforced mode (max 64 characters). |
| `Currency` | `USD` | Currency of the token prices and of `CostPerCredit`. |
| `CostPerCredit` | `0.0125` | Provider cost covered by one credit. |
| `PricingMultiplier` | `1.0` | Margin/markup applied to the provider cost. |
| `MinimumCredits` | `1` | Minimum successful charge. |
| `MaximumCreditsPerQuestion` | `5` | Per-question cap. Also the largest hold. |
| `MissingUsageFallbackCredits` | `1` | Floor applied when any call is unmeasured. |
| `OperationCodes` | `["AiQuery.Scanner"]` | Operations priced by usage. |
| `ModelPrices[]` | OpenAI `gpt-5.6-luna`, DeepSeek `deepseek-chat` | `ProviderKey`, `ModelKey`, `InputPerMillionTokens`, `CachedInputPerMillionTokens`, `OutputPerMillionTokens`, `PriceVerified`. **The shipped values are illustrative placeholders, not approved contract prices.** Unverified entries are excluded from the calculator; Enforced startup rejects any configured unverified price. |

## Usage collection

- `AiExecutionUsageFacts` gains `CachedInputTokens`. It is parsed from OpenAI Responses `usage.input_tokens_details.cached_tokens` and DeepSeek `usage.prompt_cache_hit_tokens`.
- `IAiExecutionUsageAccumulator` keeps each completed call per correlation id. `ClaimUnbilled(correlationId, claimKey)` returns the calls no earlier claim took, and is idempotent per claim key (the reservation idempotency key).
  - Legacy mode does not claim calls. Shadow and Enforced settlements claim completed calls for evaluation/audit, including zero-charge outcomes; the zero-charge status remains zero. A semantic result that continues into Feature 141 defers settlement so earlier calls remain available to the final answer's one charge.
  - A replayed finalization returns the existing ledger result. Claiming is idempotent per reservation key, so a retry after a transient pre-commit failure can claim the same calls again.
- The Feature 141 derived reservation carries `UsageCorrelationId` (the turn's correlation id), because the engine's model calls are recorded under it.
- Accumulator entries untouched for 30 minutes are evicted. This bounds the singleton.
- Model calls made after a turn's finalization are not billed.

## Credit lifecycle changes

1. **Reserve (Enforced only):** start with `max(ceiling(operation price), MinimumCredits)` and do not partially hold the wallet's remaining balance. This preserves inexpensive one-credit routes. Before MAF/Feature 141 or another expensive path, atomically increase the same reservation to the measured proposal where available, otherwise the configured per-question maximum. If the wallet cannot cover the increase, release the hold and refuse before entering that path. A replayed turn reuses its logical operation key.
2. **Finalize:** charge the whole-number usage proposal only when the reservation covers it. A defensive capacity check prevents committing above the successful hold. `UsageFinalizationService` releases unused held credits atomically with the ledger entry and outbox event.
3. **Fallback ordering:** semantic results that can continue through Feature 141 defer settlement. The fallback and earlier model calls are claimed under one turn reservation and produce one final charge. Failed, unsupported and clarification-only outcomes retain their existing zero-charge behavior.
4. **Ordering:** finalization still precedes conversation persistence, so a failed commit never persists a billed answer.
5. An additive billing migration stores nullable `RequestFingerprint` on usage reservations. It stores only the request hash, not the prompt. Provider/model/token data and provider-reported cost remain in the internal ledger for audit. Customer chat and usage responses suppress `EstimatedCost`; no internal USD cost or pricing configuration is shown to end users.

## Transparency

The chat response already carries `usage.creditsCharged` and `usage.remainingSpendingCapacity`. Usage is persisted with the assistant message, and the UI renders `{n} اعتبار مصرف شد` for live and saved answers. The response contract and visible UI are unchanged. Chat submissions now include an optional stable idempotency key so an HTTP retry can reuse the same billing operation. Provider prices and the formula are not exposed.

Internal diagnostics: structured log `Usage-based credit charge evaluated` (mode, policy version, stable operation id, correlation id, legacy/proposed/charged credits, modeled cost, cost-per-credit inputs, calls, tokens, fallback and cap) and `Usage-based model-call accounting` (stable operation id, route, provider/model calls and token counts). The histogram `billing.usage_based.proposed_credits` is emitted by meter `FinancialCopilot.Billing`.

## Rollout

1. Ship with `Mode = Legacy` (current behavior).
2. Replace the illustrative `ModelPrices` with contract prices, then switch to `Shadow`. Compare `ProposedCredits` with `OperationCredits` in the logs and histogram for real traffic.
3. Calibrate `CostPerCredit` / `PricingMultiplier`, get Product Owner approval, then switch to `Enforced`.
4. **Rollback:** set `Mode = Legacy` and restart the API (the mode is read at startup). A hold left by a turn the restart interrupted expires after 5 minutes through the existing reservation-expiry maintenance. Already-committed charges stay as they are in the ledger.

Subscription plan prices and monthly credit allocations are unchanged.

## Out of scope

New wallet or ledger tables, per-tool pricing, frontend redesign, provider-cost reconciliation, and estimating usage from text.

## Open business decisions

1. Contract token prices per model, and the `CostPerCredit` / `PricingMultiplier` values (what one credit is worth).
2. Whether `PartiallyCompleted` and cached-scanner answers should keep a discount. Credits are integers, so the legacy 0.5 / 0.2 multipliers cannot be kept.
3. Shadow calibration is still required before Enforced can be considered for production. The current model prices are placeholders and must be replaced with verified provider contract prices.
