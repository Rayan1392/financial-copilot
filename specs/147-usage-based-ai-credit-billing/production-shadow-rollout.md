# Feature 147 — Controlled Shadow Rollout & Real Usage Calibration

**Prepared:** 2026-10-29 (Asia/Tehran)

This runbook does not activate production Shadow mode. No production configuration, billing data, wallet, plan, or provider traffic was changed or accessed for this task.

## 1. Preflight verification

| Invariant | Evidence and result |
|---|---|
| Shadow preserves Legacy charges | `AiFacadeBillingHook` retains the operation-based `UsageChargeResult` in Shadow; only Enforced replaces it. The focused Shadow integration case returned a successful Scanner response with one `v1` credit charged. **Pass.** |
| Proposed credits do not touch wallets or ledger | Shadow evaluates and logs a proposal, then finalizes the Legacy charge. The injected-failure integration case checked the wallet delta and the single `<operation-id>:charge` ledger row. **Pass.** |
| Shadow makes no extra model calls | The accounting changes read accumulated usage and calculate prices locally. They add no provider or model-client call. No production request was generated. **Pass by code path.** |
| Billing idempotency remains intact | The reservation and charge keep their stable operation keys and unique ledger-key fence. The focused endpoint, hook, and transient-persistence retry tests passed. **Pass.** |
| Failed Shadow telemetry fails open | Best-effort boundaries cover provider usage recording, routing diagnostics, usage-summary/claim evaluation, and post-settlement logging. The injected accumulator throws from `Record`, `GetSummary`, and `ClaimUnbilled`; the API still returned HTTP 200 and settled exactly the Legacy charge. **Functional pass; per-call failure logging is incomplete** (see Section 2). |
| Unverified pricing is not reported as definitive cost | Rates carry explicit confidence. Public rates can be modeled as `VERIFIED_PUBLIC_RATE`; only complete usage priced with `VERIFIED_CONTRACT` is counted by the analyzer as definitive account cost. **Pass by configuration and analyzer rules.** |
| Usage events have stable logical operation IDs | Billing evaluation, model-call accounting, and settlement logs include `OperationId`. `/api/ai/v1/query` accepts optional `idempotencyKey` in the request body and returns the chosen/generated ID in the `Idempotency-Key` response header. Reuse the body value for retries; without it, a retry gets a new operation ID. **Pass per request; retry grouping depends on the caller.** |

The focused integration filter covering Shadow, Legacy, endpoint, hook, injected-telemetry-failure, and transient-persistence retry behavior passed **19/19**. The new failure test builds the current API and verifies the response, `v1` charge, wallet delta, and ledger key. Existing Feature 147 unit/integration/PostgreSQL results remain recorded in [shadow-calibration-report.md](./shadow-calibration-report.md); the PostgreSQL suite was not rerun because this change does not alter persistence.

The added fail-open boundaries are limited to optional usage/diagnostic telemetry. `OperationCanceledException` is not swallowed by the billing hook or routing diagnostic. No extra LLM call was introduced.

## 2. Existing rollout capabilities

- The checked-in `docker/appsettings.Production.json` has no `Billing:UsageBasedCredits` block. The options default is `Mode=Legacy`; without an override that is the image default. The local workspace `.env` and current process environment contain no Feature 147 override. The local Docker context is `desktop-linux`, not a production connection. The production server's ignored `.env` and effective container environment were not inspected, so the live mode is unverified.
- The root Compose file passes `.env` to the `api` service. Configuration can therefore be overridden with `Billing__UsageBasedCredits__...` environment variables.
- `UsageBasedCreditBillingOptions` has a global `Mode` and an `OperationCodes` filter. It has no user, tenant, staff, percentage, or cohort allowlist. `OperationCodes` selects operation types; it does not limit which users are observed. Shadow is therefore global for every matching operation.
- `AiFacadeController` passes `AiQuery.Scanner` for `/api/ai/v1/query`, and the V2 billing function uses that same operation code. The `AiQuery.Scanner` scope therefore covers the query categories routed through this endpoint. This is global across users.
- Compose is runtime-only and uses the existing API image. The API applies EF migrations on startup; use one API replica during restart and verify the release has no pending migration intended for this change.
- Billing emits structured `ILogger` event templates with the required property names. Compose configures the Console sink with a rendered text output template, not JSON; `docker compose logs api` can retrieve the rendered `Mode=...`, `OperationId=...`, token, credit, confidence, and settlement fields when Docker retains the lines. The Feature 147 templates do not include prompts or response text. The billing hook logs persistent Shadow accounting failures with `OperationId`; the V2 adapter's per-call record warning has only `CorrelationId`, and `AiModelExecutionService.RecordUsageSafely` catches recorder errors without logging them. A transient recorder failure can therefore be fail-open without a dedicated failure event. Compare provider-attempt telemetry with per-operation `ModelCalls`/usage totals and reconcile actual settlement against the ledger; the telemetry catch does not wrap `CommitAsync` or suppress persistence/ledger failures. No production log retention/access policy or normalized JSONL export was available to verify. Export only the Feature 147 events; other application logs can contain query-derived metadata.

