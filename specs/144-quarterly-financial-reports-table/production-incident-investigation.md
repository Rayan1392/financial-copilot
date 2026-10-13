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

---

## Follow-up: deployed V2 generic error investigation

**Investigation date:** 2026-10-12
**Production access:** Read-only container inspection and API log review
**Code changes:** Local only; no commit, push, or deployment performed for this follow-up.

### 1. Deployment and parser verification

The API container currently running in Production was built from Feature 144 source commit `b5a53286dd421ba04829b1e325baf393038cb0c4`, which contains the V2-only `BuildQuarterlyQuery` correction. The source checkout was subsequently advanced to `d58dd3b34d977bf87a3c9f1b32cc0266778d8c21`; that commit only updates this investigation document and does not change API code. The running API image digest recorded during deployment is `sha256:10e301150420da77b7306c930b6b6c71a5df16a246d31a5593c1de96818f9c1f` (tag `3963f94-tzdata`). Therefore the corrected parser is deployed.

Production container inspection also showed `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true` and an Alpine runtime without `/usr/lib/libicuuc.so`. Thus deployment of the parser fix did not eliminate the runtime failure found below.

### 2. Request matching and available production evidence

The exact reported request is `گزارشات فصلی چخزر؟`. The API logs do not record request bodies, and the supplied report did not include a timestamp, conversation ID, or correlation ID. Searching the available API logs found two V2 workflow exceptions at `2026-10-12T19:36:37.500444915Z` and `2026-10-12T19:36:45.947815933Z`; the latter is followed by `POST /api/ai/v1/query` returning HTTP 500 after 6775.2973 ms. No request or conversation correlation ID was present in these exception/log records. The exact user query cannot be uniquely matched to either 500 from the available evidence.

The captured exception stack is nevertheless specific to Feature 144 quarterly rendering. The call path below is only entered by the quarterly financial table use case. The local exact-query endpoint reproduction confirms the expected route and hint, but that does not establish which request body produced either Production 500.

### 3. Production exception and failing stage

Captured exception chain:

```text
System.InvalidOperationException: Workflow execution failed: Error invoking handler for FinancialCopilot.Infrastructure.AI.OrchestrationV2.Workflow.BillingReservedMessage
  System.Reflection.TargetInvocationException
    System.Globalization.CultureNotFoundException: Only the invariant culture is supported in globalization-invariant mode. See https://aka.ms/GlobalizationInvariantMode for more information. (Parameter 'name')
    fa-ir is an invalid culture identifier.
```

Failing method and stack location:

```text
System.Globalization.CultureInfo.GetCultureInfo(String name)
FinancialCopilot.Infrastructure.Financial.Ingestion.NadpcoApi.FinancialStatementTableQueryUseCase.RenderQuarterly(QuarterlyFinancialReportTableResult result)
  FinancialStatementTableServices.cs: line 512
FinancialStatementTableQueryUseCase.ExecuteAsync(...)
  FinancialStatementTableServices.cs: lines 427-433
FinancialCopilotWorkflowDefinition.ExecuteAgentStepAsync(...)
  FinancialCopilotWorkflowDefinition.cs: line 794
FinancialCopilotWorkflowDefinition.RunAsync(...)
AiFacadeController.Query(...)
GlobalExceptionMiddleware.Invoke(...)
```

At line 427, `quarterlyReportsUseCase.ExecuteAsync` returns before `RenderQuarterly` runs. `RenderQuarterly` returns early for `Status == "Unavailable"`; reaching line 512 means it received a non-`Unavailable` result, then failed while constructing Persian-formatted answer text. No SQL exception was logged, and the database/use-case call returned. The confirmed failure is presentation rendering, after quarterly query and normalization, before result mapping, workflow completion, and conversation persistence.

The workflow's billing `FinalizeAsync(..., "Completed", ...)` call is after the use-case/render call at lines 804-808. It is not reached when rendering throws. Production reservation/ledger state was not queried, so whether the reservation later expired or was otherwise reconciled is unresolved. No change was made to exactly-once billing or exception handling.

