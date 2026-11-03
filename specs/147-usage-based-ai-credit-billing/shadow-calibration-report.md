# Feature 147 — Shadow Calibration & Cost Accounting Completion

**As of:** 2026-10-29 (Asia/Tehran)

**Status:** `FEATURE_147_CALIBRATION_READY_WITH_BLOCKERS`

**Production billing mode:** `Legacy` (inspected; unchanged)

## Decision

Feature 147 can observe detailed model usage in Shadow while the existing billing hook continues to settle the Legacy operation charge. Focused Shadow, API, provider-adapter, concurrency, replay, and PostgreSQL tests passed. Shadow does not expose proposed credits or modeled USD as customer charges.

No production mode was activated, no Production billing configuration was edited in this task, and no live provider call was made. The new account-level cost data needed for definitive calibration is not available. **Enforced remains NO-GO.** Production Shadow is **not activated**; the recommendation is conditional on operator approval of the bounded rollout configuration below and internal telemetry access.

## 1. Cache-write accounting

The OpenAI Responses adapter now captures `input_tokens`, `output_tokens`, `input_tokens_details.cached_tokens`, aggregate `cache_write_tokens`, and the retention details `cache_write_tokens_details.ttl_30m_tokens` and `ttl_12h_tokens`. The provider/model identifier remains on every call observation. These are the field names shown in OpenAI’s [prompt caching usage examples](https://developers.openai.com/api/docs/guides/prompt-caching).

Usage flows through the hosted response, `AiExecutionUsageFacts`, the in-memory per-turn accumulator, and the Feature 147 billing observation. Structured model-call logs include input, cache reads, aggregate and duration-specific writes, output, workload, attempt number, exact provider/model, route, and the logical operation ID. Missing token fields are emitted as `?`; explicit zero remains `0`. The accumulator summary returns null when any call needed for a cache-token total is unknown.

For a model configured with separate write rates, cost is calculated as:

```text
ordinary input = input − cached reads − cache writes
input cost = ordinary × input rate
           + cached reads × cached-input rate
           + 30m writes × input rate × 1.25
           + 12h writes × input rate × 2.5
output cost = output × output rate
```

The aggregate write count is checked against the two non-overlapping retention counts and is not added a second time. Negative, inconsistent, missing premium-write, missing required cache-read, or over-input counts make the call `INCOMPLETE_USAGE`; the cost is not treated as zero and the existing missing-usage fallback remains in effect. Decimal arithmetic is used.

Long-context pricing is configured per model. When that model’s input count is greater than 272,000, the configured full-request input and output multipliers are applied. No surcharge is applied to other models without that model’s explicit policy. OpenAI’s [GPT-5.6 Luna model page](https://developers.openai.com/api/docs/models/gpt-5.6-luna) lists input at $0.20/M, cached input at $0.02/M, output at $1.20/M, and the >272K full-request surcharge of 2× input and 1.5× output. The prompt-caching guide separates the two write lifetimes and their respective 1.25× and 2.5× multipliers.

DeepSeek continues to expose its provider-reported prompt-cache-hit count through its own adapter. OpenAI write-retention prices are not applied to it. The configured `deepseek-chat` row remains an unverified placeholder and is not used to claim an exact DeepSeek cost.

## 2. Pricing confidence inventory

`PricingConfidence` now distinguishes `VERIFIED_CONTRACT`, `VERIFIED_PUBLIC_RATE`, `UNVERIFIED`, and `INCOMPLETE_USAGE`. The explicit confidence is authoritative. The legacy `PriceVerified=true` flag alone can establish at most a verified public rate; it never proves the account contract. Enforced validation requires an explicit `VERIFIED_CONTRACT`. A public rate can be used for Shadow modeling when labeled `VERIFIED_PUBLIC_RATE`. If a rate is missing, it is excluded from modeled cost and does not affect Legacy billing.

| Provider/model | Runtime status in inspected base configuration | Rate evidence | Calibration treatment |
|---|---|---|---|
| OpenAI `gpt-5.6-luna` | Enabled hosted model | Current public reference: $0.20 input, $0.02 cached input, $1.20 output per million; account contract/invoice unavailable | `VERIFIED_PUBLIC_RATE` only when explicitly configured as such; never an account-contract claim |
| OpenAI `gpt-5.6-luna` repository billing row | Same model | Existing illustrative row: $1.25 / $0.125 / $10.00; `PriceVerified=false` | `UNVERIFIED`; excluded by `ToPolicy()` |
| DeepSeek `deepseek-chat` repository billing row | No matching enabled provider registration in the inspected base descriptor list | Existing illustrative row: $0.27 / $0.07 / $1.10; no account contract verified | `UNVERIFIED`; do not map it to a different published model |
| Any call without required usage detail | Provider-dependent | Missing or inconsistent provider fields | `INCOMPLETE_USAGE`; retain raw observation and exclude from complete-cost results |

The inspected `appsettings.json` still has `Mode=Legacy`, `CostPerCredit=0.0125`, multiplier 1, minimum 1, maximum 5, and missing-usage fallback 1. These are existing conversion settings, not approved rates or profitability evidence. No provider contract, invoice, subscription revenue, payment expense, tax, discount, or revenue-share data was available.

## 3. Shadow isolation, API behavior, and rollback safety

Focused tests exercised the registered billing hook and existing endpoint paths. Shadow reserves and settles Legacy credits while recording a separate proposal; the Shadow integration case verified that the wallet decreased by the Legacy one credit even when the hypothetical proposal was higher. The same filter included the Legacy behavior, response endpoints, billing persistence/replay, and model adapters. No chat response schema or front-end usage label was changed.

The prior Feature 147 reservation hardening remains in place: stable billing operation IDs, request fingerprints, reservation capacity upgrades in Enforced only, one finalization across Feature 141 fallback, claim-once usage, uniqueness fencing, and persisted replay after a settlement race. Shadow does not rewrite existing ledger charges. No live or historical customer billing record was accessed in this task.

Rollback is a configuration rollback: set `Billing:UsageBasedCredits:Mode` to `Legacy` and restart/redeploy. The additive reservation fingerprint migration can remain applied. Existing committed ledger entries remain unchanged; an interrupted reservation follows the existing expiry maintenance behavior.

## 4. Verification results

| Check | Result |
|---|---:|
| Feature 147, billing-domain, and billing-service unit filter | **69 passed, 0 failed** |
| Feature 147 billing, persistence, API/V2 endpoint, provider adapter, and Feature 141 chat integration filter | **49 passed, 0 failed, 12 skipped** |
| Feature 147 PostgreSQL billing tests on the guarded disposable loopback database | **7 passed, 0 failed, 0 skipped after the concurrency fix** |
| Targeted architecture regression `MigratedSemanticExecutors_DoNotReintroduceLegacySymbolExtraction` | **1 failed**, same previously documented failure: `FinancialStatementTableIntentRules` found in the modified Feature 141 semantic executor |
| Calibration script syntax/CLI and incomplete-cache-write checks | **Passed** using temporary inputs labeled `SYNTHETIC_TEST`; no sample was retained or counted as traffic |

The 12 integration skips are the opt-in live Feature 141/provider cases. Provider-key environment variables were present, but no isolated non-production provider project, target endpoint, or credential authorization could be established from the available configuration. No live request was sent. The architecture failure is in existing Feature 141 source and is unrelated to Feature 147 billing.

A pre-fix PostgreSQL rerun reproduced a `READ COMMITTED` race: one contender read the reservation as `Reserved`, then saw the winning transaction's newly committed ledger row while its tracked reservation was stale. `UsageFinalizationService.CommitAsync` now reloads the reservation and wallet when that ledger row appears; if the refreshed reservation is committed, it validates the existing commit payload and returns `AlreadyFinalized`. The targeted concurrency case passed after the fix, and the complete PostgreSQL billing class then passed **7/7**.

The focused endpoint rerun also corrected a test lookup that used `workflowCorrelationId` for a ledger key. Billing uses the response's stable `Idempotency-Key` to form `<operation-guid>:charge`; the assertion now follows that contract. The API response shape and billing behavior were unchanged.

## 5. Real-usage availability and bounded collection plan

There is no Feature 147 `PRODUCTION_SHADOW` or `STAGING_LIVE` log export, ledger export, reservation export, or provider invoice available in the repository. Existing Feature 141 E2E artifacts are historical test-harness observations, not Production Shadow. The prior report records representative aggregates such as two calls and 7,426 input / 1,242 output tokens for one single-value case, with other two-call Feature 141 cases around 7.6–7.9K input tokens. Those artifacts lack the per-call cache-read/write split and stage attribution required to produce complete costs; they are not used as definitive cost evidence here.

Once an isolated staging target and its authorized provider project are confirmed, collect one run each for:

1. Simple monthly sales
2. Financial statement lookup
3. Monthly sales visualization
4. Feature 141 single-company query
5. Feature 141 cross-company discovery
6. Feature 141 filtering and ranking
7. Multi-step planning and fallback
8. Clarification and unsupported answer

Bound the initial run to eight logical questions, at most four completed model calls per question, a provider-side $1 hard spend limit for the run, and stop if the provider limit or an application token cap is reached. Disable repeated test retries; retain the operation ID and per-call model/workload usage fields. Record each run as `STAGING_LIVE`. Future Production Shadow must be labeled `PRODUCTION_SHADOW`; replayed provider records use `HISTORICAL_REPLAY`; harness-only tests use `SYNTHETIC_TEST`. Only complete observations with `VERIFIED_CONTRACT` rates count toward definitive account-cost calibration.

## 6. Calibration tool and candidate scenarios

The existing [`analyze_shadow_calibration.py`](./analyze_shadow_calibration.py) was extended; no second analysis tool was added. It now uses decimal pricing math and produces logical-question counts, calls-per-question and token distributions, cache reads/writes by duration, complete modeled-cost percentiles, 1–5 credit distribution, cap rate, cost-per-credit sensitivities, maximum-cap credit value/exposure, unpriced and non-contract percentages, Legacy versus hypothetical Enforced deltas, and workload/route cost breakdowns. The input price manifest is dated and hashed. Public-rate modeling stays distinct from contract-priced definitive cost.

The requested sensitivities are present as defaults:

| Cost per credit | Maximum 5-credit value per question | Real-sample cost recovery / credit distribution |
|---:|---:|---|
| $0.0050 | $0.0250 | Pending; zero eligible real questions |
| $0.0075 | $0.0375 | Pending; zero eligible real questions |
| $0.0100 | $0.0500 | Pending; zero eligible real questions |
| $0.0125 | $0.0625 | Pending; zero eligible real questions |
| $0.0150 | $0.0750 | Pending; zero eligible real questions |
| $0.0200 | $0.1000 | Pending; zero eligible real questions |

The cap values above are only `maximum credits × candidate CostPerCredit`; they are not provider-cost estimates or revenue. Since no real calibration window and no subscription revenue/expense data were supplied, there is no defensible final price, cost-recovery conclusion, or profitability recommendation.

## 7. Feature 141 stage-cost breakdown

Per-call workload is now included in the internal billing observation so future records can group `ScannerParsing`, `SchemaNavigation`, `ResearchTool`, `ExplanationGeneration`, and related workloads. The analyzer reports tokens and modeled cost by workload, attempt counts when supplied, and route cost for Feature 141 fallback. It does not store prompts.

Current hosted adapters emit `AttemptNumber: 0` and do not expose internal HTTP retry attempts, so retry count and retry cost are not measured by current observations.

Current historical data cannot identify the most expensive Feature 141 stage: available artifacts contain question-level totals, not per-call stage tokens, cache reads/writes, or provider costs. `SchemaNavigation` also combines navigation with schema/context work; the current workload label cannot split those sub-stages. Completed model calls can be attributed to their workload and route, but failed provider attempts without reported usage are not a measured cost. No redundant LLM call or repeated large prompt is proven by these records, so no Feature 141 prompt or behavior optimization was made.

## 8. Operator configuration for a controlled Shadow window

This is the exact `Billing:UsageBasedCredits` overlay for the currently enabled hosted model. Replace the whole `ModelPrices` array so the old illustrative rows do not survive as active rates. `PriceVerified` remains false; the explicit confidence is public reference pricing. The mode is deliberately `Shadow`, so wallet settlement remains Legacy.

```json
{
  "Billing": {
    "UsageBasedCredits": {
      "Mode": "Shadow",
      "PolicyVersion": "usage-shadow-public-2026-10-29",
      "Currency": "USD",
      "CostPerCredit": 0.0125,
      "PricingMultiplier": 1.0,
      "MinimumCredits": 1,
      "MaximumCreditsPerQuestion": 5,
      "MissingUsageFallbackCredits": 1,
      "OperationCodes": ["AiQuery.Scanner"],
      "ModelPrices": [
        {
          "ProviderKey": "OpenAI",
          "ModelKey": "gpt-5.6-luna",
          "InputPerMillionTokens": 0.20,
          "CachedInputPerMillionTokens": 0.02,
          "OutputPerMillionTokens": 1.20,
          "PriceVerified": false,
          "PricingConfidence": "VERIFIED_PUBLIC_RATE",
          "CacheReadUsageRequired": true,
          "CacheWriteUsageRequired": true,
          "CacheWrite30mInputMultiplier": 1.25,
          "CacheWrite12hInputMultiplier": 2.5,
          "LongContextThresholdInputTokens": 272000,
          "LongContextInputMultiplier": 2.0,
          "LongContextOutputMultiplier": 1.5
        }
      ]
    }
  }
}
```

The $0.0125 conversion and 1–5 cap above remain calibration scenarios, not approved tariffs. Because Shadow is not traffic-sampled by this option, use it only for a bounded, operator-approved window with internal log access and a provider-side spend stop. Unknown providers or incomplete usage will be labeled and excluded from complete-cost results.

## 9. Activation recommendations

- **Production Shadow:** **Conditional GO after operator approval** for the bounded window above. The wallet/API isolation is tested; public model rates must remain labeled `VERIFIED_PUBLIC_RATE`, internal telemetry must be access-restricted, and the base configuration must be changed only through the deployment owner’s normal release process. The current deployment remains `Legacy` until that approval and rollout occur.
- **Production Enforced:** **NO-GO.** Require account-contract rates for every active model, a representative `PRODUCTION_SHADOW` or authorized staging sample with complete usage, reviewed cap/fallback policy, product/finance approval, and a completed cost/revenue analysis before any Enforced change.

## 10. Worktree and scope

No prompt refactor, Feature 141 behavior change, production-mode activation, billing-configuration edit, commit, or push was made. The existing reservation, settlement, idempotency, concurrency, and migration safeguards were preserved.