## 3. Exact activation configuration

This is the candidate environment overlay for the currently enabled OpenAI `gpt-5.6-luna` model. It replaces the illustrative model-price rows with a public-rate reference. **The rates are not account contract prices, and `$0.0125` is a sensitivity value, not an approved tariff.** Do not copy these values into Enforced mode.

The options class declares `SectionName = "Billing:UsageBasedCredits"` and the property names used below. Compose passes `.env` through `env_file`; the .NET environment provider maps `:` to `__`, so these environment key paths bind to that section. Compose does not explicitly map a billing variable today.

Add these lines to the server `.env` only after the release owner approves the window. Remove any older `Billing__UsageBasedCredits__ModelPrices__*` keys first so stale array entries do not remain bound.

```dotenv
Billing__UsageBasedCredits__Mode=Shadow
Billing__UsageBasedCredits__PolicyVersion=usage-shadow-public-2026-10-29-r1
Billing__UsageBasedCredits__Currency=USD
Billing__UsageBasedCredits__CostPerCredit=0.0125
Billing__UsageBasedCredits__PricingMultiplier=1.0
Billing__UsageBasedCredits__MinimumCredits=1
Billing__UsageBasedCredits__MaximumCreditsPerQuestion=5
Billing__UsageBasedCredits__MissingUsageFallbackCredits=1
Billing__UsageBasedCredits__OperationCodes__0=AiQuery.Scanner
Billing__UsageBasedCredits__ModelPrices__0__ProviderKey=OpenAI
Billing__UsageBasedCredits__ModelPrices__0__ModelKey=gpt-5.6-luna
Billing__UsageBasedCredits__ModelPrices__0__InputPerMillionTokens=0.20
Billing__UsageBasedCredits__ModelPrices__0__CachedInputPerMillionTokens=0.02
Billing__UsageBasedCredits__ModelPrices__0__OutputPerMillionTokens=1.20
Billing__UsageBasedCredits__ModelPrices__0__PriceVerified=false
Billing__UsageBasedCredits__ModelPrices__0__PricingConfidence=VERIFIED_PUBLIC_RATE
Billing__UsageBasedCredits__ModelPrices__0__CacheReadUsageRequired=true
Billing__UsageBasedCredits__ModelPrices__0__CacheWriteUsageRequired=true
Billing__UsageBasedCredits__ModelPrices__0__CacheWrite30mInputMultiplier=1.25
Billing__UsageBasedCredits__ModelPrices__0__CacheWrite12hInputMultiplier=2.5
Billing__UsageBasedCredits__ModelPrices__0__LongContextThresholdInputTokens=272000
Billing__UsageBasedCredits__ModelPrices__0__LongContextInputMultiplier=2.0
Billing__UsageBasedCredits__ModelPrices__0__LongContextOutputMultiplier=1.5
```

### Pre-activation checks

1. Confirm the deployment owner has approved a global Shadow window, log access, and rollback operator. The configuration cannot target internal users only.
2. Record a 24-hour baseline for API request volume, 5xx rate, latency, provider-call count by route/workload, Legacy credits per operation, and open reservation count. Agree and record alert thresholds before enabling Shadow.
3. Verify the exact API image/tag, one API replica, database health, current migration state, and a recoverable copy of the deployment `.env`. The API restarts through the normal Compose path, which runs startup migrations.
4. Verify the effective mode and variable names without displaying secret values:

   ```bash
   cd /opt/sapio/financial-copilot
   docker compose exec api sh -lc 'printenv Billing__UsageBasedCredits__Mode || true'
   docker compose exec api sh -lc 'printenv | cut -d= -f1 | grep "^Billing__UsageBasedCredits__" || true'
   ```

   The mode must be confirmed as `Legacy` before the change. Resolve any unexpected billing overrides first.