The API exception middleware returned the sanitized generic user-facing error. The exception and stack trace remained visible in server logs. No original exception was replaced with a successful financial response.

### 4. V2 routing and exact company hint reproduction

The deployed V2 workflow selects capability `financial_statement_table` for quarterly table intent at `FinancialCopilotWorkflowDefinition.cs:778-794`. It builds the query using `FinancialStatementTableIntentRules.BuildQuarterlyQuery`, then passes `CompanyQuery` to `FinancialStatementTableQueryUseCase.ExecuteAsync`; the quarterly use case forwards that value to the company-resolution query. Existing production activity tags for the original text, candidate, resolver argument, and canonical resolution were not available in the captured log output, so the Production hint and canonical company ID for the uncorrelated incident cannot be recovered.

The exact inputs were posted to the real V2 `/api/ai/v1/query` pipeline through the closest safe environment available: the integration-test API host with isolated test storage and a deterministic catalog/repository. The endpoint returned `FinancialStatementTableLookup`, made no generic model-tool selection, and resolved the fixture's expected canonical company. Its query builder supplies the same company hint to resolution.

| Exact query | V2 capability | Company hint / expected resolver argument | Endpoint result |
| --- | --- | --- | --- |
| `گزارشات فصلی چخزر؟` | `financial_statement_table` | `چخزر` | Resolved to fixture company `FS-CHAKHAZAR` |
| `گزارشات فصلی چخزر` | `financial_statement_table` | `چخزر` | Resolved to fixture company `FS-CHAKHAZAR` |
| `گزارش فصلی چخزر` | `financial_statement_table` | `چخزر` | Resolved to fixture company `FS-CHAKHAZAR` |
| `جدول گزارشات فصلی چخزر` | `financial_statement_table` | `چخزر` | Resolved to fixture company `FS-CHAKHAZAR` |
| `جدول گزارشات فصلی کگل` | `financial_statement_table` | `کگل` | Resolved to fixture company `FS-KGL` |

These are isolated fixture mappings, not evidence that the Production company catalog currently contains these mappings or quarterly reports. No billable request was sent to Production.

### 5. Verified root cause and code changes

**Verified root cause:** the API runs on Alpine without ICU while `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true`. The quarterly renderer calls `CultureInfo.GetCultureInfo("fa-IR")`, which throws `CultureNotFoundException` in this configuration. The parser correction is deployed; the generic error is caused by the API runtime globalization configuration, not the old plural-suffix extraction defect.

The minimal local fix enables ICU for the API runtime and explicitly disables invariant mode for the API service only:

- `docker/api.Dockerfile`: install `icu-libs`; set `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false`.
- `docker-compose.yml`: set the same variable to `false` for the API service after its shared environment mapping, overriding any value in `.env`. The Worker environment is not changed.
- Add exact and alias inputs to the Feature 144 parser unit theory and V2 endpoint integration theory.

No calculation rules, SourceItem mappings, report priority, zero/missing behavior, V1 route, Features 141-143, structured persistence, generic exception handling, or billing semantics were changed.

### 6. Test results and verification limits

- `dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj --no-restore --filter FullyQualifiedName~QuarterlyFinancialReports144Tests` — **34 passed, 0 failed**.
- `dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~V2AiQuery_QuarterlyReportAliasesResolveCanonicalCompanyAndReturnStructuredTable` — **7 passed, 0 failed**. This includes the exact query with Persian question mark and the plural form without `جدول`, plus existing singular and table forms.
- The first concurrent test-build attempt encountered a shared `obj` file lock. The endpoint test then exposed duplicate theory data, which was removed; the sequential reruns above passed.
- Local Docker daemon is unavailable, so the corrected Alpine image was not built or smoke-tested here. Production remains on the existing image and still has the confirmed failure until redeployed with the ICU/runtime change.
- No Production SQL error was found in the captured exceptions. The logs do not expose the exact company hint, request correlation ID, request body, persisted response, or billing ledger result for the reported request. Those request-level details remain unresolved.

