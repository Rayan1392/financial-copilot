# Tasks: Option Chain Data and Call Exercise Ranking

Status: Gate A: Approved by the owner on 2026-11-28 — Gate B: Approved by the owner on 2026-11-28 («گیت بی مورد تایید است») — implemented locally 2026-11-28, uncommitted; open items marked `[ ]` and `[~]` below

## Feature Folder

`167-option-chain-data-and-exercise-ranking`

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner (see `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`, "Recording Approval").
- [x] The features in `Depends On` are done (none listed).
- [x] Read `user-story.md`, the references it lists, and the relevant `docs/` files.
- [~] No open question in `specs/open-questions.md` blocks this spec. Gate B was approved without a separate answer to OQ-168 to OQ-170, so the plan's assumptions were implemented; OQ-171 was answered on 2026-11-28 (no retry on the other address).

## Implementation Tasks

### 1. Provider verification (no production code)

Done during planning on 2026-11-28; results are in `user-story.md`, "Provider Verification".

- [x] 1.1 One response each of `Option`, `Instrument(Flow = 3)`, `BestLimitsAllIns(Flow = 3)` and `TradeLastDay(Flow = 3)` taken in the same session, kept outside the repository.
- [x] 1.2 Copy the verification table into `docs/tsetmc-webservice-datasource.md` (with task 7.1).
- [x] 1.3 Cut small fixtures (a few rows per operation, credentials removed) for the adapter tests (inline in `OptionMarket167Tests`).

### 2. Instrument identity

- [x] 2.1 Daily-trade persistence replaces a numeric stub symbol with the provider symbol (AC-01).
- [x] 2.2 Add flow 3 to the default `InstrumentFlows` (appsettings, `docker-compose.yml`, `.env.example`). For flow 3 the response is parsed as a stream, only valid listed option contracts are kept, and the raw payload is not stored.
- [x] 2.3 Stub backfill use case and admin endpoint, reading stored raw daily payloads in batches (AC-02).
- [x] 2.4 Find every reader of `TradingInstruments` that selects by symbol or active flag and add an explicit exclusion of option contracts where needed (AC-11). Only `MarketPulseService` needed one; the others go through a company link or company symbol.

### 3. Contract terms and open interest

- [x] 3.1 Domain: option contract with side, strike, expiry, contract size as validated values; rejection reasons.
- [x] 3.2 Adapter: `GetOptionContractsAsync` in `ITsetmcWebServiceClient` and its record; date and decimal parsing; HTML or non-SOAP response raises a provider failure.
- [x] 3.3 Persistence: `OptionContracts` table, EF configuration, migration.
- [x] 3.4 Sync use case: upsert, side from ISIN prefix, unresolved instead of stub creation (see deviations), validation, counts, sync state `Tsetmc_OptionContracts` (AC-03, AC-04, AC-06).

### 4. Order book

- [x] 4.1 Adapter: `GetBestLimitsAsync(flow)` with streamed parsing; zero price or volume mapped to absent.
- [x] 4.2 Persistence: `OptionOrderBookLevels` table, EF configuration, same migration as 3.3.
- [x] 4.3 Sync use case: option contracts only, write changed levels only, validation, counts, sync state `Tsetmc_OptionOrderBook`, no raw payload storage (AC-05, AC-06).

### 5. Scheduling and admin

- [x] 5.1 Options for both datasets (enable switch default off, interval) with startup validation.
- [x] 5.2 `TsetmcPollingWorker`: run both syncs in the intraday window, each isolated from trade and index polling and from each other (AC-06). No automated test of the loop.
- [x] 5.3 Admin sync route accepts `optioncontracts` and `optionorderbook`; sync-state query returns both (AC-12).
- [x] 5.4 Provider host answering 403 on one of its two addresses: the owner decided on 2026-11-28 that the client does not retry on the other address (OQ-171). The existing retry behaviour stays; no code change.

### 6. Read side

- [x] 6.1 Option chain query and endpoint (AC-07, AC-10).
- [x] 6.2 Call exercise ranking policy as pure domain or application code (AC-08, AC-09).
- [x] 6.3 Ranking query and endpoint (AC-08 to AC-10).

