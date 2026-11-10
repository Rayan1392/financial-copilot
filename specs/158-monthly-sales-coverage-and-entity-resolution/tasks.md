# Tasks: Monthly Sales Coverage and Canonical Company Resolution

Status: Gate B: Approved by the owner on 2026-11-10

Gate A covers D3 (canonical priority and, by the owner's decision OQ-061 option (b), the two-company clarification), D4 and D5. OQ-062 option (b) gives `OutsideScope` its own sentence. Line references are at `5dfc496`, unless stated.

## Feature Folder

`158-monthly-sales-coverage-and-entity-resolution`

## Before Starting

- [ ] This file's `Status:` line records Gate B approval by the owner (see `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`, "Recording Approval").
- [ ] The features in `Depends On` are done: none.
- [ ] Read `user-story.md`, the references it lists, and `.claude/rules/*`.
- [ ] No open question in `specs/open-questions.md` blocks this spec. OQ-030 does not block; no acceptance claim depends on it.

## Planning clarifications (presented for Gate B)

Code reading refined the approved design. None changes the approved goals. Each needs the owner's explicit agreement at Gate B.

| Topic | Design said | Plan does, and why |
|---|---|---|
| D5 resolution | Use the existing `ResolveAsync(symbol, NoavaranCurrentApi)` | Adds an eligible-first exact step before it. `ResolveAsync` alone (`CompanyResolverService.cs:62-85`) returns `Ambiguous` when two Noavaran rows share a symbol, even if one is non-eligible, which defeats goal 2 for those symbols. The approved term "canonical company row" already prefers the eligible row. |
| D5 `Ambiguous` | "Maps to the existing ambiguous outcome" | The use case still returns null (the design rejected a return-type change). The D4 port reports `CompanyAmbiguous` on the same resolution, and the executor returns `DisambiguationRequired`. |
| D4 text placement | Track A | Per the lead, placed here. One new static method in `AiDialogueOutcomePolicy`. The text travels as the executor's `Payload`, handled by the existing `string explanation => explanation` arm (`FinancialCopilotWorkflowDefinition.cs:541`), and reaches the reply through `ComposeSystemMessage` (`:1385-1387`). |
| D4 English | Not stated | The English sentence omits the symbol, because the language guard rejects Persian script in an English detail. |
| Budget (load once per turn) | Listed in D3 | **Not needed.** Tiers partition the rows already loaded at `CanonicalQueryEntityResolver.cs:60`. Out of scope. |
| Two-company clarification (OQ-061) | §5 D3 | Added after Gate B by the owner's decision. Reads only model-proposed company mentions, requires exact-kind resolution, and ignores month-name mentions, so an ordinary question such as `فروش دی ماه فولاد` does not trigger it. The separator fallback path is not needed for this and stays out of scope. |
| V1 | Behind the V2 scope | Resolver, use case and executor consult `Feature154RoutingScope.IsActive`. V1 sets it false for its whole flow (`AiQueryOrchestrationService.cs:57`). No new configuration key. |

## Who changes behaviour

### Canonical priority: callers of the resolver

Registered at `ServiceCollectionExtensions.cs:799`. In `CyclicalWaves` chat mode it is replaced (`CyclicalWavesV2ServiceCollectionExtensions.cs:56`), so that mode is unchanged. Production runs `NoavaranAmin` (OQ-008).

| Caller | Call | Effect on V2 in NoavaranAmin mode |
|---|---|---|
| `ConversationDialogueGate.PrepareAsync` (`ConversationTaskStateContracts.cs:179`) | `ResolveFromInterpretationAsync` | Changes the company for every capability that takes a company. See below. |
| Same (`:191`) | `ResolveMentionAsync`, quarterly company hint | Same rule |
| Same (`:307`) | `ResolveAllFromInterpretationAsync`, `symbol_metric_lookup` | A symbol that was skipped as ambiguous is now included |
| `StructuredQueryCompanyBinder.cs:15` | `ResolveMentionAsync` | Same rule. Feature 141 is off by default (`appsettings.json`); no customer effect while off |
| `IndustryRelativeValuationSemanticResolver.cs:216` | `ResolveMentionAsync` | Same rule. Route is retired (`ChatCapabilityAvailability.RetiredRoutes`); no customer answer changes |
| `IndustryRelativeValuationSemanticResolver.cs:33` | `ResolveExactTickerFromTextAsync` | Unchanged method |
| `CanonicalCompanyRouteAdapter` (`CanonicalQueryEntityContracts.cs:277`) | `ResolveFromInterpretationAsync` | Registered; no production caller found |

Capabilities with a required company: `symbol_metric_lookup`, `comprehensive_analysis`, `monthly_activity_trend`, `monthly_product_comparison`, `product_revenue_mix`, `product_sales_value`, `product_sales_trend`, `financial_statement_table`, `financial_statement_period_analysis`, `symbol_snapshot`. `disclosure_listing` uses the same resolution. Also listed in the catalog: `registered_financial_source` (Feature 141, off by default), `ps_gauge_visualization` and `symbol_vs_industry_relative_valuation` (retired routes).

Questions that change, for any of those capabilities. Examples are invented.

| Catalog shape for the mentioned symbol | Example | Today | After |
|---|---|---|---|
| Eligible row plus 2 or more other-provider rows with the symbol in `Ticker` | `روند فروش ماهانه کگل` | `entity_ambiguous` | Answer for the eligible row |
| Eligible row plus a non-eligible Noavaran row with the same symbol | `ترازنامه کگل` | `entity_ambiguous` | Answer for the eligible row |
| Eligible row plus exactly 1 other-provider row with the symbol in `Ticker` | `P/E کگل` | Resolved to the other row; `CanonicalId` differs; `DisplaySymbol` is usually the same text | Resolved to the eligible row |
| A symbol equal to an eligible company's exact name while another provider holds it as `Ticker` | rare | Other-provider row wins | Eligible row wins |
| Two eligible rows with one symbol (T1.1 found none) | | `entity_ambiguous` | Unchanged |
| No Noavaran row | | Today's result | Unchanged |
| No exact hit | | Substring or fuzzy | Unchanged |
| Two exact tickers in one request, on a capability with no `symbols` slot, proposed by the model | `روند فروش ماهانه کگل/شپنا` | One ticker answered, the other dropped; or `entity_ambiguous` with duplicates | `ClarificationNeeded`, reason `multiple_companies_not_supported`, pending disambiguation with both companies |

V1: under `EnterFrozenV1`, `IsActive` is false and the existing algorithm runs.

### Existing tests that pin this behaviour

Baseline run on this branch: 267 passed, 0 failed for the classes below. Each test is reviewed by reading it against the tier rule; a test is changed only if the review shows a reason, and that reason is reported. No bulk update.

| Test | Rows | Expected after, and review point |
|---|---|---|
| `CanonicalQueryEntityResolverTests.CanonicalProviderTickerWinsOverDuplicateAuxiliaryProviderTicker` | Noavaran row (no market, so T2) and auxiliary row | Passes. Confirms T2 outranks T3 |
| `CanonicalQueryEntityResolverTests.AmbiguousAlias_ReturnsOrderedCandidatesWithoutResolution` | Two T3 rows | Passes. Pins AC-4 |
| `CanonicalQueryEntityResolverTests.ExactTickerWinsOverAnEarlierAmbiguousGenericMention` | T3 rows | Passes |
| `CanonicalQueryEntityResolverTests.TickerCharacterVariants_ZwnjAndPunctuationResolveExactly`, `ArabicCharacterVariant_ResolvesToCanonicalTicker` | T3 rows | Passes; normalization unchanged |
| `CanonicalQueryEntityResolverTests.Typo_IsAnAmbiguousFuzzyCandidateAndNeverAutoResolved`, `UnknownEntity_IsNotFoundRatherThanMissingOrNoData` | T3 rows | Passes. Pins AC-5 |
| `CanonicalQueryEntityResolverTests.MultipleCanonicalEntities_AreResolvedWithoutTreatingConnectorsAsSymbols`, `V1AndV2RouteAdapters_ResolveTheSameCanonicalEntity` | T3 rows | Passes |
| Remaining `CanonicalQueryEntityResolverTests` | T3 rows, or no database | Passes; no tier difference |
| `ComprehensiveAnalysisSemanticRoutingRegressionTests.CanonicalAliasMatch_WinsOverNonCanonicalProviderAliasRows`, `HybridInterpreter_DuplicatedAndOverlappingModelEntities_DoNotCreateAmbiguity` and the rest of the class | Noavaran rows (T2) with `Tsetmc`, `Codal` or `CyclicalWaves` rows (T3) | Passes. Confirms the priority |
| `Feature125SemanticRoutingRegressionTests` | Noavaran rows | Passes |
| `MonthlyStructuredQueryPostgreSqlTests`, `Feature141*PostgreSqlTests`, `Feature141EngineVerticalSliceTests` (integration) | Single-provider company rows; their "Ambiguous" cases concern products, measures or statements | Read now: no company-tier difference found. They need a PostgreSQL instance; run only on a disposable one, never port 5432. If none is available, report as a limitation |
| `IndustryRelativeValuationSemanticAdapterTests`, `StructuredQueryCompanyBinderTests`, `AssistantHelpReply121Tests`, `SymbolSnapshot156Tests` | Fake resolvers | Not affected |

### Callers of `MonthlyActivityTrendQueryUseCase`

Registered at `ServiceCollectionExtensions.cs:1421`. `IMonthlyActivityTrendQueryUseCase` has four callers.

| Caller | Path | After |
|---|---|---|
| `MonthlyActivityTrendCapabilityExecutor` (`SemanticCapabilityExecutors.cs:160`) | V2 semantic route; default | D5 resolution, D4 reply. **Customer sees** a chart when the row has data; otherwise the table in `user-story.md` |
| `SymbolSnapshotUseCase.ReadMonthlyAsync` (`SymbolSnapshotUseCase.cs:134`) | Feature 156 card | D5 resolution, using the eligible symbol. Null gives today's card text `گزارش ماهانه‌ای برای این نماد ثبت نشده است.` No port call |
| `FinancialCopilotWorkflowDefinition.cs:877` | Legacy route inside V2 (rollback, legacy or shadow mode) | D5 resolution. Null gives today's text `اطلاعات روند فروش ماهانه برای نماد «{symbol}» در پایگاه داده یافت نشد.` No port call |
| `AiQueryOrchestrationService.cs:561` | V1, under `EnterFrozenV1` | Unchanged: `ResolveBySymbolAsync` |

V1 semantic frames also reach the executor (`AiQueryOrchestrationService.cs:166-168`). There the scope is false, so no port call happens and the result is today's.

What the customer sees from the V2 executor when resolution does not give a company:

| `CompanyResolutionStatus` | Coverage | Customer sees |
|---|---|---|
| `Ambiguous` | `CompanyAmbiguous` | `نام نماد یا شرکت به‌طور قطعی مشخص نشد. لطفاً نام نماد دقیق را وارد کنید.` |
| `NotFound`, `InvalidIdentifier` | `Unknown` | Default no-data sentence |
| `UnsupportedProviderCompany` | `OutsideScope` | `این نماد در فهرست شرکت‌های تحت پوشش گزارش ماهانه نیست.`, reason `data_stale_or_ineligible` |

## Port contract (D4)

```csharp
// Application: FinancialData/Ingestion/MonthlyReportCoverageContracts.cs
public enum MonthlyReportCoverageKind { HasRoute, NoRoute, OutsideScope, Unknown, CompanyAmbiguous }
public sealed record MonthlyReportCoverage(MonthlyReportCoverageKind Kind, string? CategoryFa = null, string? CategoryEn = null);
public interface IMonthlyReportCoverageReader
{
    Task<MonthlyReportCoverage> ReadAsync(string symbol, CancellationToken cancellationToken);
}
```

- Called from `MonthlyActivityTrendCapabilityExecutor` only, after a null result, on V2.
- Adapter, Infrastructure: resolves the company with the D5 resolver; reads `ReportingType`, `ProviderName`, `PrecedencyRight`, `MarketId` of that row by id; then outside scope is checked first, then `INoavaranMonthlyReportTypeResolver`.
- Mapping: non-eligible row → `OutsideScope`. Route `Unsupported` → `NoRoute` with labels. Route `ProductSales` or `ServiceSales` → `HasRoute`. `Missing`, `Unknown`, `Malformed` → `Unknown`.
- Failure: the adapter links the caller's token with a 2,000 ms timeout. `catch (Exception) when (!cancellationToken.IsCancellationRequested)` logs Warning and returns `Unknown`. Log fields: `Route=monthly_activity_trend`, `CompanySymbol`, `Outcome=coverage_read_failed`, plus the exception. Same pattern as `NoavaranEligibleSymbolResolver.cs:29-33`. No retry.
- Success: Information log with `Route`, `CompanySymbol`, `Coverage`.
- `OutsideScope` shows `این نماد در فهرست شرکت‌های تحت پوشش گزارش ماهانه نیست.` with reason `data_stale_or_ineligible` (OQ-062). `Unknown` shows the default no-data sentence with reason `supported_but_no_rows`.

## Implementation Tasks

### 1. Scaffolding, no behaviour change

- [x] 1.1 Add `DialogueOutcomeReasonCodes.MonthlyReportNotCovered`, value `monthly_report_not_covered`. Add `MonthlyReportCoverage` and `IMonthlyReportCoverageReader`.
- [x] 1.2 Add `NoavaranCompanyScope.IsEligible(NormalizedCompanyRow)`, using the same constants as the query filter.
- [x] 1.3 Add `DialogueOutcomeReasonCodes.MultipleCompaniesNotSupported`, value `multiple_companies_not_supported`.

### 2. Regression tests first

Each test below is added and run before its fix, and must fail for the stated reason. Rows and questions are invented. Run with `dotnet test tests/FinancialCopilot.UnitTests --configuration Release --filter <test>`.

New file `tests/FinancialCopilot.UnitTests/Feature158CanonicalPriorityTests.cs`:

- [x] 2.1 `LegacyTickerRows_DoNotMakeEligibleSymbolAmbiguous`. Fails today: exact ticker on non-Noavaran rows returns `Ambiguous` and the alias step never runs (harness scenario S2).
- [x] 2.2 `NonEligibleNoavaranDuplicate_DoesNotMakeEligibleSymbolAmbiguous`. Fails today: two Noavaran alias rows are `Ambiguous` (S3).
- [x] 2.3 `OneLegacyTickerRow_DoesNotOutrankEligibleRow`. Fails today: `CanonicalId` is the other-provider row (S4).
- [x] 2.4 `MonthlyTrendTurn_WithLegacyDuplicates_ProducesEligibleCompanyFrame`, gate level. Fails today: slot state `Ambiguous`.
- [x] 2.5 Guards that pass today and must keep passing: `TwoEligibleRows_StayAmbiguous`, `TwoOtherProviderRows_WithNoNoavaranRow_StayAmbiguous`, `FrozenV1_KeepsTodaysAmbiguousResult` (uses `EnterFrozenV1`), `EligibilityPredicate_AgreesWithTheQueryFilter`.

New file `tests/FinancialCopilot.UnitTests/Feature158MonthlyCompanyResolutionTests.cs`, using real `CompanyResolverService` and `EfCoreCompanyMonthlyActivityTrendSnapshotRepository` over the in-memory provider:

- [x] 2.6 `NoavaranRowWithOtherProviderTicker_ReadsNoavaranSnapshot`. Fails today: step 1 of `ResolveBySymbolAsync` returns the other row's `ExternalCompanyId` and the snapshot lookup finds nothing.
- [x] 2.7 `NoavaranRowWithOtherProviderTseSymbol_ReadsNoavaranSnapshot`. Fails today: table-order `FirstOrDefault` returns the other row. The test saves that row first, in its own `SaveChangesAsync` (harness confirmed).
- [x] 2.8 `EligibleAndNonEligibleNoavaranRows_ReadEligibleSnapshot`. Fails today: `FirstOrDefault` returns the non-eligible row, which the test saves first (harness confirmed).
- [x] 2.9 Guard that passes today and must keep passing: `FrozenV1_KeepsResolveBySymbolBehaviour`. Added with this group: `TwoEligibleRowsWithOneSymbol_ReturnNull` (fails today: `FirstOrDefault` returns the first row's snapshot).

New file `tests/FinancialCopilot.UnitTests/Feature158MonthlyCoverageReplyTests.cs`:

- [x] 2.10 `NoRouteCode_ReturnsNotCoveredReasonAndExactReply` (theory, seven codes, exact Persian text). Fails today: the executor does not use the port and returns `supported_but_no_rows`.
- [x] 2.11 `EnglishReply_UsesCategoryEnAndOmitsSymbol`. Same reason.
- [x] 2.12 `OutsideScope_ReturnsIneligibleReasonAndOwnSentence` (`fa` and `en`, exact text). Same reason.
- [x] 2.13 `CompanyAmbiguous_ReturnsDisambiguation`. Same reason.
- [x] 2.14 Guards: `SupportedRouteAndUnknown_KeepTodaysReason`, `ResultPresent_DoesNotCallPort`, `FrozenV1_DoesNotCallPort`.

Adapter tests, in the same file, added with the adapter in 4.2 because the type does not exist before it:

- [ ] 2.15 `Adapter_MapsSevenCodesToCategories`, `Adapter_MapsSupportedMissingUnknownAndNotEligible`, including a company found only with another provider → `OutsideScope`.
- [ ] 2.16 `Adapter_ReadFailure_LogsWarningAndReturnsUnknown`, using a disposed context and a recording logger. Asserts one Warning with `coverage_read_failed`.
- [x] 2.17 `Reply_RendersThroughOutcomePolicy`: `ComposeSystemMessage` with the text as detail returns it for `fa` and `en`.

New file `tests/FinancialCopilot.UnitTests/Feature158MultipleCompanyClarificationTests.cs`, using the real gate, resolver and dispatcher validation with a model-proposal stub:

- [x] 2.18 `TwoExactCompanies_OnOneCompanyCapability_AskForOneCompany`. Fails today: the frame carries one company, the other is dropped.
- [x] 2.19 `TwoExactCompanies_WithLegacyDuplicates_AskForOneCompany`. Fails today: slot state `Ambiguous`.
- [x] 2.20 `Dispatcher_ReturnsClarificationWithNewReason`. Fails today: an `Invalid` slot maps to `entity_not_found`.
- [x] 2.21 `ClarificationReply_UsesProposedText` (`fa` and `en`). Fails today: the default clarification sentence.
- [x] 2.22 `PendingClarification_AnsweredWithOneTicker_RunsCapabilityForIt`. Fails today: no pending action exists, so the follow-up continues the earlier task (observed: capability `product_sales_trend`, because `RecordOutcomeAsync` stores the deterministic reading of the first ticker as a product).
- [x] 2.23 Guards that pass today and must keep passing: `SymbolMetricLookup_KeepsSeveralCompanies`, `OneExactCompany_DoesNotClarify`, `VariantMention_DoesNotClarify`, `DeterministicPath_DoesNotClarify`, `MonthNameMention_DoesNotClarify`, `FrozenV1_DoesNotClarify`.

### 3. Canonical priority and two-company clarification (D3)

- [ ] 3.1 In `CanonicalQueryEntityResolver.ResolveMentionAsync`, on `IsActive` run the exact steps per tier T1, T2, T3. Inject `Feature154RoutingScope?` like the other shared components. Keep today's code path for the inactive scope.
- [ ] 3.2 Tests 2.1 to 2.5 pass. Review the existing tests one by one, as listed above.
- [ ] 3.3 In `ConversationDialogueGate.PrepareAsync`, on `IsActive`, add the two-company check and pending disambiguation. In `SemanticCapabilityDispatcher.Validate`, return `ClarificationRequired` with the new reason. Add the Persian and English arms in `AiDialogueOutcomePolicy`. Tests 2.18 to 2.23 pass.

### 4. Monthly resolution and coverage (D5, D4)

- [ ] 4.1 Add the Infrastructure canonical monthly company resolver: eligible exact match, then `ResolveAsync`. Register it. `MonthlyActivityTrendQueryUseCase` takes it as a required dependency and `Feature154RoutingScope?` as an optional one. Update the `CreateUseCase` helper in `MonthlyActivityTrendStaleComparisonBug076Tests.cs` to pass it (constructor only; assertions unchanged). Tests 2.6 to 2.9 pass.
- [ ] 4.2 Add `NoavaranReportingTypeCategories` beside `NoavaranMonthlyReportTypeResolver`, and `NoavaranMonthlyReportCoverageReader`. Register the reader for all chat modes. Tests 2.15 and 2.16 pass.
- [ ] 4.3 In `MonthlyActivityTrendCapabilityExecutor`, take `IMonthlyReportCoverageReader` as a required dependency and `Feature154RoutingScope?` as optional. Map coverage as in `user-story.md`. Update the two constructor sites in `SemanticRouteExecutorTests.cs` (constructor only; assertions unchanged).
- [ ] 4.4 Add `AiDialogueOutcomePolicy.ComposeMonthlyReportNotCovered(language, categoryFa, categoryEn, symbol)` and `ComposeMonthlyReportOutsideScope(language)`. Tests 2.10 to 2.14 and 2.17 pass.

### 5. Read-only verification (no connection in this step)

No DSN exists. No connection is attempted from this plan. Per-symbol causes stay Unverified until the lead runs the checks.

- [ ] 5.1 `read-only-checks.sql` C2 now reports `noavaran_rows`, `eligible_rows`, `other_symbol_rows`, `category_fa`, `resolver_today`, and label F uses any other-provider symbol match. Reviewed by reading; not executed.
- [ ] 5.2 When the owner provides a read-only DSN, the lead runs the file with the read-only session options in its header. Output is not committed with connection details.
- [ ] 5.3 From C2, write `specs/158-.../evidence/symbol-causes.md`: one row per symbol, with symbol, label, plain reason (table below), `category_fa` where present, and the follow-up. Only public tickers, company names, and counts; no customer text. For فملی and فولاد, record `resolver_today` and `noavaran_rows`.
- [ ] 5.4 From C3, record how many eligible companies each `ReportingType` covers, and how many symbols are affected by `ambiguous_on_ticker` and `one_other_ticker_row`.
- [ ] 5.5 Report to the owner. Mark this group `[~]` if no DSN is provided.

| C2 label | Plain reason for the owner's list | Reply after this feature | Follow-up |
|---|---|---|---|
| `A_no_noavaran_row` | Company is not in the Noavaran catalog | Default no-data sentence, or `entity_not_found` | Catalog review |
| `B_outside_scope` | Rights, or outside the three markets | Owner's outside-scope sentence, `data_stale_or_ineligible` | None |
| `C_no_monthly_route` | Company type has no monthly route in the data source | **Owner's reply** | Vendor question (OQ-026) |
| `D_type_missing`, `E_type_unknown` | Company's reporting type is missing or new | Default sentence | Catalog refresh or review |
| `F_resolver_wrong_row` | Data exists; the symbol reached another provider's row | **Monthly data shown** after D5 | None |
| `G_data_present_recheck_routing` | Data exists; cause was elsewhere | Data shown | Investigate that turn |
| `H_reports_without_snapshot` | Reports exist; no snapshot | Default sentence | Snapshot backfill, needs approval |
| `I_never_attempted`, `J_ingestion_failing` | Ingestion gap | Default sentence | Backfill, needs approval |
| `K_vendor_returned_nothing` | Vendor had no report yet | Default sentence | None; retries continue |

**If none of the ten symbols has a no-route type,** D4 changes nothing for those ten symbols' questions. D4 still applies to the other companies in the seven types (count from C3). D5 helps only symbols labelled F. Symbols labelled A, D, E, H, I, J or K keep the default sentence, and goal 1 is then not met for them: the list gives the true reason to the owner, but the customer reply changes only with a further approved item, such as a backfill or new wording.

## Tests

- [ ] All tests in §2 pass; each was seen failing for its stated reason, or is a named guard.
- [ ] `tests/FinancialCopilot.ArchitectureTests`: no new failure. Application references no Infrastructure and holds no `ReportingType` number.

## Definition Of Done

- [ ] `dotnet test src/backend/FinancialCopilot.sln --configuration Release` green: no failure outside `specs/test-baseline.md`.
- [ ] For database work: not applicable, no migration.
- [ ] For frontend work: not applicable.
- [ ] Acceptance criteria in `user-story.md` all checked, or marked with the reason. §5 may stay `[~]` until a DSN exists.
- [ ] Reference docs updated: `docs/company-master-data-model.md` and `docs/domain-glossary.md` (coverage terms). `specs/README.md` entry updated by the lead.
- [ ] User-visible change: a Persian line under `## [Unreleased]` in `CHANGELOG.md`, by the lead.
- [ ] New questions → `specs/open-questions.md`; deferred work → `specs/backlog.md`; defects found → `specs/bugs.md`, by the lead.

## Risks

- **Two-ticker requests.** The clarification depends on the model proposing separate spans. When the model proposal is unavailable or returns one combined span, today's behaviour stays. The separator fallback path is out of scope.
- **Clarification reach.** Any capability with a `symbol` and no `symbols` slot is covered, not only monthly sales, per the owner's wording.
- **Row identity.** Questions in the "exactly 1 other-provider row" shape move from that provider's row to the eligible row. `CanonicalId` differs; slot text is normally the same. Executors reading `CanonicalCompany` (`SemanticCapabilityExecutors.cs:316`, `:368`) get the eligible row.
- **Unverified fixture parity.** Integration tests that need PostgreSQL may not run here.
- **`ReportingType` freshness.** The reply depends on the catalog value. Null or new codes keep the default sentence.
- **Shared scope name.** `Feature154RoutingScope` is reused for its existing V2/V1 meaning; its name is historical.

## Rollback

Revert the feature commits; no data or schema change. The new reason code is an additive persisted string; old rows stay readable.
