# Feature 147 — Billing Hardening Report

**Date:** 2026-10-29
**Scope:** billing reliability hardening and focused verification; no commit or push.

> **Historical snapshot:** this report predates production verification. The later PostgreSQL pass added a durable request fingerprint migration and deterministic replay after a concurrent settlement loses the unique-index race. See [shadow-readiness-report.md](shadow-readiness-report.md) for the current state and evidence.

## 1. Root causes

1. **Low-balance subsidy:** Enforced mode previously held the operation price and capped the measured charge at that amount. A one-credit wallet hold could therefore authorize more costly work and collect only one credit.
2. **Fallback double settlement:** semantic handling and Feature 141 could each own a reservation/finalization path. A semantic no-data result could settle before Feature 141 completed, splitting both the charge and the question's usage.
3. **Wrong usage key in fallback:** the Feature 141 derived reservation used a different correlation key from the model calls, so the final charge could omit earlier calls.
4. **Retry identity:** correlation IDs identify HTTP executions and may change on a retried request. They were insufficient as a stable key for one logical submitted question.
5. **Concurrency exposure:** reservation creation and upgrades needed database-level wallet revision fencing in addition to an in-process per-account lock, plus recovery from competing idempotency inserts.
6. **Metering gaps:** per-call provider/model details and cached input were incomplete, mixed provider currencies could not safely be summed, and the singleton accumulator needed bounded retention.

## 2. Files modified

- **Billing domain and contracts:** `src/backend/FinancialCopilot.Billing/Contracts/BillingContracts.cs`, `Pricing/UsageBasedCreditPolicy.cs`, and `Services/UsageBasedCreditCalculator.cs`.
- **Application orchestration:** `src/backend/FinancialCopilot.Application/AI/ModelProviders/AiModelContracts.cs`, `AI/Orchestration/AiOrchestrationContracts.cs`, `AI/Orchestration/AiQueryOrchestrationService.cs`, and `AI/Orchestration/SemanticCapabilityExecutionContracts.cs`.
- **Infrastructure billing and orchestration:** `src/backend/FinancialCopilot.Infrastructure/Billing/AiFacadeBillingHook.cs`, `Billing/UsageBasedCreditBillingOptions.cs`, `Billing/Persistence/UsageReservationAuthorizationService.cs`, `ServiceCollectionExtensions.cs`, `Authentication/TelegramMembershipService.cs`, `AI/ModelProviders/{ConfiguredHostedAiModelClient,DeepSeekHostedAiModelTransport,InMemoryAiExecutionUsageAccumulator,OpenAiHostedAiModelTransport}.cs`, `AI/OrchestrationV2/FinancialCopilotAgentWorkflowRunner.cs`, `AI/OrchestrationV2/Functions/BillingFunctions.cs`, `AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs`, and `AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowMessages.cs`.
- **API and customer outputs:** `src/backend/FinancialCopilot.API/Contracts/AiFacadeContracts.cs`, `Controllers/AiFacadeController.cs`, `Controllers/UsageController.cs`, and `appsettings.json`.
- **Chat request identity:** `src/frontend/src/lib/chat.functions.ts` and both chat submission routes. Each submission gets one UUID that is reused if its HTTP request is retried. This small request-side change is needed to preserve logical-operation identity across retries; the response shape and visible usage UI remain unchanged.
- **Verification:** the new Feature 147 unit, integration, and PostgreSQL test files, plus the existing architecture test's recognition of the new semantic coordinator method.
- **Documentation:** `spec.md`, `tasks.md`, the historical note in `implementation-report.md`, and this report.

No database migration, dependency, new billing subsystem, or production mode change was added. `Legacy` remains the default.

## 3. Architectural decisions

- Reused the existing reservation, wallet, ledger, finalization, outbox, and billing facade abstractions. The upgrade contract is implemented by the existing reservation authorization service.
- Enforced mode begins with the configured minimum hold, normally one credit. Before MAF/Feature 141 or another configured expensive path, it upgrades the same hold to the measured proposal when available or the configured question maximum otherwise. An insufficient upgrade releases the hold and refuses before that path begins.
- One logical question keeps one reservation and one usage correlation through semantic-to-Feature-141 fallback. A semantic result that can continue defers settlement; finalization claims all completed calls once.
- An optional API `IdempotencyKey` becomes the billing operation ID. The first-party chat client supplies a UUID per submission. Older/external callers without a key fall back to the correlation ID for compatibility.
- Legacy uses the existing operation charge. Shadow uses that same charge while computing and logging a hypothetical usage charge. Enforced uses the whole-number usage proposal and leaves zero-charge outcomes at zero.
- Decimal arithmetic, cached-token subset handling, provider/model-specific prices, missing-usage fallback, unknown-model handling, policy version, and same-currency cost rules remain server-side. No token counts are fabricated. Customer chat and usage responses suppress internal `EstimatedCost`.
- No prompt or response text was added to billing telemetry. Evaluation logs and model-call logs contain logical ID, route, model, token counts, legacy/proposed/charged credits, modeled cost and recovered value/delta, currency, fallback/cap state, policy version, and settlement outcome.

## 4. Billing lifecycle before and after

**Before:** reserve operation price → semantic route → possibly finalize → Feature 141 may create a derived reservation → settle at most the reserved amount. A cheap hold could undercharge an expensive completion, fallback could split one question into two settlements, and retry identity could change with the correlation ID.

**After:** create/reuse the stable operation reservation → hold the minimum → run the inexpensive route → atomically top up before an expensive route → retain the same reservation and usage correlation through any Feature 141 fallback → finalize once under a stable ledger key → release unused credits with the existing finalizer. A replay returns the existing ledger result.

Legacy and Shadow retain the legacy charge amount. The progressive top-up and usage-based integer charge apply only in Enforced mode.

