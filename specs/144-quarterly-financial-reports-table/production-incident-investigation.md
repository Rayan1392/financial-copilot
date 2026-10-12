# Feature 144 — Production Incident Investigation

**Investigation date:** 2026-10-12  
**Scope:** V2 quarterly financial report table, company resolution, billing, and deployment evidence  
**Deployment status:** Deployed to the VPS on 2026-10-12 from source commit `b5a53286dd421ba04829b1e325baf393038cb0c4`

## Findings

### Case 1 — `جدول گزارشات فصلی چخزر`

**Confirmed local root cause:** The V2 quarterly route used `FinancialStatementTableIntentRules.BuildQuery`, whose shared company-hint extraction removed intent aliases with substring replacement. Removing `گزارش` from `گزارشات` left `ات`. The extracted hint and exact argument passed to company resolution were therefore `ات چخزر`, which does not match the canonical ticker `چخزر`; the resolver returned `NotFound/company_not_found` before reading reports. The suspect fragment in the incident is consistent with this code path.

The V2-only `BuildQuarterlyQuery` now removes plural report cues as whole Persian phrases. For this request, extraction is now `چخزر`, and the resolver argument is exactly `چخزر`. The endpoint regression test resolves it to canonical external company ID `FS-CHAKHAZAR` in its isolated test catalog and returns the structured quarterly result. The general `BuildQuery` remains unchanged for V1 callers.

### Case 2 — `جدول گزارشات فصلی کگل`

The response is inconsistent with the current V2 source. In the current workflow, the query satisfies `LooksLikeQuarterlyReportTableQuery`, selects `financial_statement_table`, and returns directly from the quarterly use case. The API mapper preserves `QuarterlyResult` in `financialStatementTableResult`; this branch does not send the query to the model for a generic answer. With the previous extractor, its company candidate would also have been `ات کگل`; with the fix it is `کگل`.

The production decision that led to the generic response is **not established**: production request traces, the deployed image digest, and the corresponding source revision were unavailable. The response indicates that the current deterministic table result was not returned, but it does not identify whether the deployed route was absent, the route predicate differed, or a later response-mapping layer lost the result. A stale or mismatched deployment is a possibility, not a verified root cause. The new V2 endpoint test confirms local routing and structured response for this exact query.

## Parser, routing, and resolution evidence

The new quarterly activity tags record the original query, normalized query, selected capability, extracted candidate, exact resolver argument, company resolution status, canonical ID/symbol, typed outcome, and error reason when the activity is recorded.

| Query | Previous candidate / resolver argument | Fixed candidate / resolver argument | Local V2 route |
| --- | --- | --- | --- |
| `جدول گزارشات فصلی چخزر` | `ات چخزر` | `چخزر` | `financial_statement_table` |
| `جدول گزارشات فصلی کگل` | `ات کگل` | `کگل` | `financial_statement_table` |
| `جدول گزارش‌های فصلی چخزر` | Joined suffix could remain in the hint | `چخزر` | `financial_statement_table` |
| `گزارش فصلی چخزر` | Report cue removed by substring | `چخزر` | `financial_statement_table` |
| `صورت سود و زیان فصلی کگل` | No explicit report table alias | `کگل` | `financial_statement_table` |

The quarterly query builder sets the statement type to `IncomeStatement` and marks the request as a quarterly table. Its tests assert both extraction and route recognition. V1's existing `BuildQuery` behavior was kept unchanged; the new builder is called only by the V2 workflow.

### Resolver and catalog

Local registrations are present: `IQuarterlyFinancialReportsRepository` is backed by the financial statement table repository; `IQuarterlyFinancialReportsQueryUseCase` is registered; and `CompanyResolverService` is exposed through both resolver interfaces. The quarterly use case calls `ICompanyResolutionOutcomeService` before querying persisted reports. The API mapping carries the typed quarterly result through to the HTTP response.

The checked-in Feature 144 read-only data evidence is explicitly from the **Development PostgreSQL database**, not production. It resolves `چخزر` to external company ID `12783` for `NoavaranCurrentApi`; the recorded result is `Available` with five fiscal periods and six persisted report observations. The artifact contains no finding for `کگل`. No production catalog connection or production read-only query was available, so the production presence, ticker spelling, provider mapping, and report coverage for either company remain unverified. No company or financial records were created or changed.

### Typed outcomes

Feature 144 results now expose a `QuarterlyFinancialReportOutcome` in addition to the existing status, reason, and company-resolution status:

- `CompanyNotFound`
- `CompanyAmbiguous`
- `CompanyUnavailableFromProvider`
- `CompanyFoundNoPersistedReports`
- `SourceObservationsUnavailable`
- `SuccessfulTable`
- `PartialTable`
- `InvalidQuery`

The no-report result applies when the resolved company has no persisted reports compatible with the requested table. Persisted but conflicting or unusable source evidence is classified separately. Existing row and cell states remain available for detail.

## Billing investigation

The V2 workflow reserves usage before routing the query. Feature 144 uses operation code `AiQuery.Scanner`, priced at one credit in the local default policy. The table branch finalizes with completion status `Completed` after it receives the typed result, including a `company_not_found` result. `Completed` is not in the configured zero-charge status set, so the charge calculator applies the operation price. This explains the reported one-credit debit in the current code path.

