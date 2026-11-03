# Feature 147 — Production Verification & Shadow Calibration

**Verification date:** 2026-10-29  
**Verdict:** `FEATURE_147_SHADOW_READY_WITH_BLOCKERS`  
**Scope:** code, a disposable local PostgreSQL instance, repository configuration, existing Feature 141 artifacts, and current official provider documentation. No production database, deployment runtime, invoice, or private provider contract was accessed.

## Decision

The billing changes now have PostgreSQL evidence for reservation fencing, concurrent settlement replay, idempotency conflicts, and rollback after a failed write. Focused billing/API/provider regressions pass. Shadow mode is isolated from wallet charging in the tested composition, and the inspected base configuration remains `Legacy`.

Do **not** activate production Shadow yet. The deployed account rates are unconfirmed, the configured price rows are explicitly unverified placeholders, OpenAI cache-write usage is not captured by the application, and no production Shadow sample exists. Enforced mode is **NO-GO**.

## 1. Production blocker resolution matrix

| Gate | Result | Evidence | Remaining action |
|---|---|---|---|
| PostgreSQL wallet and settlement safety | **Verified locally** | Seven Feature 147 tests ran through EF migrations on unique disposable PostgreSQL databases. Same-wallet concurrent reservations cannot overbook; a settlement uniqueness race replays the persisted result. | Repeat in the deployment’s supported PostgreSQL version/managed environment before release. |
| End-to-end billing idempotency | **Implemented; billing replay verified** | Stable operation IDs, SHA-256 request fingerprints, unique reservation/ledger/outbox keys, conflict response, and PostgreSQL retry tests. | API callers must send and retain a key. Same-key retries can still execute the AI workflow again; there is no durable response cache. |
| Provider tariff and account pricing | **BLOCKED** | Current official public tariffs are documented below, but no authorized account rate or invoice was available. Repository price rows have `PriceVerified=false`. | Finance/provider owner must confirm the applicable account rates and update a verified manifest/configuration through the normal release process. |
| Shadow wallet isolation | **Verified in focused integration test** | Shadow computes a proposal while reserving/settling the legacy operation charge; the tested wallet changes by the legacy one credit, not the four-credit proposal. Monetary `EstimatedCost` is not returned in the customer response. | Keep the inspected base mode at `Legacy` until rates and metering are approved. |
| Production calibration sample | **BLOCKED / pending** | The inspected base configuration is `Legacy`; no Feature 147 Shadow log export or production ledger/reservation export was supplied. | Collect an approved production Shadow window after the pricing and cache-write gates are closed. |
| Feature 141 token attribution | **PARTIAL** | Existing E2E artifacts provide question-level totals and some step latencies, not per-step tokens or cache-read/write counts. | Instrument per-call step labels and token details before drawing prompt-level cost conclusions. |
| Enforced activation | **NO-GO** | Enforced startup validation rejects missing/unverified model prices; shadow pricing evidence is absent. | Require a reviewed, representative shadow sample and explicit pricing approval first. |

## 2. PostgreSQL verification

The tests used the existing loopback PostgreSQL service on port `55441`, admin database `postgres`, and the guarded `FINANCIAL_COPILOT_TEST_POSTGRES_CONNECTION_STRING` test hook. The fixture created uniquely named `feature147_billing_<guid>` databases, applied migrations with `MigrateAsync`, and dropped only those test databases. It did not connect to an application or production database. A test query confirmed PostgreSQL `read committed` isolation.

**Result: 7 passed, 0 failed, 0 skipped** (`UsageBasedCreditBillingPostgreSqlTests`). The cases verified:

- Two concurrent reservations against one wallet revision cannot both reserve four credits from a six-credit wallet. One reservation persists; the other is rejected; wallet balance remains six and reserved amount is four.
- Two independent EF contexts that load the same wallet revision are fenced by the PostgreSQL concurrency token: one update succeeds and one raises `DbUpdateConcurrencyException`.
- A reservation upgrade from one to five credits updates the wallet hold, and two concurrent settlements for the same logical operation converge on one committed ledger charge. The loser of the outbox/unique-index race rereads the committed reservation and ledger and returns an idempotent replay.
- Concurrent reservation inserts with the same key converge on one row; a different request fingerprint is rejected.
- Concurrent upgrades keep the reservation at the largest required capacity.
- Failed answer paths release their reservation.
- A trigger-injected ledger write failure rolls back the attempted settlement; a retry commits once and subsequent replay remains single-charge.

The verified database constraints include unique indexes on `billing_usage_reservations.IdempotencyKey`, `billing_usage_ledger_entries.IdempotencyKey`, and `billing_outbox_messages.IdempotencyKey`. Wallet `Revision` and reservation `Status` are EF concurrency tokens. The in-process per-account semaphore reduces local contention; the wallet revision token and unique database indexes are the cross-instance correctness fences.