5. Confirm the normal restricted log store captures `OperationId`, `CorrelationId`, `ExecutionRoute`, `OperationCode`, model IDs, per-call usage fields, `PricingConfidence`, `CostComplete`, and settlement outcome without prompts or response text. If it only has rendered console text, do not claim structured completeness until the fields have been normalized and checked.
6. `/api/ai/v1/query` accepts optional `idempotencyKey` in the request body (`AiQueryHttpRequest.IdempotencyKey`) and returns the selected/generated identifier in the response header `Idempotency-Key`. First-party clients send a stable body value for retries. If omitted, the server generates a new ID per request.

### Activation and restart

These are operator instructions only; they were not executed here.

```bash
cd /opt/sapio/financial-copilot
cp .env ".env.pre-feature147-shadow-$(date -u +%Y%m%dT%H%M%SZ)"
# Add the approved configuration block above to .env; remove stale ModelPrices indexes.
docker compose up -d --no-build --force-recreate api
docker compose ps api
curl -fsS http://127.0.0.1:5074/health
docker compose logs --since=10m --no-color api | grep -F "Usage-based credit charge evaluated"
```

Do not create a test prompt or synthetic production request to make a Shadow event appear. Wait for normal traffic. Confirm logged `Mode=Shadow`, the approved `OperationCode`, and a non-empty `OperationId`; if there is no matching traffic, the observation window has not started.

## 4. Monitoring and rollback procedures

### Monitor the existing structured events

Use the restricted log store's structured fields where available. The current event templates are:

- `Usage-based credit charge evaluated`: `Mode`, `PolicyVersion`, `OperationId`, `CorrelationId`, `OperationCode`, `ExecutionRoute`, `OperationCredits`, `ProposedCredits`, `ChargedCredits`, `CostComplete`, `PricingConfidence`, `ModelCalls`, token totals, `UnmeasuredModelCalls`, `FallbackApplied`, and `CappedAtMaximum`.
- `Usage-based model-call accounting`: `OperationId`, `CorrelationId`, `ExecutionRoute`, and per-call provider/model, call count, input/cache-read/cache-write/output tokens, workload, and attempt.
- `Usage-based billing settlement completed`: `OperationId`, `CorrelationId`, `ExecutionRoute`, `SettlementOutcome`, `ActualCredits`, reservation, remaining balance, and `LedgerId`.
- `Shadow usage telemetry failed; the Legacy charge will continue.`: stable `OperationId`, `CorrelationId`, and exception type. These warnings must not correlate with user-visible chat failures.

Required assertions for every matching Shadow operation:

- `ChargedCredits = OperationCredits`; `ProposedCredits` may differ and must never be displayed or persisted as the actual debit.
- Exactly one completed charge ledger row for `<OperationId>:charge`; no duplicate settlement for the same operation ID.
- `SettlementOutcome` is expected for the chat outcome; no unexplained `Reserved` reservation remains past the normal request/reservation expiry window.
- No negative wallet `Balance` or `ReservedAmount`.
- No LLM call is attributable to Shadow accounting. Compare model-call count by route/workload with the recorded baseline; any confirmed extra call caused by billing telemetry is an immediate stop.
- Retain incomplete token observations, but exclude them from complete-cost percentiles. Track `CostComplete=false`, `PricingConfidence`, unknown model rates, cache detail coverage, and unmeasured calls separately.

Read-only database checks (run through the approved production read-only path):

```sql
SELECT COUNT(*) AS negative_wallet_rows
FROM billing_wallet_projections
WHERE "Balance" < 0 OR "ReservedAmount" < 0;

SELECT "IdempotencyKey", COUNT(*) AS row_count
FROM billing_usage_ledger_entries
WHERE "OccurredAt" >= now() - interval '1 day'
GROUP BY "IdempotencyKey"
HAVING COUNT(*) > 1;
```

Both queries must return zero violating rows. Reconcile actual credits to `CreditsCharged` using the charge key `<OperationId>:charge`; the log's `ProposedCredits` is not a ledger debit.

### Stop and rollback conditions

Immediately stop collection and roll back if any of the following occurs:

