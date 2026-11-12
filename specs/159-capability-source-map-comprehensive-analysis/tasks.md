# Tasks: Comprehensive Analysis from TahlilApp in Chat (Capability Source Map, Step 1)

Status: Gate B: Approved by the owner on 2026-11-10

Approval record: Gate A on 2026-11-10 (see `design-proposal.md`). Gate B on 2026-11-10: the owner answered
OQ-063..065 and wrote «Gate B مورد تایید است». The answers removed the slug, the migration, the API field, the teaser
cut and the disclaimer, and set symbol-in-title matching (`design-proposal.md` §6). This plan already reflects them.

## Feature Folder

`159-capability-source-map-comprehensive-analysis` (branch `feature/162-capability-source-map`, worktree `D:\wt\f`)

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner.
- [x] Depends On: Feature 148 v2 analysis ingestion exists in code.
- [x] Read `user-story.md`, the ADR, and the references.
- [x] No open question blocks implementation (OQ-063/064/065 answered 2026-11-10).

## Implementation Tasks

### 1. Source map (Application, single owner)

- [x] 1.1 `FinancialDataOptions.CapabilitySources` (route code → provider name, case-insensitive keys) and `ChatCapabilitySourceMap` with `OverridableRoutes = { comprehensive_analysis }`.
- [x] 1.2 Validation (fail at composition with a clear message): key in allowlist, defined provider name, no entries when `ChatProvider=CyclicalWaves`; an empty value counts as absent.
- [x] 1.3 `IChatCapabilityAvailability.SourceFor(route)`; `Evaluate` applies C2 only when the route source is NoavaranAmin. `Provider` keeps meaning the base mode.
- [x] 1.4 Unit tests: default map = current matrix (both modes); mapped route available; unmapped routes unchanged; validation failures.

### 2. Composition and workflow

- [x] 2.1 DI: in NoavaranAmin mode register `CyclicalWavesV2ComprehensiveAnalysisQuery` for `IComprehensiveAnalysisQueryUseCase` when mapped; otherwise keep `UnavailableComprehensiveAnalysisQueryUseCase`. Nothing else is replaced.
- [x] 2.2 `FinancialCopilotWorkflowDefinition`: pick the analysis agent tool by `SourceFor(ComprehensiveAnalysis)`; no Feature 141 fallback for a route whose source differs from the base provider. No other `IsCyclicalWavesMode` check changes.
- [x] 2.3 Telemetry tag `chat.capability_source` on the semantic step activity.
- [x] 2.4 Composition tests (`CyclicalWavesV2CompositionTests`): mapped/unmapped registrations; resolver, lookup, scanner, statements unchanged in mapped NoavaranAmin mode; invalid map fails.

### 3. Query and matching

- [x] 3.1 `CyclicalWavesV2ComprehensiveAnalysisQuery`: symbol matches when it is a whole word of the normalized title (any position); tags no longer select by symbol; candidate pre-filter `Title` contains the symbol (bounded window).
- [x] 3.2 Summary safety bound 6,000 characters with «…» on a cut.
- [x] 3.3 Tests: «شگل، بخش سوم - نسبت P/s», «حق تقدم شگل», «اخابر؛», «فرابورس- …» vs «بورس», Arabic ي/ك, tag-only article not matched.
- [x] 3.4 «جامع» in `DeterministicCapabilityInterpreter.NonEntityWords`; routing tests for the FR9 corpus.

### 4. Answer card

- [x] 4.1 Application `ComprehensiveAnalysisPresentationOptions` (`ComprehensiveAnalysis:TahlilAppAnalysesUrl`, default `https://tahlilapp.com/app/analysis`, absolute https validated at start-up).
- [x] 4.2 Application `ComprehensiveAnalysisCardComposer` (pure): markdown card and plain-text card; no-result sentence with the symbol.
- [x] 4.3 `ComprehensiveAnalysisToolResult.AgentSummary` uses the composer with the configured link at every call site (workflow definition, agent runner, tool adapter).
- [x] 4.4 Composer unit tests (Persian text, empty summary, several items, no items with and without symbols).

### 5. Telegram

- [x] 5.1 Tests reproducing the drop (one lookup row) and duplicate (`TextAnswer` + `AppendAnalysis`) cases for `SemanticCapabilityCode = comprehensive_analysis`.
- [x] 5.2 Fix: for that route render the plain-text card once (after the stock card or lookup rows); other routes unchanged.

### 6. Ops and configuration

- [x] 6.1 `appsettings.json`: `FinancialData:CapabilitySources` empty; `ComprehensiveAnalysis:TahlilAppAnalysesUrl`.
- [x] 6.2 `docker-compose.yml`: `FinancialData__CapabilitySources__comprehensive_analysis: ${FINANCIAL_DATA_SOURCE_COMPREHENSIVE_ANALYSIS:-}`; `.env.example` line.
- [x] 6.3 Read-only production check SQL (`Analyses` count, newest `PublishedAt`, last `Analysis` sync run) in the provider README. Written, not run (needs owner access).