Migration `20261029150802_AddBillingReservationRequestFingerprint` adds nullable `RequestFingerprint varchar(64)` to `billing_usage_reservations`. The disposable migration path applied it successfully. This additive nullable change supports old rows; it does not prove a production rollout has applied the migration.

## 3. Idempotency API contract

Endpoint: `POST /api/ai/v1/query`.

1. Send a non-empty GUID in the JSON body as `idempotencyKey`. Generate one key per logical question and reuse it for every transport retry. First-party web chat creates the key outside the mutation function; thread chat now passes it in the mutation variables so a mutation retry reuses the same value. Pagination actions are new billing operations and get new keys. Telegram derives a stable operation GUID from the Telegram update/callback identity.
2. The API returns the selected key in the `Idempotency-Key` response header. For backwards compatibility, a missing body key is replaced with a server-generated GUID. The request header is currently **not** an input; the JSON body is the contract. A caller that omitted the key and lost the first response cannot safely deduplicate its retry, so new API clients should always supply the key.
3. `Guid.Empty` is rejected with HTTP 400. Reusing a key with a different stable payload returns HTTP 409 `ProblemDetails` (`Idempotency key conflict`). A same-payload replay can return success and does not charge the wallet again.
4. The stored version-1 SHA-256 fingerprint covers the original caller message, conversation ID, scanner/disclosure paging, alert/insight/monthly-report context IDs, and suggested action. It excludes trace/correlation IDs and server-derived context; raw prompts are not stored in the fingerprint column. Alert-backed API requests now preserve `OriginalUserMessage` before adding current alert context, so that context changes do not change the caller-payload fingerprint.
5. Reservation and charge keys derive from `BillingOperationId`, not correlation IDs. The same request/fingerprint flows through V1 and V2 billing hooks and the Feature 141 fallback. Existing rows with a null fingerprint remain replay-compatible, but cannot reject a changed payload based on a fingerprint that was never stored.

The API test now verifies the response key, replay with a different trace, one persisted charge, and HTTP 409 on changed payload. PostgreSQL tests verify concurrent insert/settlement behavior. **Residual limitation:** same-key replay is billing-idempotent, not execution-idempotent. The endpoint does not cache/replay the original answer before orchestration, so a retry may call the provider and persist another conversation exchange even though finalization reuses one ledger charge.

## 4. Provider pricing inventory

The prices below are USD per million tokens. “Public tariff verified” means the linked provider documentation currently lists the rate; it does **not** establish the rate on Financial Copilot’s account or contract.

| Runtime/config entry | Current public tariff checked 2026-10-29 | Configured row / status | Provider-specific accounting and result |
|---|---|---|---|
| OpenAI `gpt-5.6-luna` (active model in the inspected repository configuration) | Input **$0.20**, cached input **$0.02**, output **$1.20**. OpenAI also lists cache writes at **1.25× input** (equivalent to $0.25/1M at this rate). The model page does not state a tariff effective date; 2026-10-29 is the documentation check date. | Existing row: $1.25 / $0.125 / $10.00, `PriceVerified=false`. Account-effective rate: **`UNVERIFIED_PRICING`**. | The OpenAI transport parses `input_tokens`, `output_tokens`, and `input_tokens_details.cached_tokens`. It does not parse/log `cache_write_tokens`; exact cost can be understated if cache writes occurred. Reasoning tokens are included in billed output usage. |
| DeepSeek `deepseek-chat` (price placeholder only; not an enabled provider registration in the inspected configuration) | Current official model is `deepseek-flash` / DeepSeek-V4.1-Flash-1017. Cache hit: **$0.003 off-peak / $0.006 peak**; cache miss: **$0.15 / $0.30**; output: **$0.60 / $1.20**. The provider says these rates took effect 2026-09-10 04:00 UTC and its 2026-10-17 model update kept pricing unchanged. | Existing `deepseek-chat` row: $0.27 / $0.07 / $1.10, `PriceVerified=false`. Exact configured model/rate: **`UNVERIFIED_PRICING`**. | The name does not match the current published model name. Peak pricing depends on UTC time (weekdays 01:00–04:00 and 06:00–10:00, excluding Chinese public holidays); the current calibration script does not schedule peak/off-peak rates. Do not map this row to `deepseek-flash` without approval. |

