# Feature 136 Runtime Diagnosis

Date: 2026-10-04

## Executive conclusion

Feature 136 is not production-runtime ready in the current working tree. The
failure is multi-layered:

1. The active V2 semantic-primary route classifies product trend questions as
   `monthly_activity_trend`. Its early semantic return prevents the direct
   `MonthlyProductTrend` branch from running.
2. The accepted local data contains the requested products, but the persisted
   product identity is not stable across periods. The current repository also
   substitutes the generated `ProductCode` for missing provider identity, so
   the product resolver sees multiple candidates and returns `Ambiguous`.
3. The direct product result is carried through the live response path but is
   omitted from the workflow's `PersistAsync` call. Conversation reloads can
   therefore lose the typed product result and its frontend chart.
4. An end-to-end HTTP replay was blocked before query execution by the local
   billing configuration (`No billable customer account is configured for this
   actor`). This is an environment blocker, not evidence that the product path
   works.

The data decision is **B — targeted backfill/re-ingestion is required** after
the identity path is corrected. This is not a “data missing” case: the source
facts exist, but their persisted identity is insufficient for a stable
cross-period product trend.

## Scope and evidence

The four required prompts were traced:

| Prompt | Intended route | Current route before billing blocked replay | Diagnosis |
|---|---|---|---|
| `روند فروش کگل` | `MonthlyActivityTrend` | semantic `monthly_activity_trend` | Correct company-level route |
| `روند فروش گندله کگل` | `MonthlyProductTrend` | semantic `monthly_activity_trend` | Wrong: product request is treated as company trend |
| `روند فروش ماهانه کچاد` | `MonthlyActivityTrend` | semantic `monthly_activity_trend` | Correct company-level route |
| `روند فروش آهن اسفنجی کچاد` | `MonthlyProductTrend` | semantic `monthly_activity_trend` | Wrong: product request is treated as company trend |

Static route checks show that the new deterministic product rule does recognize
the two product prompts: `MonthlyProductTrendIntentRules` extracts `گندله` /
`کگل` and `آهن اسفنجی` / `کچاد`, while company-only prompts have no product
slot. That rule is not reached when a semantic frame is present.

Focused verification completed during this diagnosis:

- Feature 136 focused unit tests: **9 passed, 0 failed**.
- `GET http://localhost:5074/health`: **200 Healthy** while the local API was
  running.
- Exact API replay with a development token: **HTTP 500**, before semantic or
  deterministic query execution, due to the missing billable customer account.
- The local PostgreSQL database was queried read-only; no application or data
  mutation was performed by this diagnosis.

## 1. Routing and precedence

### Actual control flow

The active local configuration is:

- `AiOrchestration.Mode = MicrosoftAgentFrameworkV2`
- `SemanticRouting.DefaultMode = SemanticPrimary`
- `SemanticRouting.CanaryPercentage = 100`

The semantic interpreter has a generic trend capability. It scores
`monthly_activity_trend` for trend words and raises it to `0.95` when an
entity, sales word, and trend word are present. It has product-revenue-mix
keywords, but no registered semantic `monthly_product_trend` capability:

- [DeterministicCapabilityInterpreter.cs](../../src/backend/FinancialCopilot.Application/AI/Orchestration/DeterministicCapabilityInterpreter.cs:15)
- [DeterministicCapabilityInterpreter.cs](../../src/backend/FinancialCopilot.Application/AI/Orchestration/DeterministicCapabilityInterpreter.cs:63)
- [DeterministicCapabilityInterpreter.cs](../../src/backend/FinancialCopilot.Application/AI/Orchestration/DeterministicCapabilityInterpreter.cs:130)
- [CapabilityInterpretationGovernance.cs](../../src/backend/FinancialCopilot.Application/AI/Orchestration/CapabilityInterpretationGovernance.cs:73)

In `ExecuteAgentStepAsync`, any semantic frame except explicit product
comparison enters the semantic executor and returns at line 334. The direct
product predicate is calculated only after that early-return block. Therefore
the two product prompts never reach `MonthlyProductTrendQueryUseCase` in the
active semantic-primary configuration:

- semantic preemption: [FinancialCopilotWorkflowDefinition.cs](../../src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs:245)
- missing semantic payload case for `MonthlyProductTrendResult`: [FinancialCopilotWorkflowDefinition.cs](../../src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs:259)
- direct product branch that is bypassed: [FinancialCopilotWorkflowDefinition.cs](../../src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs:465)
- product use-case invocation: [FinancialCopilotWorkflowDefinition.cs](../../src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs:485)