## 5. Concurrency and idempotency guarantees

- Reservation creation and upgrade serialize per account within an application instance. Database wallet `Revision` concurrency fencing protects against concurrent application instances. A stale reservation snapshot refreshes and retries once.
- Reservation and ledger idempotency keys are unique at persistence. If a competing request wins reservation creation, the loser reads and reuses the compatible persisted reservation. Settlement replays by ledger key and checks account, actor, tenant, API client, and operation identity.
- Upgrade writes the reservation amount, wallet hold, and outbox event in one `SaveChanges` transaction. The existing finalizer atomically commits the ledger, wallet, reservation, and outbox state; unused credits are released.
- The usage accumulator's per-reservation claim is idempotent, so a retry after a transient failure before commit can claim the same completed calls again. A new integration test injects one pre-commit `DbUpdateException` and verifies successful retry and replay leave one ledger charge.
- A stable retry key is guaranteed for first-party chat submissions. External API clients must send the optional idempotency key to guarantee replay protection across requests that use different correlation IDs; correlation fallback only deduplicates requests that keep the same correlation.
- Real PostgreSQL concurrent reservation and settlement verification remains outstanding because Docker was unavailable. See §8.

## 6. Test results

| Check | Result |
|---|---:|
| Unit billing and domain tests (`UsageBasedCreditBillingTests` + `BillingDomainTests`) | **51/51 passed** |
| Feature 147 billing integration tests (registered hook, V2 endpoint, Legacy/Shadow modes, transient retry) | **18/18 passed** |
| Existing billing persistence and endpoint tests | **35/35 passed** |
| Provider adapter integration tests (`AiModelProviderAdapterTests`) | **16/16 passed** |
| Persisted chat message billing display (`message-list.test.tsx`) | **8/8 passed** |
| Frontend production build (`npm run build`) | **Succeeded**; existing route-file and large-chunk warnings remain |
| Architecture tests | **11 passed, 1 failed**; see §7 |
| PostgreSQL/Testcontainers billing tests | **Not verified**; Docker unavailable, see §8 |
| Live LLM smoke test | **Not run**; no authorized backend LLM configuration was identified for a bounded request |

The semantic-to-Feature-141 billing test drives the registered billing hook with model calls representing both route stages; it does not execute a live Feature 141 LLM route. The API, Infrastructure, Billing, Application, and integration test projects compiled as part of the focused .NET test runs. No full test-suite run was performed.

## 7. Regression failures and baseline comparison

- The architecture failure is `MigratedSemanticExecutors_DoNotReintroduceLegacySymbolExtraction`: the guard finds `FinancialStatementTableIntentRules` in `SemanticCapabilityExecutors.cs`. That implementation file is untouched by Feature 147, and the same 11/12 result was recorded in the original implementation report. The architecture test was adjusted only to recognize the new semantic coordinator entry point; the unrelated semantic executor was not edited.
- The original implementation report records a broader integration comparison of 210 passed and 16 failed, with the same 16 failures after temporarily restoring Feature 147 files to `HEAD`. Those failures concern existing financial table contents and routing assertions. This pass did not repeat that broad comparison; its focused existing billing persistence and endpoint checks passed 35/35.
- The frontend build emitted a warning for the existing `auth-closed-registration.test.tsx` route file and a large generated chunk. The build succeeded; neither warning is from billing changes.

## 8. PostgreSQL verification

Two PostgreSQL-backed tests are present: concurrent reservations from one wallet revision must not overbook, and concurrent settlement/replay after reservation upgrade must leave one ledger charge and consistent wallet state. A prior filtered probe found the Docker/Testcontainers prerequisite unavailable and skipped the then-existing PostgreSQL case. The second case was added afterward and compiles with the integration project, but neither case has been executed against PostgreSQL in this pass. The in-memory tests do not establish cross-instance transaction behavior.

## 9. Remaining risks

- Provider token prices and `CostPerCredit` remain placeholders, not verified contract prices. Shadow results are not suitable for commercial calibration until the price table and product pricing inputs are approved.
- PostgreSQL concurrency semantics have not been exercised in this environment. Run both new PostgreSQL tests against the supported production PostgreSQL version before enabling Enforced.
- Non-first-party clients can omit `IdempotencyKey`; retries that use a new correlation ID can then start a new logical billing operation.
- A low-cost dialogue/semantic routing call may run under the minimum hold before its measured top-up is known. If the user cannot cover the next route, the expensive route is stopped and the hold released, but the small routing call's provider cost may remain unrecovered.
- A live LLM request was not made. Provider-reported usage parsing has adapter tests, but real account-specific payload behavior and route cost distribution still need observation.
- Shadow has not been run against production traffic. Internal cost/recovery telemetry must be reviewed with access limited to operational staff.
- The response API test verifies credits and hides estimated cost; the existing frontend history test verifies the saved assistant message still renders the credit-use label. No new history persistence contract was introduced.

## 10. Shadow Mode readiness verdict

**Implementation ready for a controlled Shadow rollout after configuration.** Legacy charging is preserved, hypothetical charges are calculated and logged without being applied, and the needed token/route/recovery fields are available. Production Shadow calibration is not complete: replace placeholder model rates, approve the pricing basis, deploy Shadow, and review real traffic before deciding on Enforced.

## 11. Enforced Mode readiness verdict

**Not production-ready and must remain disabled.** The progressive reserve/upgrade and one-charge fallback changes are implemented and covered by focused tests, but provider prices are unverified, PostgreSQL concurrency tests have not run, production Shadow calibration and product approval are outstanding, and retry safety for external clients depends on them supplying an idempotency key. Production configuration remains `Legacy`.

FEATURE_147_HARDENING_COMPLETE_WITH_BLOCKERS