### 7. Documentation and rollout

- [x] 7.1 `docs/tsetmc-webservice-datasource.md`: the two operations, field mapping, schedule, configuration, admin datasets.
- [x] 7.2 `docs/domain-glossary.md`: option market terms. The two tables are described in `docs/tsetmc-webservice-datasource.md`.
- [x] 7.3 `specs/README.md` index entry; put ranking and user-facing exposure into `specs/backlog.md`.
- [~] 7.4 Rollout note written in `docs/tsetmc-webservice-datasource.md`; the rollout itself and the AC-13 coverage check are not done. Steps: deploy with both switches off; run the stub backfill; enable contract sync, check counts; enable order-book sync, check duration and counts for one session; coverage query for AC-13. Rollback is switching the two flags off; the new tables can stay.

## Tests

- [x] Unit: ranking policy (ordering, ties, the three ineligibility reasons, screenshot figures); contract validation; side from ISIN prefix.
- [x] Unit: adapter parsing of the fixtures from 1.3, including the HTML 403 page. The saved real responses (1,867 contracts, 8,877 order-book rows, the 66 MB instrument response) were also parsed once with a temporary probe that is not in the repository.
- [x] Integration (fake provider): AC-01 to AC-05, AC-07 to AC-10 and AC-12 on the in-memory database in `tests/FinancialCopilot.UnitTests/OptionMarket167Tests.cs` (35 tests); migration, SQL translation, constraints and market pulse on PostgreSQL in `tests/FinancialCopilot.IntegrationTests/OptionMarket167PostgreSqlTests.cs` (2 tests).
- [ ] Architecture: no new architecture test was added. Provider records are used only in `Providers/Tsetmc` and `Ingestion/Tsetmc`; the spec 064 behaviour tests still pass.
- [x] No frontend tests (no UI work).

## Verification Record (2026-11-28, Release)

- Build of `FinancialCopilot.sln`: 0 errors, 0 warnings.
- Architecture tests: 24 of 24 passed.
- Unit tests: 4,335 total, 31 failed in the full run. 28 are in `specs/test-baseline.md` or `specs/bugs.md` (BUG-050). Three are not listed and failed only in the full run; each passes alone on this tree and on clean `HEAD` `25e09869`: `TelegramAssistantResponseRenderer089Tests.Monthly_trend_is_rendered_as_a_deterministic_png_with_a_concise_caption`, `TelegramFinancialStatementRecognizer143Tests.Supplied_examples_parse_header_fields`, `TelegramMonthlyReportRecognizer133Tests.Corrected_channel_caption_extracts_kaveh_and_ignores_fiscal_year_end`. None touches code changed here.
- Integration tests, run one after the other on clean `HEAD` and on this tree: `HEAD` 1,279 total, 251 failed (213 methods); this tree 1,281 total, 250 failed (212 methods). No method fails only on this tree. The integration suite has no baseline; its existing failures are not from this feature.
- `dotnet ef migrations has-pending-model-changes --context FinancialIngestionDbContext`: no pending changes.
- Not verified: AC-13 (live sync on a real environment), the worker loop, the admin and read endpoints over HTTP.

## Definition Of Done

- [ ] `dotnet test src/backend/FinancialCopilot.sln --configuration Release` green (builds the solution and runs unit, integration and architecture tests): no failure outside `specs/test-baseline.md`.
- [ ] `dotnet ef migrations has-pending-model-changes --context FinancialIngestionDbContext` reports no pending model changes.
- [ ] Acceptance criteria in `user-story.md` all checked (AC-13 by the owner or from the production host).
- [ ] Reference docs updated: `specs/README.md` index entry, affected `docs/*.md` and `README.md` sections.
- [ ] No user-visible change, so no `CHANGELOG.md` line unless the owner wants the API noted.
- [ ] New questions → `specs/open-questions.md`; deferred work → `specs/backlog.md`; defects found → `specs/bugs.md`.