The intended deterministic precedence is present only in the non-semantic
branch: explicit product comparison, then product trend, then company trend,
with ProductRevenueMix handled later. This does not protect the active
semantic-primary path. The result computation then independently labels a
product query as `MonthlyProductTrend`, even if the semantic branch populated
only `MonthlyActivityTrendResult`, creating contradictory metadata and payload
state:

- [FinancialCopilotWorkflowDefinition.cs](../../src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs:778)

### Routing conclusion

The primary runtime regression for the two product prompts is **semantic
preemption**, not a missing product parser. The active semantic route must
recognize the product slot and dispatch a typed product result, or the product
predicate must be allowed to take precedence before the generic semantic early
return. A product `NotFound` or `Ambiguous` result must remain a product result;
it must not degrade to company trend.

## 2. Data availability and identity

The read repository applies the required strict source predicate:

```text
ReportType = ProductSales
OutputType = 0
IsAccepted = true
```

This predicate is visible in
[EfCoreMonthlyProductComparisonRepository.cs](../../src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/EfCoreMonthlyProductComparisonRepository.cs:13)
and is used for both period discovery and period rows.

Read-only local database results for the requested companies:

| Company | External id | Accepted qualifying reports | Line items | Period span | Requested product coverage |
|---|---:|---:|---:|---|---|
| کگل | 4 | 17 | 150 | 2025-03-21 through 2026-07-23 | گندله: 17 periods, 17 rows with quantity/value |
| کچاد | 3 | 16 | 234 | 2025-03-21 through 2026-06-22 | آهن اسفنجی: 16 periods, 8 quantity/value rows; گندله: 8 periods, 8 quantity/value rows |

The required source facts are therefore present. The identity quality is the
problem:

- `ProviderProductCode` and `ProviderProductId` are null for the inspected
  target rows.
- The persisted `ProductCode` values are `PRODUCT:NATURAL:<hash>` and vary by
  period for the same displayed product. For example, exact-title `گندله`
  produces 17 distinct product codes for کگل; exact-title `آهن اسفنجی`
  produces 16 distinct product codes for کچاد.
- The repository maps `item.ProviderProductCode ?? item.ProductCode` into the
  observation's provider-code field:
  [EfCoreMonthlyProductComparisonRepository.cs](../../src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/EfCoreMonthlyProductComparisonRepository.cs:30)
- The product use case groups candidates by `ProductKey`, so the varying
  generated code prevents the intended title-plus-unit fallback from being
  used and produces multiple exact-title matches:
  [MonthlyProductTrendQueryUseCase.cs](../../src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/MonthlyProductTrendQueryUseCase.cs:27)

The product key contract itself correctly prefers provider code, then provider
id, then normalized title plus unit:

- [MonthlyProductTrendContracts.cs](../../src/backend/FinancialCopilot.Application/FinancialData/Ingestion/MonthlyProductTrendContracts.cs:101)

But the repository's substitution makes a generated line-item code look like
provider identity. This is the data/runtime interaction that will remain after
the routing defect is fixed.

### Backfill decision

**B — targeted backfill/re-ingestion required.**

Do not repair these rows by assigning arbitrary database keys. After the product
identity read path is corrected, re-ingest the relevant accepted ProductSales
history for کگل and کچاد, or run a source-derived, idempotent backfill that
populates stable provider product provenance where the provider supplies it.
Then verify that each requested product resolves to one company-scoped key
across its available periods. The existing migration is present in the working
tree and was applied to the local development database, but that does not repair
the existing source identity values.

## 3. Calculation and typed result

The calculation contract is present and unit-tested:

```text
SalesRate = SalesValueMillionRial * 100000 / SaleQuantity
```

It uses checked decimal arithmetic and returns typed statuses for missing,
zero, invalid, and overflow cases:

- [MonthlyProductTrendContracts.cs](../../src/backend/FinancialCopilot.Application/FinancialData/Ingestion/MonthlyProductTrendContracts.cs:76)

The use case calculates product points from the selected product rows and does
not use the company `MonthlyAverageSalesRate` field:

- [MonthlyProductTrendQueryUseCase.cs](../../src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/MonthlyProductTrendQueryUseCase.cs:104)

