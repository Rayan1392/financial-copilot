# Feature 148 Operational Verification Report

Date: 2026-11-01

## Verdict

**The full import and scheduled worker paths ran against the confirmed isolated local database. The first full sync succeeded; the scheduled repeat completed with one provider error. Live Chat and Telegram acceptance remain unverified.** No Production database, service, or configuration was used.

## Database and Import

The user confirmed that the configured `localhost / financial_copilot` database is isolated and authorized. Before starting the worker, the temporary runner checked the configured host/database and queried `current_database()`; it refused any other target. Migrations were disabled in the temporary host. No repository configuration was changed for the run.

The first worker run used the real CyclicalWaves v2 provider and completed the complete `Full` synchronization through `StartupCatchUp`:

| Run | Trigger | Status | Processed | Updated | Unchanged | Failed |
|---|---|---:|---:|---:|---:|---:|
| `be282b61-012d-4244-922c-4a59886ad92c` | StartupCatchUp | Succeeded | 17,948 | 17,947 | 1 | 0 |

Its phase summary recorded 683 catalog tickers, 82 line definitions, and 17,264 analyses from 173 pages. The database also contained 8,112 quarterly statements and 139,317 statement line values after the import.

A second worker host used a safe temporary UTC schedule occurrence (`08:07 UTC`, 2026-11-01) to exercise the scheduled path and repeat the full scope. The run reached `Scheduled`, processed the same 683 ticker catalog and 82 definitions without additions or definition changes, and used the expected incremental analysis mode. It inserted one newly available analysis and reported 14 unchanged analyses on its page. The run ended `CompletedWithErrors` with 698 processed, 5 updated, 692 unchanged, and 1 failed item; the run summary identifies a provider `HttpRequestException` in the statements phase. This was not a clean idempotent pass, so the overall idempotency check is **partial**.

Post-run database verification:

| Table / check | Observed |
|---|---:|
| `Tickers` | 683 |
| `MetricDefinitions` | 29 |
| `FinancialValues` | 19,807 |
| `ScreenerMetadata` | 683 |
| `IncomeStatementLineDefinitions` | 82 |
| `IncomeStatements` | 8,112 |
| `IncomeStatementValues` | 139,317 |
| `Analyses` | 17,265 |
| `AnalysisTags` | 45,339 |
| Unvalidated foreign-key constraints | 0 |
| Duplicate ticker/metric value pairs | 0 |
| Duplicate ticker/period statement keys | 0 |
| Duplicate statement/line value keys | 0 |

Each of the 683 tickers has all 29 financial values (`683 × 29 = 19,807`); none of those values is null. All 29 metric definitions have a unit: monetary metrics are `Irr`, margins/growth metrics are `Percent`, and P/E/P/S are `Ratio`. Quarterly statement values preserve optional nulls: 834 `YearToDate` and 11,061 `TrailingYear` values are null, with no duplicate statement/line keys.

Analysis text has a data-quality gap: 17,106 of 17,265 v2 analysis rows have both an empty `Summary` and empty `PlainTextSummary`. The existing plain-text backfill service targets the legacy `ComprehensiveAnalyses` set, not the imported `cyclicalwaves.Analyses` rows; it cannot reconstruct summaries the v2 provider did not return. Analysis retrieval may therefore produce entries with no usable summary for most imported records.

## Scheduled Worker

- The real `CyclicalWavesV2SyncWorker` was registered and run in a temporary host that removed unrelated hosted services. Its normal Worker configuration still has `IngestionEnabled=false`, `DailySyncEnabled=false`, and `DailySyncCronUtc=30 23 * * *` (23:30 UTC).
- Startup catch-up produced a persisted successful `Full` run. The safe schedule override produced a persisted `Scheduled` run without changing the system clock or repository settings.
- Run status and counters were read from `cyclicalwaves.SyncRuns`. The scheduled repeat's single failed item remains a provider-level operational error.
- Existing safe-clock unit tests passed: `CyclicalWavesV2SyncTests` plus `CyclicalWavesDataAcquisitionWorkerTests`: 18 passed, 0 failed, 0 skipped. Those tests cover overlap/lease behavior. A live competing-worker overlap was not induced during import.
- The legacy `CyclicalWavesDataAcquisitionWorker` remains retired and is not hosted.

The API was also started locally with migrations disabled. Its V2 status endpoint reported the configured normal cron and disabled ingestion flags; a per-process override confirmed the `CyclicalWaves` Chat mode, and the default configuration reported `NoavaranAmin`. Both API processes were stopped after verification.

## Real LLM Chat

The process had a configured OpenAI environment key, but the live request did **not** reach model invocation. A real API request for the first acceptance question in `CyclicalWaves` mode failed with HTTP 500 at `BillableAccountResolver`: the synthetic actor had no customer billing account. The same question in `NoavaranAmin` mode failed at the same billing gate. No existing customer account was used, and no model call or model charge occurred.

As a result, the ten exact Feature 148 questions were **not** run through the real model in either provider mode. Live semantic routing, structured responses, provider isolation/no-fallback, result persistence, and replay remain unverified. The scripted Feature 148 integration suite remains 28 passed, 0 failed, 0 skipped, but is not real-model evidence.

## Telegram

No Telegram bot token or authorized test destination is configured in the environment. No live Telegram message was sent. Live lookup, quarterly-statement, scanner, and unsupported-capability smoke tests remain unavailable; the local gateway integration tests ran only in the scripted suite.

## Commands and Evidence

- `dotnet run` of a temporary worker host referencing `FinancialCopilot.Worker`, with a preflight check that accepted only `localhost / financial_copilot`, migrations disabled, and only `CyclicalWavesV2SyncWorker` registered.
- Read-only PostgreSQL verification of table counts, financial units, null counts, key duplicates, and foreign-key validation after each run.
- Temporary local API host on the development launch profile with `Database:ApplyMigrationsOnStartup=false`; V2 status endpoint verified both provider-mode configurations.
- `dotnet test tests/FinancialCopilot.IntegrationTests/FinancialCopilot.IntegrationTests.csproj --no-restore --filter 'FullyQualifiedName~Feature148' --verbosity minimal` — 28 passed, 0 failed, 0 skipped (scripted model).
- `dotnet test tests/FinancialCopilot.UnitTests/FinancialCopilot.UnitTests.csproj --no-restore --no-build --filter 'FullyQualifiedName~CyclicalWavesV2SyncTests|FullyQualifiedName~CyclicalWavesDataAcquisitionWorkerTests' --verbosity minimal` — 18 passed, 0 failed, 0 skipped.

## Remaining Blockers

1. The scheduled repeat had one provider `HttpRequestException`; rerun after the provider issue is resolved to complete idempotency verification.
2. Most v2 analysis rows lack summary text, and the current backfill endpoint does not target this table.
3. Live Chat acceptance requires an authorized isolated test billing account for the synthetic actor; do not use an existing customer account.
4. Live Telegram verification requires an authorized test bot and destination.

No Production data was accessed or changed. No push or deployment occurred.
