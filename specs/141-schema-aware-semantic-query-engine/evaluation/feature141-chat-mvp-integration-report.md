# Feature 141 — MVP Chat Integration Report (2026-10-29)

Verdict: **FEATURE141_CHAT_MVP_PASSED**

The verified Feature 141 capabilities now answer in chat behind the existing `Feature141:Enabled` flag. The evidence comes from
the production chat orchestration service, `IAiQueryOrchestrationService`, which is the service behind `POST api/ai/v1/query`.
It ran in MAF V2 mode, composed from the real infrastructure registration, with the live model providers and an isolated
PostgreSQL database that holds the chat, billing and memory schemas plus a deterministic financial fixture. The final focused
live run, executed after the last code change, passed 40 of 40 tests (exit code 0): 9 chat questions, the flag-off rollback and
the 30 engine vertical slices. No question showed a wrong number. Feature 141 now defaults to off, and the rollback is a single
setting.

## Implemented integration changes

| File | Change |
|---|---|
| `Infrastructure/AI/OrchestrationV2/Workflow/Feature141ChatQueryService.cs` | Comparison, ranking and threshold-filter intents go to the verified `FinancialSourcePlannerHandoff.CreateCrossCompany`, after the same typed binding gate as the engine. The slice 3a chat refusal is removed. A **threshold guard** returns the typed `Unsupported` outcome for any numeric threshold on the general (monthly or discovery) planner path, so a monthly filter never runs as an unfiltered retrieval. The single exception is `production > 0` in a discovery question, which restates the discovery rule. A router-supplied symbol no longer overwrites a multi-company request. `AnswerAsync` reports whether the answer is **definitive**: an engine plan was answered or refused, or a constraint was refused with a typed outcome. |
| `Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs` | One helper, `TryFeature141FallbackAsync`, gives Feature 141 the turn only when the route that chat already selected **did not answer**: a semantic capability ending in clarification, disambiguation, unsupported or no data; a semantic stop; a direct metric lookup without rows or with unresolved symbols; or an agent turn without a grounded tool result. Only a definitive Feature 141 result replaces the non-answer, and existing answers always win. The fallback records the executed capability (`registered_financial_source`) and a typed outcome (Answered or Unsupported). The agent tool description now covers comparison, ranking, threshold filters and monthly product values. |
| `Application/.../Navigation/CrossCompanyStatementComparison.cs` | A ranking with no stated direction defaults to highest first. |
| `Application/.../Intent/LlmCanonicalSemanticIntentExtractor.cs` | One prompt sentence: a missing ranking direction alone is not a reason for clarification. |
| `Application/.../Intent/CanonicalBusinessMeasureConceptRegistry.cs` | The requested field `rank` is dropped for ranked results, because every row already carries its rank. This was observed live. |
| `API/appsettings.json` | `Feature141:Enabled` changed from `true` to **`false`** (disabled by default). |
| `docker-compose.yml` (api service) | `Feature141__Enabled: ${FEATURE141_ENABLED:-false}`. |

No new framework, layer, routing system, SQL generation, authorization model or billing rule was added. The planner, compiler,
authorizer, read-only executor and renderers are the verified engine components, unchanged. Tests added:
`Feature141ChatMvpTests` (live chat harness), `Feature141ChatThresholdGuardTests` (unit) and three unit assertions.
`MonthlyStructuredQueryPostgreSqlTests` now sets the flag explicitly, because it relied on the old default.

## Routing decisions

Specialist priority is preserved: the semantic router and the deterministic rules run first, exactly as before. Feature 141
answers only after the selected route fails to answer. The final run recorded these routes:

| Question | Route chat selected first | Its result | Feature 141 |
|---|---|---|---|
| C1 سود خالص کگل چقدر است؟ | `symbol_metric_lookup` (parsed «خالص» as the company) | ClarificationRequired (entity not found) | answered |
| C2 تولید گندله کگل در شهریور ۱۴۰۵ چقدر بود؟ | controlled fallback → direct metric lookup | unresolved symbol | answered |
| C3 مبلغ فروش گندله کگهر در شهریور ۱۴۰۵ چقدر بود؟ | `financial_statement_value_search` | NoData | answered |
| C4 سود خالص کگل و کگهر را مقایسه کن. | `symbol_metric_lookup` | ClarificationRequired | answered |
| C5 شرکت‌ها را بر اساس سود خالص رتبه‌بندی کن. | `monthly_sales_quality_ranking` | NoData | answered |
| C6 کدام شرکت‌ها سود خالص بالای ۱۰۰۰ میلیون ریال دارند؟ | `stock_screening` | ClarificationRequired | answered |
| C7 …سود خالص بالای ۱۰۰۰ میلیون ریال و EPS بالای ۲۰۰۰ ریال… | `stock_screening` | ClarificationRequired | answered |
| C8 کدام شرکت‌ها در شهریور ۱۴۰۵ گندله تولید کرده‌اند؟ | `disclosure_listing` | ClarificationRequired | answered (partial-coverage disclosure) |
| C9 کدام شرکت‌ها تولید گندله بالای ۵۰۰ تن داشته‌اند؟ | controlled fallback → agent | no grounded result | typed **Unsupported**, no query |