Because the API replay was stopped by billing before query execution, no
authoritative live JSON result was obtained for the four prompts. The focused
unit suite validates arithmetic and status behavior, but it does not prove the
active V2 semantic entry point.

## 4. Transport, persistence, and frontend

The typed result is present in the application contract, workflow records, API
response contract, controller mapping, frontend mapping, and product chart.
The live result path reaches `BuildFinalResponse` with
`MonthlyProductTrendResult`:

- [AiFacadeContracts.cs](../../src/backend/FinancialCopilot.API/Contracts/AiFacadeContracts.cs:62)
- [AiFacadeController.cs](../../src/backend/FinancialCopilot.API/Controllers/AiFacadeController.cs:524)
- [FinancialCopilotWorkflowDefinition.cs](../../src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs:1004)

There is, however, a persistence gap. `MessagePersistenceFunction.PersistAsync`
accepts and serializes `monthlyProductTrendResult`, but the workflow call does
not pass `msg.MonthlyProductTrendResult`:

- function parameter and payload field: [MessagePersistenceFunction.cs](../../src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Functions/MessagePersistenceFunction.cs:18)
- payload serialization: [MessagePersistenceFunction.cs](../../src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Functions/MessagePersistenceFunction.cs:80)
- missing argument in the workflow persistence call: [FinancialCopilotWorkflowDefinition.cs](../../src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs:927)

Consequences:

- a successful direct request may return the typed result in the immediate
  HTTP response;
- the assistant text can still be persisted;
- the typed product result is absent from persisted payload/reload;
- the frontend's typed dispatch cannot render the product chart after replay.

This is independent of the semantic routing defect and must be fixed before
claiming transport/replay completeness.

## 5. Fallback and legacy-rate checks

The direct product use case has explicit `Resolved`, `NotFound`, and
`Ambiguous` outcomes and does not call the company trend use case on resolution
failure. That part of the fallback contract is correct:

- [MonthlyProductTrendQueryUseCase.cs](../../src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/MonthlyProductTrendQueryUseCase.cs:41)
- [MonthlyProductTrendContracts.cs](../../src/backend/FinancialCopilot.Application/FinancialData/Ingestion/MonthlyProductTrendContracts.cs:3)

The active semantic-primary route is nevertheless a functional silent fallback:
the product query is executed as company activity before product resolution is
attempted. This violates the requirement that a product request never fall
back to a company trend.

No product calculation or chart path was found to read or map the legacy
company-level `MonthlyAverageSalesRate`; the product chart uses the typed
calculated rate. The company trend and product trend must still be regression-
tested separately after routing is corrected.

## 6. Deployment and runtime state

Repository/deployment evidence at diagnosis time:

- branch: `develop`
- `HEAD`: `72ce97f86db92f54fd53b40b08b9329b25c34402`
- no commit in the repository history contains the Feature 136 specification
  or implementation; the Feature 136 files and migration are uncommitted
  working-tree changes;
- the local development API was healthy on port 5074 and used the active V2
  semantic-primary settings;
- the Feature 136 migration was present in the local PostgreSQL
  `__EFMigrationsHistory` table;
- this does not establish that the migration or any Feature 136 binary/frontend
  bundle is deployed in production.

### Restart/redeploy decision

**RESTART/REDEPLOY REQUIRED: Yes.**

After code and data corrections are approved, production needs a backend build
and restart/redeploy, frontend build/deploy, migration verification, and a
targeted product-data re-ingestion. The replay gate also needs a configured
billable test actor so the four exact prompts can be exercised end to end.

## Recommended repair order

1. Make product-aware capability interpretation/precedence run before the
   generic semantic company-trend early return, and carry a typed product
   payload through the semantic branch.
2. Stop treating generated `ProductCode` as provider product identity when
   `ProviderProductCode` is absent; use the guarded company-scoped title/unit
   fallback or a stable source-derived identity.
3. Pass `MonthlyProductTrendResult` into the workflow persistence call and add
   a persistence/reload assertion.
4. Re-ingest or safely backfill accepted ProductSales/OutputType-0 product
   identity for the affected companies.
5. Add an end-to-end V2 replay test for all four prompts with billing disabled
   or a test account, asserting both capability and typed result shape.

FEATURE_136_RUNTIME_DIAGNOSIS_COMPLETE