### 7. Required deployment steps

1. Build a new API image from the reviewed worktree with `icu-libs` installed; retain the API-only `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false` setting in the deployed compose configuration.
2. Deploy/recreate the API container using that image and effective compose configuration. Do not deploy the current Production image unchanged.
3. Verify inside the new container that invariant mode is false and ICU is installed, then check API health.
4. Use an approved, non-billable/read-only verification path to confirm request activity/correlation IDs, `financial_statement_table`, resolver hint `چخزر`, canonical company mapping, structured response persistence, and billing finalization. Do not use a normal billable account to probe Production.

This follow-up changed local Docker configuration, tests, and this report only. It was not committed, pushed, or deployed.

---

## Follow-up: ICU image readiness check

**Date:** 2026-10-13
**Status:** `BUILD_BLOCKED`
**Production change:** None. No production database, billing record, container, or image was changed.

### Dockerfile and Compose inspection

The API Dockerfile now installs `icu-libs` in the Alpine runtime stage and sets `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false`. The existing local `COPY` entries for the Feature 141 semantic catalog and navigation hierarchy were preserved because the API publish requires those inputs. No Worker Dockerfile, financial code, calculation, mapping, V1 route, or Feature 141-143 logic was changed.

The local Compose configuration sets the invariant variable to `false` at the API service level, outside the shared API/Worker environment anchor. `docker compose config --format json` succeeded and confirmed:

```text
API_INVARIANT=false
WORKER_INVARIANT_OVERRIDE=
```

The existing Production API image remains `financial-copilot-api:3963f94-tzdata`, digest `sha256:10e301150420da77b7306c930b6b6c71a5df16a246d31a5593c1de96818f9c1f`. No new image tag or digest exists yet.

### Build and container verification

The Docker CLI and Compose client are installed, but neither configured Docker Engine pipe is available:

```text
desktop-linux: //./pipe/dockerDesktopLinuxEngine — not found
default:      //./pipe/docker_engine — unavailable
```

The prescribed build was attempted with tag `financial-copilot-api:feature144-icu-20261013` and failed before any build step:

```text
ERROR: error during connect: Head "http://%2F%2F.%2Fpipe%2FdockerDesktopLinuxEngine/_ping":
open //./pipe/dockerDesktopLinuxEngine: The system cannot find the file specified.
```

Therefore the API image build did not run and the proposed tag was not created. No isolated container was started. The following runtime checks are **not verified**: effective container environment, ICU package/library presence, `CultureInfo.GetCultureInfo("fa-IR")`, container API startup, quarterly renderer output inside the image, and exact-query response from that image. The existing API integration test is not a substitute for running the built Alpine image.

The exact query `گزارشات فصلی چخزر؟` was **not** sent to Production. There is still no billable Production smoke request, no Production database access, and no billing-record change. Do not mark the incident resolved until a corrected image is deployed and an approved Production request succeeds.

### Tests rerun for this readiness check

- Feature 144 focused unit tests: **34 passed, 0 failed**.
- V2 quarterly API endpoint integration tests: **7 passed, 0 failed**. These exercise the exact query and the required aliases using the isolated test API host, fake catalog, and test repository; they do not execute inside the Alpine container.
- Compose configuration parse/effective API environment check: **passed**. No Docker engine was needed for this check.
- API Docker image build and image-level smoke checks: **blocked** by the unavailable Docker daemon.

### Proposed immutable image identity and build procedure

When the documented local Docker Engine is available, use a new release tag rather than reusing `3963f94-tzdata`. The following is a **proposed tag**, not a built or validated artifact:

```text
financial-copilot-api:feature144-icu-20261013
```