The semantic router's first choice varies between runs. In an earlier run, C3 went to `product_sales_value`, which answered
correctly by itself ("۰٫۰۷ میلیارد تومان", which equals 700 million rial). Feature 141 stayed out of that turn, and the harness
accepts either correct route.

## Questions tested and actual responses (final run)

All values match the fixture: the latest standalone quarter 1405/01/01–1405/03/31 for statement items, and Shahrivar 1405
(2026-08-23..2026-09-22) for monthly items. Prior-quarter values, the earlier month, another product, and فولاد (outside the
authorized universe) never appear. Each answer was persisted as the assistant message, the user message was persisted too, and
each turn was settled once by the existing billing hook.

| # | Actual chat response (abridged) | Engine status | Outcome | Billing |
|---|---|---|---|---|
| C1 | «کگل در بازهٔ ۲۰۲۶-۰۳-۲۱ تا ۲۰۲۶-۰۶-۲۱، مقدار مالی گزارش‌شده: ۱٬۲۰۰ میلیون ریال.» | Succeeded | Answered | 1 credit, Completed |
| C2 | «کگل گندله در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲، مقدار تولید ماهانه: ۲۵ تن.» | Succeeded | Answered | 1, Completed |
| C3 | «کگهر گندله در بازهٔ ۲۰۲۶-۰۸-۲۳ تا ۲۰۲۶-۰۹-۲۲، مبلغ فروش ماهانه: ۷۰۰ میلیون ریال.» | Succeeded | Answered | 1, Completed |
| C4 | مقایسه در ۱۴۰۵/۰۱/۰۱–۱۴۰۵/۰۳/۳۱: کگل ۱٬۲۰۰، کگهر ۹۰۰ میلیون ریال؛ بیشترین متعلق به کگل؛ مبنا جداگانه | Succeeded | Answered | 1, Completed |
| C5 | رتبه‌بندی از بیشترین به کمترین: ۱. فملی ۱٬۶۰۰ ۲. کگل ۱٬۲۰۰ ۳. کگهر ۹۰۰ میلیون ریال | Succeeded | Answered | 1, Completed |
| C6 | با شرط «سود خالص بیشتر از ۱٬۰۰۰ میلیون ریال»: فملی ۱٬۶۰۰، کگل ۱٬۲۰۰؛ «۱ شرکت دیگر … شرط را نداشتند» | Succeeded | Answered | 1, Completed |
| C7 | دو شرط: فملی ۱٬۶۰۰ (EPS ۳٬۲۰۰)، کگل ۱٬۲۰۰ (EPS ۲٬۴۰۰) ریال به ازای هر سهم | Succeeded | Answered | 1, Completed |
| C8 | تولید مثبت گندله: کگل ۲۵ تن، کگهر ۴۰ تن؛ «این فهرست جزئی است …» | Incomplete (product alias coverage) | Answered | 1, Completed |
| C9 | «این پرسش را نمی‌توانم به‌صورت قابل اتکا پاسخ بدهم …» (chat's standard Unsupported message) | not executed | **Unsupported** | 0, ValidationFailed |
| Flag off (C4) | «نماد یا شرکت درخواست‌شده در فهرست شرکت‌های معتبر پیدا نشد.» (the existing route's answer, unchanged) | — | ClarificationNeeded | zero-charge |

C9's typed refusal comes from Feature 141's guard, logged as `threshold_filter_not_supported_on_general_planner_path`. The chat
outcome policy then renders it with the existing standard Unsupported text. Chat turns took 16–27 s. Each one includes the
specialist attempt and Feature 141's own two model calls.

Per-question artifacts: `evaluation/artifacts/feature141-chat-mvp/2026-10-29-0949*..0953*-C*.json`. Each holds the route
log, Feature 141 log, response, persisted messages, usage and outcome. The full run output is `final-live-run.log`.

## Tests and exit codes

| Run | Scope | Result | Exit code |
|---|---|---|---|
| **Final focused live run (after the last code change)** | `Feature141ChatMvpTests` (9 questions + flag-off rollback) and `Feature141EngineVerticalSliceTests` (30) | **40/40 passed** | **0** |
| Earlier live run (before the `rank` and discovery fixes) | same 40 | 39/40; C5 and then C8 failed once each on live-model variance (requested field `rank`; a `production > 0` threshold on discovery). Both were refused safely, without a wrong number, and fixed deterministically with unit coverage | 1 |
| Offline integration | `Feature141`, `AiFacadeEndpointTests` (HTTP chat contract, authentication, billing), `TelegramAssistant130` | 54 passed, 41 skipped (live opt-in), 0 failed | 0 |
| Unit suite | `FinancialCopilot.UnitTests` | 2,456 total, 2,436 passed, 20 failed | 1 |
| Architecture tests | `FinancialCopilot.ArchitectureTests` | 11 passed, 1 failed | 1 |

The 20 unit failures are the same named set recorded before this task. The architecture failure,
`MigratedSemanticExecutors_DoNotReintroduceLegacySymbolExtraction`, also fails on pristine HEAD `ff73d3c`
(`D:\Source\fc-base141`). There are no new failures and none were fixed.

Verification coverage:

- Routing: route logs above.
- Values, units and periods: answer text checked against the fixture, with decoys excluded.
- Authentication: the request is built exactly as `AiFacadeController` builds it for an authenticated web user, and the
  Feature 141 gate still refuses requests without a tenant or actor (F141-E20). The HTTP authentication middleware itself is
  covered by the passing `AiFacadeEndpointTests`, not by the live harness.
- Billing: one settled usage per turn. Answers cost 1 credit; refusals and clarifications are zero-charge, per the existing
  policy.
- Persistence: user and assistant messages read back through `IMessageRepository`.
- Rendering: Persian text with units, Jalali and Gregorian periods, and the statement basis.
- Specialist regression: specialists keep first priority, and `product_sales_value` answered C3 by itself in an earlier run.
- Flag rollback: the flag-off test above.

## Exact activation and rollback configuration

Activation for a pilot environment only (the existing controls; nothing new):

```text
FEATURE141_ENABLED=true                                   # docker-compose api → Feature141__Enabled (default false)
FEATURE141_READONLY_CONNECTION_STRING=<read-only role>    # existing, required; SELECT on FinancialStatements,
                                                          # FinancialStatementLineItems, FinancialStatementSourceItems,
                                                          # Companies, MonthlyReports, MonthlyReportLineItems only
AiOrchestration__Mode=MicrosoftAgentFrameworkV2           # already the default in appsettings.json
```

Population questions (ranking, filters, discovery) also need the pilot tenant's existing discovery configuration. Without it,
the engine returns the typed refusal `company_discovery_scope_not_authorized`.

```text
Feature141__CompanyDiscoveryScopes__0__TenantId=<tenant id>
Feature141__CompanyDiscoveryScopes__0__ScopeId=<scope id>
Feature141__CompanyDiscoveryScopes__0__ApprovalReference=<owner approval reference>
Feature141__CompanyDiscoveryScopes__0__CompanyCoverageComplete=true|false
Feature141__QueryExecution__ReviewedTenantCompanyDiscoveryScopes__0=<tenant id>|<scope id>|<approval reference>
Feature141__QueryExecution__ReviewedTenantCompanyBindings__N=<tenant id>:<company id>   # one per reviewed company
```

Rollback: set `FEATURE141_ENABLED=false` (or unset it) and restart the API. Feature 141 services are then not registered, the
workflow receives no Feature 141 service, and the tool and every fallback disappear. Chat routing returns to its pre-Feature 141
behaviour. The flag-off test verifies this.

## Remaining blockers and caveats

1. **The default changed for deployments.** At HEAD, `appsettings.json` shipped `Feature141:Enabled=true`, and the API service
   did not override it, so the single-company statement tool was live for every authenticated V2 user. It is now off until
   `FEATURE141_ENABLED=true` is set. Any environment that relied on that tool must opt in.
2. **The pilot is environment-level.** The existing flag is per environment. A per-user pilot inside a shared environment would
   need an allowlist, which does not exist at HEAD; the removed Developer UAT allowlist is what the two pre-existing failing UAT
   tests describe.
3. **Owner decisions are still pending** (unchanged): countersign D1 (Companies.Name/Ticker) and the slice 1 directive records;
   per-tenant discovery scope and universe (D2/D3); and product alias coverage (D4). Because of D4, discovery answers (C8) carry
   a partial-coverage disclosure.
4. **Router misroutes add latency.** The semantic router often picks a specialist that fails first (for example, «سود خالص» is
   parsed as a company). Feature 141 answers afterwards, which adds latency. A specialist that answers *wrongly* instead of
   failing would keep priority; none did in these runs. Improving the router's statement-item routing is the next step if
   latency matters.
5. **Refusal wording is generic.** Typed refusals are shown with chat's standard Unsupported text, not Feature 141's specific
   reason; the existing outcome policy composes that text. The specific reason is logged.
6. Monthly numeric filters remain unsupported by design; they are refused, never executed unfiltered.