- Any matching Shadow event has `ChargedCredits != OperationCredits`, or a production wallet/ledger debit differs from the Legacy charge.
- Any negative wallet or duplicate ledger key/settlement is found.
- A Shadow telemetry failure is followed by a user-visible 5xx, timeout, or failed answer attributable to that telemetry path.
- Any provider call is confirmed to have been added only for Shadow accounting.
- The approved API error/latency threshold from the 24-hour baseline is exceeded, a reservation/ledger reconciliation fails, or billing events lose stable `OperationId` values.
- The log source is missing, exposes prompts/PII to an unauthorized audience, or cannot support the agreed retention/access controls.

Before starting, record the numeric API error/latency thresholds against the measured baseline; do not invent them after an alert. An incomplete cost sample is a calibration hold: retain raw token data and report coverage, but do not publish a definitive cost conclusion.

### Rollback commands and post-rollback verification

Set only the mode back to `Legacy`, then recreate the API using the same image. Do not reverse migrations or edit billing rows.

```bash
cd /opt/sapio/financial-copilot
if grep -q '^Billing__UsageBasedCredits__Mode=' .env; then
  sed -i 's/^Billing__UsageBasedCredits__Mode=.*/Billing__UsageBasedCredits__Mode=Legacy/' .env
else
  printf '\nBilling__UsageBasedCredits__Mode=Legacy\n' >> .env
fi
docker compose up -d --no-build --force-recreate api
docker compose ps api
docker compose exec api sh -lc 'printenv Billing__UsageBasedCredits__Mode || true'
curl -fsS http://127.0.0.1:5074/health
```

Verify the container reports `Legacy`, health is successful, and no new `Mode=Shadow` evaluation appears after the restart. Confirm the wallet, reservation, and ledger checks above; compare actual deductions with the Legacy operation-price baseline. Existing read-only Shadow telemetry may remain under the approved retention policy. Do not reset credits, delete ledger entries, or revert an additive migration.

## 5. Usage collection method

Collect passively from normal successful requests after activation. Target about **200 distinct logical questions**, aiming for roughly 25 per category when normal traffic supports it:

1. Simple specialist questions
2. Financial statement queries
3. Monthly sales trend queries
4. Feature 141 single-company queries
5. Cross-company discovery
6. Filtering and ranking
7. Multi-step semantic queries
8. Fallback and clarification workflows

Use stable `OperationId` to count and join events. For API retries, the caller must send the same request-body `idempotencyKey`; the response header `Idempotency-Key` confirms the operation ID. If callers omit the body field, a retry receives a new ID, so do not claim exact logical-question deduplication without an existing request-level dedupe signal. Do not store or export prompts to make categories. Map only observed `ExecutionRoute` values to the eight labels in a small `--categories` JSON file.

Export only the required structured usage events and minimized billing columns. Keep the operation ID for joins; omit account, actor, tenant, external-user, prompt, and response-text fields. Use the approved restricted log and database export path, encrypt the files at rest, and delete them according to the approved retention period. Do not replay requests, add test prompts, call providers for missing usage, or send extra LLM calls for calibration.

The analyzer expects normalized JSONL events and these billing CSV fields:

- Ledger: `IdempotencyKey`, `OperationCode`, `CompletionStatus`, `CreditsCharged`, `PricingPolicyVersion`, `OccurredAt`.
- Reservations: `IdempotencyKey`, `Status`, `ReservedCredits`, `CreatedAt`.

Normalize the existing structured log event properties to top-level fields and preserve the event names and `OperationId`. The production Console sink has no verified structured-export configuration in this repository; use an already approved structured log store/export, or hold collection until one is confirmed. Do not export full application logs as a substitute.

## 6. Calibration execution commands

Prepare a dated price manifest for each effective rate window. This public reference example is explicitly not an account contract:

```json
{
  "version": "public-rate-reference-2026-10-29",
  "asOfUtc": "2026-10-29T00:00:00Z",
  "currency": "USD",
  "models": [
    {
      "providerKey": "OpenAI",
      "modelKey": "gpt-5.6-luna",
      "inputPerMillionTokens": 0.20,
      "cachedInputPerMillionTokens": 0.02,
      "outputPerMillionTokens": 1.20,
      "pricingConfidence": "VERIFIED_PUBLIC_RATE",
      "cacheWriteUsageRequired": true,
      "cacheWrite30mInputMultiplier": 1.25,
      "cacheWrite12hInputMultiplier": 2.5,
      "longContextThresholdInputTokens": 272000,
      "longContextInputMultiplier": 2.0,
      "longContextOutputMultiplier": 1.5
    }
  ]
}
```