The repository's release procedure builds Linux/AMD64 images on the local build machine, saves an archive, then transfers and loads that archive on the VPS. API-only commands from PowerShell, run at the repository root:

```powershell
$ImageTag = "feature144-icu-20261013"
$ImageArchive = Join-Path $env:TEMP "financial-copilot-api-$ImageTag.tar"

docker buildx build --platform linux/amd64 --load `
  -f docker/api.Dockerfile `
  -t "financial-copilot-api:$ImageTag" .

docker image inspect "financial-copilot-api:$ImageTag" --format '{{.Id}}'
docker save --output $ImageArchive "financial-copilot-api:$ImageTag"
Get-Item $ImageArchive | Select-Object FullName,Length

scp -P 22033 $ImageArchive root@185.126.203.173:/opt/sapio/financial-copilot/
scp -P 22033 docker-compose.yml root@185.126.203.173:/opt/sapio/financial-copilot/docker-compose.yml.feature144-icu
```

Do not proceed unless the build succeeds, the image inspection returns its immutable `sha256:` image ID, and the archive is non-empty. The Compose API override must remain `false`; no Worker image or service is part of this release.

Before using the API image for any isolated container smoke, verify the image-level checks first. Start the API only in a disposable environment with a throwaway database, Redis, RabbitMQ, and non-production credentials. Confirm the container's `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false`, installed `icu-libs` and ICU shared library, successful `CultureInfo.GetCultureInfo("fa-IR")`, and API health. Then post the exact query to that isolated V2 API and confirm a structured Feature 144 result without `CultureNotFoundException`. Never point this smoke environment at Production storage or a Production billing account.

### Production deploy and rollback instructions (not executed)

The actual VPS Compose project is `/opt/sapio/financial-copilot`; its runtime Compose file uses `IMAGE_TAG` and the deployed API service is `api`. When separately authorized, first confirm there are no pending EF migrations: the production Compose configuration enables `Database__ApplyMigrationsOnStartup=true`, so starting the API may apply migrations. Stop for separate database-change approval if any migration is pending. Then, on the VPS:

```bash
cd /opt/sapio/financial-copilot
cp -a docker-compose.yml docker-compose.yml.before-feature144-icu
cp docker-compose.yml.feature144-icu docker-compose.yml
docker load --input financial-copilot-api-feature144-icu-20261013.tar
IMAGE_TAG=feature144-icu-20261013 docker compose up -d --no-deps api
docker compose ps api
docker logs --tail 200 financial-copilot-api-1
docker exec financial-copilot-api-1 sh -lc 'printf "invariant=%s\\n" "$DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"; apk info -e icu-libs; ls /usr/lib/libicuuc.so*'
curl -fsS --max-time 10 http://127.0.0.1:5074/health
```

For a rollback, restore the saved Compose file (including its previous invariant environment), then recreate **only** the API from the recorded previous image tag:

```bash
cd /opt/sapio/financial-copilot
cp -a docker-compose.yml.before-feature144-icu docker-compose.yml
IMAGE_TAG=3963f94-tzdata docker compose up -d --no-deps api
docker compose ps api
curl -fsS --max-time 10 http://127.0.0.1:5074/health
```

The rollback target is the previously deployed API digest `sha256:10e301150420da77b7306c930b6b6c71a5df16a246d31a5593c1de96818f9c1f`. Confirm that tag/digest is still present on the VPS before rollback. This rollback restores the known `fa-IR` failure for quarterly rendering; it does not reverse database migrations. No command above was executed.

### Readiness decision

The existing Feature 144 and endpoint tests pass, and the local Compose configuration is correct. Image build, ICU runtime validation, isolated container startup, and containerized exact-query smoke remain unperformed because Docker Engine is unavailable. No new image identity is available. This work is **`BUILD_BLOCKED`**, not deployment-ready; the Production incident remains unresolved until the build, isolated image smoke, authorized deployment, and successful Production verification are completed.