### 7. Docs

- [x] 7.1 ADR 0001 (drafted with this plan).
- [x] 7.2 Amendment note in 148 `design.md` (C2 now per capability) and `docs/providers/cyclicalwaves-v2/README.md`.
- [x] 7.3 `AGENTS.md` comprehensive-analysis note: v2 source via the map, card format, title matching.
- [x] 7.4 `README.md` configuration section; `specs/README.md` index row; `CHANGELOG.md` `[Unreleased]` Persian line.
- [x] 7.5 `specs/backlog.md`: `is_published`, `required_plans`, thumbnails, stale-analysis note, Steps 2 and 3.

## Tests

- [x] Unit tests listed above (`tests/FinancialCopilot.UnitTests`).
- [x] Integration: `Feature159ComprehensiveAnalysisSourceMapTests` with the `Feature148TableReadRecorder`: mapped NoavaranAmin reads only the three analysis tables from the v2 schema; unmapped reads none; 0 credits on no result.
- [x] Integration: existing `Feature148ChatProviderE2ETests` and NoavaranAmin regression suites unchanged.
- [x] Architecture: Application does not reference Infrastructure; no other route may be overridden (allowlist test).
- [x] Live Development check (AC9) on disposable PostgreSQL.

## Definition Of Done

- [x] `dotnet test src/backend/FinancialCopilot.sln --configuration Release` green: no failure outside `specs/test-baseline.md`.
- [x] Acceptance criteria in `user-story.md` all checked.
- [x] Reference docs updated (task group 7).
- [x] `CHANGELOG.md` `[Unreleased]` Persian line.
- [x] New questions → `specs/open-questions.md`; deferred work → `specs/backlog.md`; defects → `specs/bugs.md`.

## Release gates (not part of Gate B)

- Read-only production check of v2 analysis ingestion (owner access).
- If ingestion is off: separate approval to enable it.
- Setting the map on the server is the owner's go-ahead per `docs/release/release-process.md`.

## Implementation notes (2026-11-10)

- Done on branch `feature/162-capability-source-map` (worktree `D:\wt`), uncommitted. The map is empty by default, so
  production behaviour is unchanged until the owner sets `FINANCIAL_DATA_SOURCE_COMPREHENSIVE_ANALYSIS=CyclicalWaves`.
- FR7 needed one executor change: `ComprehensiveAnalysisCapabilityExecutor` now returns NoData (free) when the analysis
  query succeeds with no item, even if context metrics exist (before: Partial, 1 credit). A failed analysis query is
  still Partial or TemporarilyUnavailable. This also applies in CyclicalWaves mode (same capability).
- Matching by title word replaces the leading-segment and tag rule in both modes (owner rule). Two existing tests
  changed on purpose: `Analysis_TagMatchesTicker` became `Analysis_TagAloneDoesNotSelectAnArticleForATicker`, and the
  Feature 148 guard on `FinancialDataOptions` properties now lists `CapabilitySources`.
- Telegram: the drop (one lookup row → analysis missing) and duplicate (text + appendix) were reproduced by tests on
  the old renderer (actual 0 and 2 occurrences) before the fix. Other turns keep the old analysis appendix.
- Tests: unit 3,396 run, 32 failed = exactly the `specs/test-baseline.md` set (29 methods). Architecture: 1 failure,
  the known BUG-003. Integration (Feature 148/162/analysis filter): Feature 159 green; 9 failures that fail identically
  on clean `develop` e32fd5c (recorded in `specs/backlog.md`). Build: 0 warnings.
- Owner request (2026-11-10): local runs map `comprehensive_analysis` to `CyclicalWaves` in `appsettings.Development.json`
  (not `appsettings.json`: every integration test host reads that file, and the trial broke 34 tests; production uses
  the `Production` environment). `AuthenticationApiFactory` resets the map so test hosts stay independent of local
  settings; the Feature 159 host sets it after the base. Integration result unchanged: only the 9 pre-existing failures.
- Local run (2026-11-10): `appsettings.Development.json` also sets `ChatProvider=NoavaranAmin`,
  `ComprehensiveAnalysis:TahlilAppAnalysesUrl` and `SemanticRouting:Capabilities:comprehensive_analysis=SemanticPrimary`
  (local user-secrets set `DefaultMode=Shadow`, where the LLM writes the answer from the card instead of showing it).
  docker-compose defaults `SemanticRouting__DefaultMode` to `Shadow` too: production shows the card verbatim only with
  that capability on `SemanticPrimary` (owner decision at release).
- AC9 live check (2026-11-10): disposable PostgreSQL 18 (port 55441) with 200 real analyses captured from the public
  TahlilApp API; `Feature159LiveAnalysisPostgreSqlTests` (skipped unless `FEATURE159_LIVE_ANALYSES_JSON` is set) passed
  for شگل (three «شگل، بخش …» titles), وتجارت and اخابر, together with the existing v2 PostgreSQL tests (9/9).