Run the existing analyzer against the passively collected, privacy-minimized exports. Its default `CostPerCredit` scenarios are `$0.0050`, `$0.0075`, `$0.0100`, `$0.0125`, `$0.0150`, and `$0.0200`.

```powershell
python .\specs\147-usage-based-ai-credit-billing\analyze_shadow_calibration.py `
  --logs .\restricted\feature147\production-shadow.jsonl `
  --prices .\restricted\feature147\prices-2026-10-29.json `
  --ledger .\restricted\feature147\ledger.csv `
  --reservations .\restricted\feature147\reservations.csv `
  --categories .\restricted\feature147\route-categories.json `
  --sample-provenance PRODUCTION_SHADOW `
  --multiplier 1.0 --minimum-credits 1 --maximum-credits 5 `
  --out .\restricted\feature147\calibration-2026-10-29
```

For definitive account-cost statements, use an effective dated provider contract/invoice manifest with `pricingConfidence: VERIFIED_CONTRACT`. With the public example above, costs are modeled at the published rate only. The analyzer records price-manifest hash/date and emits question/call CSV plus a summary JSON; review and remove operation identifiers from any broader report after joins are complete.

## 7. Data-quality requirements and analysis

For each of the approximately 200 operation IDs, retain/report: provider and model IDs, LLM call count, input/cache-read/cache-write/output tokens, provider cost when supplied, pricing confidence, Legacy credits, proposed credits, reservation status, settlement outcome, route/workload, fallback use, and whether the five-credit cap applied.

The analyzer reports P50/P90/P95/P99 and average modeled cost, calls per question, token totals, hypothetical credit distribution from 1–5, cap frequency and cap exposure, rounding overage, Legacy-versus-proposed comparison, workload/route grouping, completeness, and price-confidence coverage. Keep incomplete observations in raw-usage coverage; do not treat missing cache/token fields as zero. Only complete observations with contract confidence and real provenance qualify as definitive cost.

The current adapter's attempt field does not establish provider-side HTTP retry totals. Do not infer retry costs or expensive prompt stages where the data lacks a complete per-call workload. Do not add category-based fixed charging rules. Feature 141 is genuinely more expensive only if the complete, contract-priced observed distribution shows that result against specialist routes; no current production sample establishes it.

## 8. Production safety assessment

The focused code and integration checks support the central Shadow isolation invariant, and the operation-based charge remains the actual wallet/ledger settlement. However, the mode is global for the configured operation codes, the runtime production `.env` is unverified, and the available repository evidence does not establish a structured, access-controlled production export. Public rates do not establish actual account expense. Production Shadow is not enabled by this runbook.

No Production Shadow logs, ledger export, reservation export, provider invoice, or account contract were available. Historical Feature 141 harness artifacts are not production shadow observations and lack the full per-call cache/stage fields needed for complete AI cost accounting. No real usage calibration was run and no cost or profitability results are inferred.

## 9. Remaining blockers

### PRE_ACTIVATION_BLOCKER

- This workspace has no authorized production runtime connection. The server `.env`, effective container mode, deployed image identity, and production log retention/access therefore remain unverified. The deployment operator must check these on the server before changing `.env`.
- The operator must confirm Docker log access and privacy controls, capture the 24-hour baseline, record numeric error/latency thresholds, and obtain explicit approval for the global Shadow window.
- Per-call usage-record failures in `AiModelExecutionService` are caught without a dedicated warning. Before activation, confirm the existing provider-attempt telemetry can be compared with operation-level `ModelCalls`; if that correlation is unavailable, usage-observation gaps cannot be reliably surfaced without a code change.
- The release owner must confirm the production image includes the tested fail-open changes before enabling the mode. No image was built or deployed in this operational preflight.

### POST_ACTIVATION_CALIBRATION_REQUIREMENT

- Collect the approximately 200-question representative sample passively after approval and activation. A missing real Shadow sample does not prevent initial Shadow activation.
- Obtain account-specific price evidence before making definitive account-cost or profitability claims. Until then, public rates are modeling references only.

## 10. Activation recommendation

Do not activate Shadow from this workspace. Source-level safety checks and the focused tests pass, but effective production configuration, the deployed image, and production log access were not verifiable here. An operator must complete the pre-activation checks above and provide explicit authorization. The real usage sample is a post-activation calibration requirement, not a pre-activation blocker.

FEATURE_147_SHADOW_PREFLIGHT_BLOCKED
