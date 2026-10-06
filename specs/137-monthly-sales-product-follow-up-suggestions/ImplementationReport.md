# Feature 137 Implementation Report

## 1. Outcome

Implemented deterministic V2 product follow-up suggestions for successful company monthly activity trend responses. The selector uses accepted company-scoped `ProductSales` observations, Feature-136-compatible product identity and canonical titles, parser round-trip validation, the common anchor period, checked sales aggregation, and stable ranking. Suggestions reuse the existing `SuggestedAction` persistence and API path. A V2 API integration test follows a returned action through normal routing to `MonthlyProductTrend`.

## 2. Files changed

**Production (10 files):**

- `FinancialCopilot.Application/FinancialData/Ingestion/MonthlyActivityTrendQueryContracts.cs`
- `FinancialCopilot.Application/FinancialData/Ingestion/MonthlyProductComparisonContracts.cs`
- `FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/EfCoreMonthlyProductComparisonRepository.cs`
- `FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/MonthlyProductTrendProductIdentity.cs` (new)
- `FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/MonthlyProductTrendQueryUseCase.cs`
- `FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/MonthlySalesProductFollowUpSuggestionService.cs` (new)
- `FinancialCopilot.Infrastructure/ServiceCollectionExtensions.cs`
- `FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs`
- `FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowMessages.cs`
- `FinancialCopilot.Infrastructure/AI/OrchestrationV2/Functions/MessagePersistenceFunction.cs`

**Test files (4):**

- `tests/FinancialCopilot.UnitTests/MonthlyProductTrend136Tests.cs`
- `tests/FinancialCopilot.UnitTests/MonthlySalesProductFollowUp137Tests.cs` (new)
- `tests/FinancialCopilot.UnitTests/MessagePersistenceFeature137Tests.cs` (new)
- `tests/FinancialCopilot.IntegrationTests/AiFacadeV2EndpointTests.cs`

Two frontend files already had uncommitted workspace edits when implementation began. They were left untouched and are excluded from the counts above.

## 3. Architecture summary

The EF repository exposes a single company-scoped accepted product-sales catalog read plus a common anchor period and its rows. A shared internal helper now owns the existing Feature 136 candidate grouping, title selection, matching, and key reuse. The selector validates generated product trend messages in memory and returns up to three deterministic actions. V2 attaches actions only after a usable typed `MonthlyActivityTrend` result. `Feature137SuggestionsApplied` distinguishes an explicit empty result from an inapplicable path; persistence preserves applied actions and bypasses generic guidance for that response. Existing API, web, conversation, and Telegram action contracts remain in use.

## 4. Task completion table

| Task | Status | Evidence |
|---|---|---|
| 1. Bounded candidate contract | Complete | Read result carries the company candidate universe, anchor period, and anchor observations; observation fields preserve product/provider identity, company scope, period, title, and sales amount. |
| 2. Shared Feature 136 identity/title helper | Complete | Feature 136 use case now uses the shared grouping, title, key, and matcher helper; focused Feature 136 tests pass. |
| 3. Accepted-data read and anchor | Complete | Existing set-based catalog query filters company, `ProductSales`, output type 0, accepted rows; repository test excludes null-only periods, header-only reports, and later periods from anchor selection. |
| 4. Parser/query safety | Complete | Selector gates with `LooksLikeMonthlyProductTrendQuery`, parses with `BuildQuery`, verifies normalized slots, and requires one shared matcher result for the same key. |
| 5. Aggregation and ranking | Complete | Checked per-key `SalesAmount` aggregation; null-only excluded and zero retained; deterministic amount/title/key ordering and limit of three. |
| 6. SuggestedAction construction | Complete | Focused tests verify Persian label/message, capability, kind, slots, and relevance reason; canonical company symbol chain is used. |
| 7. Stable IDs and version | Complete | SHA-256 ID uses feature, capability, registry version, external company ID, and product key; repeat and registry-version variation tested. |
| 8. V2 integration seam | Complete | Invoked after typed usable company trend results in V2 result computation; API test covers seven company-trend phrasings and a returned action round-trip. |
| 9. Persistence ownership | Complete | Applied actions, including empty and three-action collections, persist exactly and skip guidance; non-applied persistence still invokes generic guidance. |
| 10. End-to-end propagation | Complete | API integration response contains the persisted structured action; existing payload and API mappers are reused. |
| 11. V1 preservation | Complete | No V1 route, parser, selector, or branch added. Shared persistence defaults to the unchanged generic guidance path unless V2 explicitly marks Feature 137 applied. |
| 12. Web and Telegram consumers | Complete | Existing web component displays `label`, submits `message` with the action ID; existing Telegram renderer uses `LocalizedLabel` and `Message`. Existing consumer tests pass. |
| 13. Observability and fallback | Complete | Activity tags record invocation, duration, period, candidate/eligible/returned counts, rejection/ambiguity/duplicate counts, zero reason, read failure, and attribution. Selection exceptions return no actions without replacing the company response. |
| 14. Focused tests | Complete | Added selector, repository anchor, persistence, and endpoint round-trip tests; existing Feature 136 tests supply identity and resolution regressions. Focused combined run: 28 passed. |
| 15. Regression and rollout verification | Complete | Full solution, frontend, Telegram, and focused runs completed. Unrelated/environment integration failures are recorded below. |