The existing policy documentation requires zero/minimal usage for validation or clarification before billable work and explicit policy for provider/backend failures. The configured zero-charge statuses include `ValidationFailed`, `ClarificationRequired`, and `ProviderFailed`; there is no `CompanyNotFound` status. Whether company lookup failure should be treated as pre-execution validation is not explicit enough to silently change the billing contract.

**Product Owner decision required:** decide whether `CompanyNotFound` should create a zero-credit usage entry and release the held capacity. No billing behavior was changed. The existing successful-query integration test confirms one reservation and one usage ledger entry, and conversation replay adds no second charge.

The actual production reservation ID and ledger row for the reported request were not accessible. The reservation, finalization, status, pricing, and idempotency behavior above is traced from the current code; it is not presented as a production ledger export.

## Deployment verification

Local source evidence shows the production settings file in the working tree specifies `MicrosoftAgentFrameworkV2` and workflow `maf-orchestration-v2.0`. That settings file was already modified before this investigation, so it is not evidence of the deployed configuration. The local source has the V2 quarterly route, resolver registrations, Feature 144 use case registration, and structured API mapping described above.

The VPS was on `develop` at `1a857c4` before deployment. Its existing edits to `.dockerignore`, `docker/api.Dockerfile`, and `docker/appsettings.Production.json` were saved in a Git stash during fast-forward and restored afterward. The `.dockerignore` edit re-includes the tracked Feature 141 `navigation-hierarchy.yaml` required by API publish; the first build attempt without that saved edit failed with MSB3030. The build succeeded after restoring the server's original edits.

Only API and Worker were rebuilt and recreated. PostgreSQL, Redis, RabbitMQ, frontend, and Telegram gateway were left running. Both rebuilt images use the existing tag `3963f94-tzdata`; their image IDs are:

- API: `sha256:10e301150420da77b7306c930b6b6c71a5df16a246d31a5593c1de96818f9c1f`
- Worker: `sha256:db34d1e1449b9bbe566ed48278fd15c0f7ca66b128b283b953f53ee2f68df857`

The server checkout and `origin/develop` were both `b5a53286dd421ba04829b1e325baf393038cb0c4` after deployment. The API container is running and returned `Healthy` from `http://127.0.0.1:5074/health`; `https://api.sapioai.ir/health` returned HTTP 200 and `Healthy`. Root filesystem usage was 71% before build and 81% after it, so no cleanup was performed. The server's three pre-existing edits are restored, with a backup stash named `before-feature144-deploy-20261012` retained.

The production catalog, production company resolution for `چخزر`/`کگل`, and usage ledger for the reported incident were not queried. No production Feature 144 request was sent because its current `AiQuery.Scanner` charge could debit a real account. These remain unverified. The Product Owner decision about zero-charging `CompanyNotFound` also remains open; the deployed version retains the existing one-credit charging behavior. No financial source data or billing policy was changed.

## Files changed

- `src/backend/FinancialCopilot.Application/AI/Orchestration/FinancialStatementTableIntentRules.cs` — V2 quarterly builder and whole-phrase cue extraction.
- `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/QuarterlyFinancialReportsContracts.cs` — typed result outcomes.
- `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/QuarterlyFinancialReportsNormalizer.cs` — classify no reports, unavailable source evidence, and successful/partial tables.
- `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs` — use the V2-only parser and attach incident diagnostic tags.
- `tests/FinancialCopilot.IntegrationTests/AiFacadeV2EndpointTests.cs` — exact requests through `POST /api/ai/v1/query`, real company resolver, structured results, unavailable outcome, persistence/replay, and single-charge coverage.
- `tests/FinancialCopilot.UnitTests/QuarterlyFinancialReports144ResolutionTests.cs` — resolver outcomes and no-report distinction.
- `tests/FinancialCopilot.UnitTests/QuarterlyFinancialReports144Tests.cs` — Persian extraction, route, and source-outcome cases.

Pre-existing edits to `.dockerignore`, `docker/api.Dockerfile`, and `docker/appsettings.Production.json` were left untouched.

## Test results

- Feature 144 focused unit tests: **41 passed**.
- Exact V2 endpoint, resolver, structured-result, unavailable-result, replay, and billing filter: **8 passed**.
- V1/V2 facade, Feature 141 test configuration, and Feature 143 end-to-end filter: **29 passed**.
- Feature 142 backfill provider tests: **41 passed**; backfill endpoint tests: **6 passed**.
- Feature 083, Billing domain, and V1 intent detector unit filter: **56 passed**.
- A broader adjacent unit filter had **73 passed and 5 failed**. The failures are two Feature 141 authorization tests and three Feature 143 handler tests. These same failures and their fixture/configuration causes are already recorded in `implementation-report.md`; they do not touch the modified files. Feature 083, Feature 142, Billing, and V1 intent tests passed in their focused filters.
- No production test or production catalog query was run. The PostgreSQL integration database was not connected for this incident run.

No V1 intent, route, or orchestration code was changed. No financial source data or billing semantics were changed. Source commit `b5a5328` was pushed to `develop` and deployed to the VPS.