Sources: [OpenAI GPT-5.6 Luna model and pricing](https://developers.openai.com/api/docs/models/gpt-5.6-luna), [OpenAI prompt caching and cache-write accounting](https://developers.openai.com/api/docs/guides/prompt-caching), [DeepSeek models and pricing](https://api-docs.deepseek.com/quick_start/pricing/), and [DeepSeek V4.1-Flash model update](https://api-docs.deepseek.com/updates/?release=deepseek-coder).

The inspected base configuration uses `Mode=Legacy`, `CostPerCredit=USD 0.0125`, multiplier `1`, minimum `1`, cap `5`, and missing-usage fallback `1`. These are billing conversion settings, not verified provider costs, subscription revenue, or commercial margin. `PriceVerified` defaults to false; unverified rows are excluded from the policy, and Enforced startup validation rejects missing/unverified price rows. No `appsettings.json` value was changed for this verification.

## 5. Shadow isolation and rollback

The inspected code keeps Shadow’s wallet settlement on the operation-based legacy charge. It separately calculates and logs `ProposedCredits`, `ActualCost`, `ActualCredits`, `RecoveryDelta`, route, model-call count, unmeasured calls, token totals, fallback, cap, policy parameters, and logical `OperationId`. Model-call logs are grouped under that operation ID. The response mapping supports `EstimatedCost`, but the billing hook returns null for it, so provider dollars are not included in customer-facing output.

`UsageBasedCreditShadowModeTests.ShadowMode_ChargesTheLegacyCredit_WhileEvaluatingTheUsageBasedCharge` verified a four-credit proposal while the reserved/charged amount and wallet decrement stayed at one legacy credit. `LegacyMode_ChargesOnlyTheOperationPriceAndDoesNotClaimUsage` verified the Legacy behavior. These tests establish isolation in the test composition, not deployed environment state. Because unverified prices are filtered, Shadow mode with current repository rates would produce fallback proposals and misleading zero tariff cost; do not use it for economic calibration.

Rollback is to set `Billing:UsageBasedCredits:Mode` back to `Legacy` and restart/redeploy the service. The nullable fingerprint column can remain; routine Shadow rollback does not require a schema rollback. Enforced remains disabled.

## 6. Calibration data availability and runbook

There is no production Shadow sample in this verification. The repository base configuration is Legacy, and no deployed telemetry or production ledger export was queried. Do not treat the Feature 141 test harness counts below as production observations.

The prepared [`analyze_shadow_calibration.py`](./analyze_shadow_calibration.py) joins normalized Feature 147 JSONL events with read-only ledger/reservation CSV exports by logical operation key. It uses only `verified: true` USD rates, records the price-manifest version/date/SHA-256, and fails closed for a model that declares a cache-write premium when the log lacks write-token counts. It produces `questions.csv` and `summary.json` with per-question route/category, calls/models/tokens, modeled provider cost, legacy and hypothetical credits, reservation/settlement status, cap hits, cost percentiles, 1–5 credit distribution, category averages/medians, cost-per-credit sensitivities, and rounding overage. Categories come from the logged execution route; no category price is hardcoded. Provider cost remains separate from subscription revenue and margin.

Use one frozen, verified price snapshot for each log window (the script does not automatically choose historical rates or model DeepSeek peak/off-peak schedules). For GPT-5.6 Luna, include `cacheWriteInputMultiplier: 1.25`; until application logs contain `write=<tokens>`, those calls will be marked unpriced rather than silently treated as ordinary input. The current application emits `cached=<tokens>` but not cache-write tokens, so current OpenAI shadow events are not sufficient for full tariff cost.

After production Shadow is approved, export only the sample window with read-only access and run:

```powershell
python specs/147-usage-based-ai-credit-billing/analyze_shadow_calibration.py `
  --logs <normalized-shadow-events.jsonl> `
  --prices <verified-price-snapshot.json> `
  --ledger <billing-usage-ledger.csv> `
  --reservations <billing-usage-reservations.csv> `
  --categories <execution-route-categories.json> `
  --cost-per-credit 0.0125,0.01,0.02 `
  --out <private-calibration-output>
```

The script was syntax-checked and its CLI help was invoked. No production or synthetic question sample was generated. Percentiles, average/median credits, credit distribution, cap percentage, recovery ratios, and rounding impact remain **pending real data**.

## 7. Feature 141 cost diagnostics

Available token evidence is from `specs/141-schema-aware-semantic-query-engine/evaluation/engine-e2e-results.json`, an `ENGINE_E2E_PARTIAL` test-harness artifact from 2026-10-29, not production Shadow telemetry.

| Harness slice | Calls | Input tokens | Output tokens | Step latency available |
|---|---:|---:|---:|---|
| Vertical slice | 2 | 7,426 | 1,242 | Intent 8,266 ms; navigator 6,889 ms |
| Monthly production | 3 | 14,846 | 2,192 | Intent 5,725 ms; navigator 10,905 ms |
| Product discovery live run | 3 | 17,828 | 2,250 | Aggregate only |
| Cross-company cases | 1–2 | 2,591–7,748 | 345–1,068 | Aggregate only |
| Threshold/filter cases | 1–2 | 2,592–7,891 | 661–1,356 | Aggregate only |

The evidence does **not** identify which step consumes the most input or output tokens: token counts are aggregated per question/run, not split by intent, navigation, or synthesis call. The available latency data also do not show one consistent slowest step. No cache-hit or cache-write counts are present in the artifact, so cache effectiveness cannot be established. The OpenAI transport uses Responses API `previous_response_id` and sends continuation items rather than replaying the full prior message payload, but the available counters cannot establish the billed reuse or cache-read savings.

The prior [Feature 141 production-data pilot report](../141-schema-aware-semantic-query-engine/evaluation/production-data-pilot-report.md) remains a separate product-readiness warning: its original 10-question pilot recorded 0 pass, 6 fail, and 4 blocked; a later company-mapping fix resolved one identified statement join issue, while the report still recommends NO-GO for real users because other capability and billing blockers remained. The current focused `Feature141ChatMvpTests` were skipped because live/provider opt-in was unavailable in this run.

Optimization recommendations, ranked by potential impact and implementation complexity:

1. **Add per-call usage attribution (high impact, low–medium complexity):** log step name/call index, provider/model, input, cache-read, cache-write, output, timestamp, route, operation ID, and retry attempt. This is the prerequisite for identifying the true cost-heavy step and pricing cache writes.
2. **Measure cache reads/writes on a fixed workload (potentially high impact, low–medium complexity):** use identical Feature 141 prompts and model settings, preserve the stable prefix, and compare billed cache-read/write tokens and total cost. OpenAI documents that prompt caching is automatic for supported models, but a session or repeated context does not guarantee a cache hit.
3. **Reduce the schema outline/context only after accuracy evaluation (potentially high input savings, medium–high complexity):** test narrower catalog excerpts and retrieval context against existing cross-company, filter, monthly, and refusal cases; preserve current semantic results.
4. **Remove a route/planning call only after call traces prove it is redundant (unknown savings, medium complexity):** observed 1–3 calls per harness question do not by themselves establish waste. Do not refactor Feature 141 as part of Feature 147.

## 8. Regression results

| Verification | Result |
|---|---|
| Focused Feature 147, billing-domain, and billing-service unit tests | **63 passed, 0 failed** |
| Focused Feature 147, persistence, API endpoint, V2 endpoint, provider adapter, and Feature 141 chat integration filter | **51 passed, 0 failed, 10 skipped** (live Feature 141/provider-opt-in cases) |
| Feature 147 PostgreSQL concurrency/migration suite | **7 passed, 0 failed, 0 skipped** against the disposable loopback instance |
| Frontend persisted message-list/history tests | **8 passed** |
| Frontend production build | **Passed.** Existing warnings remain for a test file discovered as a route and a client chunk larger than 500 kB. |
| Architecture tests | **11 passed, 1 failed.** The failure is `MigratedSemanticExecutors_DoNotReintroduceLegacySymbolExtraction`, which finds `FinancialStatementTableIntentRules` in the already-modified Feature 141 semantic execution source. It is unrelated to Feature 147 billing. |
| Calibration tool | Python syntax check and `--help` passed; no data sample was fabricated. |

The integration suite initially exposed a reservation-upgrade service-cast failure when an existing endpoint test substituted a reservation implementation. The DI fallback now fails closed only if an Enforced flow requests an unsupported upgrade; the final focused integration run passes.

## 9. Remaining risks and activation recommendations

- **Shadow:** Keep `Legacy` active. Before a production Shadow can produce credible economics, confirm an account-effective tariff for the exact runtime model, add OpenAI cache-write token telemetry through the adapter/log/calibration path, and verify the actual deployed configuration. Then collect and review a representative production sample across the requested route categories.
- **Enforced:** **NO-GO.** Require verified effective prices for every active model, accurate cached-read/write accounting, approved `CostPerCredit` and cap/fallback policy, and reviewed Shadow results before any Enforced rollout.
- **Retry semantics:** Require callers to reuse keys. Financial settlement replay is verified, but the API does not short-circuit a repeated workflow or return a stored answer, so provider spend and transcript duplication remain possible on a retry.
- **Legacy fingerprints:** Rows predating the migration have a null fingerprint and remain replayable for compatibility; payload mismatch protection is not retroactive for those rows.
- **Feature 141:** The token artifact cannot answer per-step cost attribution; the production-data pilot remains NO-GO in its own report. Billing calibration must not be used to imply Feature 141 product readiness.
- **Deployment evidence:** This report does not certify a live production migration, runtime mode, provider account contract, or production invoice. Those require deployment-owner/finance confirmation.

**Final status:** `FEATURE_147_SHADOW_READY_WITH_BLOCKERS`