## 5. Acceptance criteria evidence matrix

| AC | Result | Evidence |
|---|---|---|
| AC-1 | Verified | V2 monthly trend API integration returns structured actions. |
| AC-2 | Verified | Selector test with four eligible products returns exactly three. |
| AC-3 | Verified | Shared Feature 136 title/key helper; existing title and ambiguity tests; API action uses the canonical product title. |
| AC-4 | Verified | Selector obtains names only from accepted structured product rows; no LLM or client candidate-generation path. |
| AC-5 | Verified | Parser/build/slot/matcher checks are in memory; generated API action routes to a resolved `MonthlyProductTrend` result without per-candidate trend-use-case calls. |
| AC-6 | Verified | Null-only sales are excluded; common-anchor test requires a non-null sales observation. |
| AC-7 | Verified | Repeated selector execution yields identical IDs, messages, and ordering. |
| AC-8 | Verified | Sales-value fixture establishes ranking order; checked aggregation is used. |
| AC-9 | Verified | Selector uses `TseSymbol ?? Ticker ?? CompanySymbol`; tests verify symbol fallback and no-symbol empty result, and no display-name fallback. |
| AC-10 | Verified | Empty selector output is persisted explicitly when applied; persistence test confirms generic guidance is skipped. |
| AC-11 | Verified | Trend endpoint regression asserts existing text and chart fields remain present and unchanged. |
| AC-12 | Verified | Company trend snapshot/use-case tests pass in the full unit run; Feature 137 does not modify calculations. |
| AC-13 | Verified | Focused Feature 136 tests pass; its trend calculations are not changed. |
| AC-14 | Verified | API response exposes structured `suggestedActions`; integration checks action fields. |
| AC-15 | Verified | Seven different company monthly trend phrasings are exercised by the API integration test. |
| AC-16 | Verified | Code-path inspection: activation checks typed result, detected intent, usable points, and outcome, not a Persian phrase. |
| AC-17 | Verified | Code-path inspection: selector depends on company resolver and read repository only; no LLM/database access path was added. |
| AC-18 | Verified | No Feature 137 V1 wiring exists; persistence generic-path test passes and V1 defaults to `Feature137SuggestionsApplied=false`. |
| AC-19 | Verified | One-product selector case returns one action; three-result cap is tested. |
| AC-20 | Verified | Duplicate normalized titles are excluded; repeated ProductKey rows are grouped before action construction. |
| AC-21 | Verified | No-anchor/header-only conditions produce an empty read; duplicate-only selector fixture returns no fabricated action. |
| AC-22 | Verified | Existing optional action payload remains compatible; frontend suggested-action test and Telegram renderer tests pass. |

## 6. Feature 136 regression result

### Feature 136 query-equivalence prerequisite

- **Root cause:** `MonthlyProductTrendIntentRules` selected the company as the last
  non-stopword token and treated earlier non-stopwords as product text. It did not
  remove a default recent-history phrase or trailing conversational question words,
  so terms such as “recent” and “how was it” could contaminate product/company
  identity before the existing 12-position use case ran.
- **Files changed:** `MonthlyProductTrendIntentRules.cs`,
  `DeterministicCapabilityInterpreter.cs`, `SemanticCapabilityExecutors.cs`,
  `MonthlyProductTrendContracts.cs`, `MonthlyProductTrendQueryUseCase.cs`,
  `MonthlyProductTrend136Tests.cs`, `AiFacadeV2EndpointTests.cs`, and this report.
- **Normalization rule:** before intent gating and slot extraction, remove only
  explicit wording equivalent to the default recent 12-position history and the
  supported trailing conversational suffixes. ASCII/Persian 12, “twelve months,”
  and “one recent year” variants map to the implicit default. Other period phrases
  are retained as distinct input and represented by `unsupported_time_window`; the
  use case returns typed `NotFound` without resolving a company or calculating the
  default window. The semantic interpreter promotes these generally parsed product
  trends ahead of the broader company trend route. Absolute supported Jalali from/to
  periods keep their current path.
- **Focused tests:** Feature 136 unit class **42/42 passed**. This includes 15
  default-window/suffix equivalence cases, two full result equivalence cases across
  two product/company fixtures, and company-only routing coverage. Focused V2
  integration tests passed **15/15**, covering product routing, explicit-window
  API equivalence, company-only routing, and Feature 137 suggested-action round trips.
- **Negative period tests:** **7/7 passed** across unit and API coverage for 6, 3,
  and 18 months, Jalali year 1404, and a named Jalali month range. Each retains its
  distinct wording, preserves product/company slots for the typed response, and
  returns `unsupported_time_window` rather than the default history.
- **Feature 136 regression result:** **PASS.** The 12-position window and rate/value
  calculations are unchanged; equivalent parsed queries return identical periods,
  chart points, identities, and calculations.
- **Feature 137 action round-trip impact:** **PASS.** The normal generated action
  text is unchanged and still routes to a resolved product trend. The existing V2
  monthly trend/action round-trip integration cases passed with the parser change;
  the seven focused Feature 137 selector and persistence unit tests also passed.

Current focused Feature 136 and V2 monthly-trend regressions pass as recorded above. The Feature 137 API action still uses the normal query text and returns the resolved product trend; no action wording or Feature 136 calculation was changed.

## 7. V1 regression result

No Feature 137 action generation is wired into V1. The generic persistence path remains the default, and a focused persistence test confirms it still invokes `CapabilityGuidanceService`. No V1-specific Feature 137 production or DTO code was added.

## 8. Focused test results

- Combined Feature 136 / Feature 137 selector / persistence unit filter: **28 passed, 0 failed**.
- Feature 137 V2 monthly trend endpoint integration: **7 passed, 0 failed**; each case verifies a structured action and submits its message through the API to the product trend result.
- Build: **passed**, 0 warnings and 0 errors.

## 9. Full regression results

`dotnet test src/backend/FinancialCopilot.sln --configuration Release --no-restore`:

- Unit: **1,793 passed, 0 failed**.
- Architecture: **12 passed, 0 failed**.
- Integration: **384 passed, 73 failed, 41 skipped**.
- The 73 integration failures are outside Feature 137, including schema expectation mismatches, unrelated scanner/metric/Telegram/billing/market-insight assertions, and V2 semantic-route expectation mismatches. Two direct Feature 136 API tests are in the latter group. Six PostgreSQL tests report Docker/Testcontainers unavailable as an environment failure.

## 10. Frontend test results

`npm test -- --run`: **52 passed, 1 failed** across 11 files. The failure is the unrelated disclosure receipt-date assertion expecting the literal `(تهران)` suffix; the formatter returned the localized Tehran time without that suffix. Existing suggested-action rendering/click tests passed. No frontend files were changed for Feature 137.

## 11. Telegram verification

**PASS.** All 12 `TelegramAssistantResponseRenderer089Tests` passed. Code inspection confirms the renderer prints structured action labels and messages and receives the same persisted `AiQueryResponse.SuggestedActions` collection.

## 12. Performance/query-shape verification

**PASS.** One company-scoped set-based accepted product-sales catalog read supplies both canonical identity candidates and anchor rows. Anchor selection and aggregation run in memory. The selector does not invoke `MonthlyProductTrendQueryUseCase`, issue a per-product query, or call a provider. Repository and selector tests verify the anchor boundary and one read per selector invocation.

## 13. Manual/API validation

**PASS (automated API fixture).** The V2 test fixture uses a manufacturing company and a real structured product-sales row. Seven equivalent company-trend phrasings retain the existing answer/chart and return one structured action. The test submits the action message back to `POST /api/ai/v1/query` and verifies a resolved `MonthlyProductTrend` response with the canonical product title. No development database/manual live-host session was available or needed for this fixture-backed API validation.

## 14. Known unrelated failures

- Full integration run: 73 failures, 41 skips. Failures cluster in unrelated schema, scanner, symbol/metric lookup, Telegram membership/linking, billing, revenue-mix, market-insight, admin data-operation, and semantic routing tests. Docker/Testcontainers is unavailable for PostgreSQL tests.
- Frontend suite: one disclosure timezone suffix expectation failed; 52 tests passed.
- No Feature 137 focused test or its V2 API integration test failed.

## 15. Remaining blockers

None for Feature 137. The unrelated suite failures above remain recorded for their owning areas.

## 16. Rollout notes

Feature 137 is V2-only and returns zero actions for absent symbols, missing anchors, parser-unsafe/ambiguous/duplicate titles, or selector read failures. Existing chart, company calculations, product calculations, action API shape, generic guidance outside the applied Feature 137 response, and V1 routing remain unchanged. No migration or new client abstraction was introduced.
