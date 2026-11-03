# Feature 141 — Production Data Pilot Verification (2026-10-29)

Contents:
- Update: Noavaran company mapping fix — **FEATURE141_NOAVARAN_MAPPING_FIXED**
- Original pilot run, before the fix — FEATURE141_PRODUCTION_PILOT_FAILED (NO-GO)

# Update: Noavaran company mapping fix

Verdict: **FEATURE141_NOAVARAN_MAPPING_FIXED**

Feature 141 now finds Noavaran financial statements through the provider company identity `(ProviderName, ExternalCompanyId)`.
It no longer uses `FinancialStatements.CompanyId`. On real data it returns the owner's وبملت values exactly:
`1025278132`, `1271936747`, `303`, `376`, for the six months ending ۳۱ شهریور ۱۴۰۵. Pilot blocker B1 is resolved. Feature 141
stays disabled for real users, and the other pilot blockers remain (section 8).

## 1. Root cause

- **Catalog relationship.** The catalog had one statement-to-company relationship, `rel_e4fa0fe989156533`: the physical FK
  `FinancialStatements.CompanyId → Companies.Id`.
- **Snapshot builder.** `StructuredQueryCatalogSnapshotBuilder` attached that relationship to every direct source item.
- **Compiler binding.** `StructuredQueryCompiler.BindDirectSourcePhysical` refused any relationship other than exactly
  `CompanyId → Id`.
- **Generated SQL.** Statement SQL therefore filtered on `st."CompanyId" = @company_id` in `CompileDirect`, and on
  `c_st."CompanyId" = ANY(@company_ids)` plus `st."CompanyId" = cc."CompanyId"` in `CompileStatementComparison`.
- **Real data.** In the real database `CompanyId` is NULL for all 37,346 `NoavaranCurrentApi` statements.

The result: every statement question found no rows, and users got a false "no data".

What was already correct, and is unchanged:
- **Symbol resolution.** `CanonicalQueryEntityResolver` matches the symbol against `Companies.CompanySymbol` (among other
  symbol columns) and prefers `NoavaranCurrentApi` rows. It returns `Companies.Id`.
- **Line items.** Line items join statements through `FinancialStatementId`.
- **Source items.** Source items join through `SourceItemCatalogId`.

**About `NoavaranEligibleCompanies`.** `pg_get_viewdef` shows the view is `Companies` filtered to `ProviderName =
'NoavaranCurrentApi'`, `PrecedencyRight = 0` and four markets, joined to `IndustryGroups` and `Industries`. Its `Id` column
is `c."Id"`, that is, `Companies.Id`. This differs from contract point 3, which says the view's Id is independent of
`Companies.Id`.

The read-only principal `feature141_app_reader` has no SELECT on the view. It can read the base columns
`Companies.(ProviderName, ExternalCompanyId, CompanySymbol)`. That pair carries the unique index
`IX_Companies_ProviderName_ExternalCompanyId`, and all 37,346 Noavaran statements match exactly one company through it.

Feature 141 therefore joins through the view's base identity. No mapping table was added and no database permission
changed. Reading the view itself would need a DBA to grant SELECT to `feature141_app_reader`. Without the view, its
market and `PrecedencyRight` eligibility filters are not applied. Any company must still resolve from the user's symbol,
or belong to the tenant's reviewed universe.

## 2. Mapping correction

Noavaran statements now reach their company through the provider identity:

```
FinancialStatements st JOIN Companies co
  ON st."ExternalCompanyId" = co."ExternalCompanyId" AND st."ProviderName" = co."ProviderName"
WHERE co."Id" = @company_id            -- or = ANY(@company_ids)
```

- **New relationship.** The catalog gets a logical relationship, `rel_26d0b2a4b006b368`:
  `FinancialStatements.ExternalCompanyId → Companies.ExternalCompanyId`. It is a verified application relationship, runtime
  joinable, with provenance `product-owner-confirmed:Feature141-noavaran-company-mapping-2026-10-29`. The compiler always
  adds the `ProviderName` equality, because the key is the pair.
- **Physical FK record kept.** `rel_e4fa0fe989156533` is still in the catalog, because the FK exists and `CyclicalWaves`
  statements use it. It is marked `runtimeJoinable: false`.
- **Snapshot selection.** When two tables have more than one relationship, the snapshot builder takes the runtime-joinable
  one. A single relationship is used as before.
- **Compiler.** It accepts either relationship form. For the provider-identity form it adds the provider equality. It
  filters and returns the company as `Companies.Id` (`co."Id" AS "CompanyId"`) instead of the statement's `CompanyId`.
- **Column approvals.** The newly referenced columns are approved and authorized like the other compiler columns:
  `FinancialStatements.ExternalCompanyId`, `Companies.ProviderName` and `Companies.ExternalCompanyId`.
- **Unchanged rules.** Statement type, provider, period selection (latest period per basis, latest common period),
  consolidation scope, revision handling and source-item identity are untouched. SQL is still compiled only from the
  catalog with bound parameters; the LLM generates no SQL.

**Metric mapping is unchanged and comes from the catalog.** For `NoavaranCurrentApi` `IncomeStatement`:

| Concept | SourceItemId | Effective unit |
|---|---|---|
| `netProfit` (NET_PROFIT) | 143 | MillionIRR |
| `operatingProfit` (OPERATING_PROFIT) | 140 | MillionIRR |
| `earningsPerShare` (EPS) | 160 | IRRPerShare |
| `operatingProfitPerShare` | 161 | IRRPerShare |

No item IDs appear in question-specific code. The executed queries show the IDs and units arriving from the catalog
(section 4).

## 3. Changed files

| File | Change |
|---|---|
| `src/backend/FinancialCopilot.Application/FinancialData/Semantics/Navigation/StructuredQueryCompilation.cs` | `BindDirectSourcePhysical` accepts the provider-identity relationship and binds `Companies.Id` and `Companies.ProviderName`. `DirectSourcePhysicalBinding.CompanyJoin` builds the join. `CompileDirect` and `CompileStatementComparison` use it, and both now authorize the new columns. |
| `src/backend/FinancialCopilot.Application/FinancialData/Semantics/Navigation/StructuredQueryCatalogSnapshotBuilder.cs` | `SingleRelationship` picks the runtime-joinable relationship when several connect two tables |
| `specs/141-…/SemanticCatalog/relationships/rel_26d0b2a4b006b368.yaml` (new) | Provider-identity statement-to-company relationship |
| `specs/141-…/SemanticCatalog/relationships/rel_e4fa0fe989156533.yaml` | Physical FK kept, marked not runtime-joinable |
| `specs/141-…/SemanticCatalog/catalog.yaml`, `tables/tbl_53d51e26e1ca6929.yaml` (FinancialStatements), `tables/tbl_8646d83cdb350f92.yaml` (Companies) | Relationship index entry; column approvals for `ExternalCompanyId` and `ProviderName` |
| `specs/141-…/SemanticCatalog/manifest.yaml`, `artifacts/navigation-hierarchy.yaml`, `artifacts/navigation-hierarchy.md` | Regenerated with `catalog.py hierarchy build`. `catalog.py validate` passes; catalog hash `a617530e…` |
| `tools/SemanticCatalog/preflight_feature141.py` | Statement path relationship id updated |
| `tests/FinancialCopilot.UnitTests/Feature141CrossCompanyStatementComparisonTests.cs` | New test `StatementsJoinCompaniesThroughTheNoavaranProviderIdentityNotCompanyId` |
| `tests/FinancialCopilot.IntegrationTests/Feature141EngineVerticalSliceTests.cs` | Statement fixtures now mirror production: `CompanyId = NULL`, and companies and statements share their provider identity. Before, the single-company fixture's companies and statements had different external ids and were linked only by `CompanyId`. The independent comparison read joins through the provider identity. |
| `tests/FinancialCopilot.IntegrationTests/Feature141ProductionDataPilotTests.cs` | Pilot harness: وبملت questions, questions with the basis stated, and a recording wrapper around the read-only executor that stores the compiled SQL, parameters and rows |

Not changed: database schema, migrations, data, database permissions, Feature 129, ingestion, billing, chat routing and the
feature flag (`Feature141:Enabled=false`).

## 4. Generated SQL and parameters (captured from the real-data run)

Single company, P16 «سود خالص وبملت در آخرین دوره بر اساس صورت‌های مالی جداگانه چقدر است؟»:

```sql
WITH latest_period AS (
  SELECT MAX(st_scope."PeriodEnd") AS "PeriodEnd"
  FROM "public"."FinancialStatements" st_scope
  JOIN "public"."Companies" co_scope ON st_scope."ExternalCompanyId" = co_scope."ExternalCompanyId" AND st_scope."ProviderName" = co_scope."ProviderName"
  WHERE co_scope."Id" = @company_id
    AND st_scope."ProviderName" = @provider_name
    AND st_scope."StatementType" = @statement_type AND st_scope."IsComposing" = @is_composing
  GROUP BY st_scope."IsComposing"
)
SELECT li."Value" AS "Value", st."PeriodEnd" AS "PeriodEnd", st."Id" AS "StatementId", …, co."Id" AS "CompanyId", st."PeriodType" AS "PeriodType", …
FROM "public"."FinancialStatements" st
JOIN "public"."Companies" co ON st."ExternalCompanyId" = co."ExternalCompanyId" AND st."ProviderName" = co."ProviderName"
LEFT JOIN "public"."FinancialStatementLineItems" li ON li."FinancialStatementId" = st."Id" AND li."SourceItemCatalogId" = @source_item_catalog_id
WHERE co."Id" = @company_id
  AND st."ProviderName" = @provider_name
  AND st."StatementType" = @statement_type
  AND EXISTS (SELECT 1 FROM "public"."FinancialStatementSourceItems" si WHERE si."Id" = @source_item_catalog_id AND si."ProviderName" = @provider_name AND si."StatementType" = @statement_type AND si."SourceItemId" = @source_item_id)
  AND st."IsComposing" = @is_composing
  AND st."PeriodEnd" IN (SELECT "PeriodEnd" FROM latest_period)
ORDER BY st."PeriodEnd" DESC, st."PeriodType", st."Id"
LIMIT @row_limit OFFSET @row_offset
```

Parameters: `company_id=308d5136-079d-489b-a8d3-8db222706f41` (وبملت, `Companies.Id`),
`source_item_catalog_id=bd36909d-abf8-44c2-b7db-5e53dd678b53`, `provider_name=NoavaranCurrentApi`,
`statement_type=IncomeStatement`, `source_item_id=143`, `is_composing=false`, `row_limit=101`, `row_offset=0`.

The other items used the same SQL with these values:

| Item | `source_item_id` | `source_item_catalog_id` |
|---|---|---|
| Operating profit | 140 | `f2a6e460-176f-4426-a4e7-78d4ffd86ba6` |
| EPS | 160 | `2146da26-b28e-4d04-b50c-f49fc9b70e61` |
| Operating profit per share | 161 | `3cafc7d3-b0ab-4341-85ef-ebcbc9d60528` |

Comparison, P21 «سود خالص کگل و کگهر را بر اساس صورت‌های مالی جداگانه مقایسه کن.» (the comparison, ranking and filter SQL;
only the changed parts shown):

```sql
WITH candidate AS (
  SELECT c_co."Id" AS "CompanyId", c_st."PeriodStart" AS "PeriodStart", c_st."PeriodEnd" AS "PeriodEnd", c_st."PeriodType" AS "PeriodType", c_st."IsComposing" AS "IsComposing"
  FROM "public"."FinancialStatements" c_st
  JOIN "public"."Companies" c_co ON c_st."ExternalCompanyId" = c_co."ExternalCompanyId" AND c_st."ProviderName" = c_co."ProviderName"
  WHERE c_co."Id" = ANY(@company_ids)
    AND c_st."ProviderName" = @provider_name
    AND c_st."StatementType" = @statement_type AND c_st."IsComposing" = @is_composing
), company_coverage AS (…), common_period AS (…), target_period AS (…)
SELECT cc."CompanyId" AS "CompanyId", …, li."Value" AS "Value", st."PeriodStart" AS "PeriodStart", st."PeriodEnd" AS "PeriodEnd", …
FROM company_coverage cc
JOIN "public"."Companies" co ON co."Id" = cc."CompanyId"
LEFT JOIN "public"."FinancialStatements" st ON st."ExternalCompanyId" = co."ExternalCompanyId" AND st."ProviderName" = co."ProviderName"
  AND st."ProviderName" = @provider_name AND st."StatementType" = @statement_type AND st."IsComposing" = @is_composing
  AND EXISTS (SELECT 1 FROM target_period tp WHERE …)
LEFT JOIN "public"."FinancialStatementLineItems" li ON li."FinancialStatementId" = st."Id" AND li."SourceItemCatalogId" = @source_item_catalog_id
WHERE co."Id" = ANY(@company_ids) AND EXISTS (…source item identity…)
ORDER BY cc."CompanyId", st."PeriodType", st."Id"
LIMIT @row_limit
```

Parameters: `company_ids=[3386cf62-2fc7-427f-bbdf-364c4666d8df (کگل), 980f880b-32c9-467e-8007-19d9ef30dc19 (کگهر)]`,
`source_item_catalog_id=bd36909d-…`, `provider_name=NoavaranCurrentApi`, `statement_type=IncomeStatement`,
`source_item_id=143`, `is_composing=false`, `row_limit=101`.

Full SQL, parameters and returned rows for every question are in
`evaluation/artifacts/feature141-production-pilot/2026-10-29-145*.json` (`ExecutedQueries`).

## 5. Real-data verification

- **Database access.** Real data came from `financial_copilot` and was read only through `feature141_app_reader`.
- **Chat path.** Questions went through the production chat orchestration service with the live models.
- **Isolation.** Chat and billing ran on a disposable database with one fresh test user per question. Feature 141 was
  enabled only inside the test host.
- **Reproduction.** Same pilot harness as before: 22 questions plus the flag-off check, 16 + 6 runs, all harness checks
  passed.

Independent read-only SQL (provider identity join, no `CompanyId`) for the owner's example:

```sql
SELECT st."PeriodType", st."PeriodStart", st."PeriodEnd", st."IsComposing", st."CompanyId", si."SourceItemId", li."Value"
FROM "FinancialStatements" st
JOIN "Companies" c ON c."ProviderName" = st."ProviderName" AND c."ExternalCompanyId" = st."ExternalCompanyId"
JOIN "FinancialStatementLineItems" li ON li."FinancialStatementId" = st."Id"
JOIN "FinancialStatementSourceItems" si ON si."Id" = li."SourceItemCatalogId" AND si."ProviderName" = st."ProviderName" AND si."StatementType" = st."StatementType"
WHERE c."CompanySymbol" = 'وبملت' AND st."ProviderName" = 'NoavaranCurrentApi' AND st."StatementType" = 'IncomeStatement'
  AND si."SourceItemId" IN (140, 143, 160, 161) AND st."PeriodEnd" >= '2026-03-01';
```

The query found `Companies` row `308d5136-…`, `ExternalCompanyId=452`. Statement `0741e9de-dd4c-4033-a4a9-28e0e69b9e04`
is `SixMonths`, 2026-03-21..2026-09-22, standalone, with `CompanyId` NULL: 143 = 1025278132, 140 = 1271936747,
160 = 303, 161 = 376.

## 6. Expected vs actual

| # | Question | Route | Actual answer (Feature 141) | Expected (database) | Value | Period | Unit | Result |
|---|---|---|---|---|---|---|---|---|
| P16 | سود خالص وبملت … جداگانه | Feature 141 single company, `Succeeded` | «وبملت در دوره شش‌ماهه منتهی به ۳۱ شهریور ۱۴۰۵، مقدار مالی گزارش‌شده: ۱٬۰۲۵٬۲۷۸٬۱۳۲ میلیون ریال.» | 1025278132 MillionIRR, SixMonths ending 2026-09-22 | ✓ | ✓ | ✓ | **PASS** |
| P17 | سود عملیاتی وبملت … جداگانه | same | «… ۱٬۲۷۱٬۹۳۶٬۷۴۷ میلیون ریال.» | 1271936747 MillionIRR | ✓ | ✓ | ✓ | **PASS** |
| P18 | سود هر سهم وبملت … جداگانه | same | «… ۳۰۳ IRRPerShare.» | 303 IRRPerShare | ✓ | ✓ | ✓ (raw unit code shown) | **PASS** |
| P19 | سود عملیاتی هر سهم وبملت … جداگانه | same | «… ۳۷۶ IRRPerShare.» | 376 IRRPerShare | ✓ | ✓ | ✓ (raw unit code shown) | **PASS** |
| P20 | سود خالص کگل … جداگانه | same | «کگل در دوره سه‌ماهه منتهی به ۲۹ اسفند ۱۴۰۴، مقدار مالی گزارش‌شده: ۳۷٬۰۰۱٬۶۳۰ میلیون ریال.» | 37001630, ThreeMonths ending 2026-03-20 | ✓ | ✓ | ✓ | **PASS** |
| P21 | سود خالص کگل و کگهر را بر اساس صورت‌های مالی جداگانه مقایسه کن | Feature 141 comparison, `Succeeded` | «مقایسهٔ سود خالص در آخرین دورهٔ گزارش مشترک (دوره سه‌ماهه منتهی به ۲۹ اسفند ۱۴۰۴): کگهر ۴۴٬۱۶۴٬۳۵۰ … کگل ۳۷٬۰۰۱٬۶۳۰ میلیون ریال …» | کگهر 44164350, کگل 37001630, same period | ✓ | ✓ | ✓ | **PASS** |
| P11, P12, P14 | وبملت net profit, operating profit, operating profit per share, "در آخرین دوره" without a basis | Feature 141 single company, `Ambiguous` (`statement_scope_unspecified_across_consolidation_bases`) | «داده‌های موجود برای ارائهٔ یک پاسخ مالی یکتا کافی نیستند؛ نتیجه مبهم است.» | First returned row is the expected statement (1025278132 / 1271936747 / 376) | rows ✓ | — | — | mapping ✓; answer withheld by the existing consolidation rule |
| P01, P02 | کگل net profit, کگهر operating profit, previously failing | same, `Ambiguous` | same text | Rows include 37001630 / 48054919 for the three months ending ۲۹ اسفند ۱۴۰۴ | rows ✓ | — | — | false "no data" fixed; answer withheld as above |
| P15 | سود خالص کگل و کگهر را مقایسه کن (previously failing) | Feature 141 comparison, `Ambiguous` | «برای کگل هم صورت مالی تلفیقی و هم صورت مالی جداگانه وجود دارد؛ لطفاً مشخص کنید مقایسه بر اساس صورت‌های تلفیقی باشد یا جداگانه.» | Both bases exist for کگل | — | — | — | correct clarification |
| P13 | سود هر سهم وبملت در آخرین دوره | specialist `symbol_metric_lookup` (Feature 141 not invoked) | «… مقدار قابل اتکایی برای EPS … موجود نیست.» | 303 | — | — | — | specialist path, as P03 in the original pilot |

Why the unspecified-basis questions are withheld: وبملت, کگل and کگهر report both standalone and consolidated statements.
For وبملت the latest consolidated statement ends 2024-09-21 and the latest standalone one 2026-09-22. The existing rule
(latest period per basis, plus `LatestStatementObservationPolicy`) refuses to choose a basis for the user. This rule
was to be preserved, so it is unchanged. The comparison renderer asks which basis to use; the single-company answer
only says the result is ambiguous.

## 7. Focused regression

| Run | Scope | Result |
|---|---|---|
| Unit tests (focused) | `Feature141*`, cross-company, response generator / Jalali rendering, `StructuredQuery*`, monthly: 556 tests | 549 passed, 7 failed. The 7 fail identically on the untouched baseline worktree `D:\Source\fc-base141`; they assert older catalog states. The new provider-identity test passes. |
| Unit tests (full) | `FinancialCopilot.UnitTests`, 2,502 tests | 2,482 passed, 20 failed: the same 20 pre-existing failures |
| Live engine regression | `Feature141EngineVerticalSliceTests` (30): single-company retrieval (E01–E04, operating profit per share), comparison and ranking (E05, E06, E14, E21–E28), numeric filters and multi-condition filters (E15, E17, E29–E34), typed rejections (E18–E20), monthly production (E08, E09, monthly slice), discovery | **30/30 passed.** Statement fixtures now have `CompanyId = NULL`; each slice still checks its SQL result against an independent database read. |
| Live chat regression | `Feature141ChatMvpTests` (9 questions + flag-off rollback) | 8/10 passed. C1 and C4 returned the correct answers (C1 «… دوره سه‌ماهه منتهی به ۳۱ خرداد ۱۴۰۵ … ۱٬۲۰۰ میلیون ریال», C4 کگل ۱٬۲۰۰ / کگهر ۹۰۰) and passed every answer, period, Jalali and persistence check. They failed the final `Assert.NotNull(run.Usage)`: see section 8, item 1. |
| Offline integration | `Feature141*` and `MonthlyStructuredQuery*` without live opt-in | 39 passed, 41 skipped (live only), 1 failed. The failure is `CompiledMonthlyRetrievalAndTimeSeriesExecuteThroughRestrictedReadOnlyExecutor`, which also fails on the untouched baseline. |
| Catalog | `catalog.py validate` | Valid (`a617530e…`) |

Jalali rendering is covered by `Feature141JalaliRenderingTests` (in the focused unit run) and by the live answers above.
Monthly production and sales queries do not use the statement relationship; their slices pass.

## 8. Remaining blockers

1. **Billing in the current working tree: Feature 141 answers record no usage.** In every run after this session's
   concurrent billing edits, a turn answered by Feature 141 commits its reservation at 0 credits and returns `Usage=null`.
   Specialist and agent turns still return usage. The cause is the in-progress usage-based billing changes in the working
   tree (`AiFacadeBillingHook.cs`, last modified 18:30 today, plus `UsageReservationAuthorizationService.cs`,
   `UsageFinalizationService.cs` and `BillingFunctions.cs`), which this task did not touch. That causes the two chat-MVP
   failures. It needs to be resolved together with that billing work.
2. **Unspecified basis gives an ambiguous non-answer for single-company questions** (P01, P02, P11, P12, P14). Many real
   companies report both bases. Until a default basis or a clarifying question is decided, users must say
   «جداگانه/تلفیقی» to get a number. The comparison path already asks.
3. **Pilot B3 is unchanged: monthly data is unreadable.** `feature141_app_reader` has no SELECT on `MonthlyReports` and
   `MonthlyReportLineItems`. P09 failed again with `database_query_failed`.
4. **Pilot B2 is unchanged: population labels use `Companies.Ticker`, which is empty in real data.** This affects ranking
   and filter answers over the universe, not named companies.
5. **Pilot B5 is unchanged: no reviewed company universe is configured for real tenants.**
6. **Minor: per-share answers show the raw unit code `IRRPerShare`** instead of a Persian unit label.

The original pilot findings follow unchanged. B1 is resolved by this update; B4 (billing) should be re-checked against
the new billing work.

# Original pilot run (before the mapping fix)

Verdict at the time: **FEATURE141_PRODUCTION_PILOT_FAILED**

Recommendation at the time: **NO-GO.** Keep `Feature141:Enabled=false` for real users.

The pilot ran Feature 141 on real persisted financial data. It never produced a wrong number. It did produce wrong user-facing
facts: it told users "no qualifying data" for companies whose data exists. It also charged credits for non-answers, and once
charged twice for one turn. None of the 10 questions received a correct numeric answer from Feature 141.

## Pilot setup

| Item | What was used | Verified |
|---|---|---|
| Feature flag | `Feature141:Enabled` is `false` in `appsettings.json` (and compose `FEATURE141_ENABLED:-false`). The pilot turned it on only inside the test host. | Not enabled globally |
| Financial data | The real application database `financial_copilot` (PostgreSQL 17.2, port 5432), read only through the existing read-only principal `feature141_app_reader` (the `Feature141ReadOnly` connection of the Development configuration). The role has `default_transaction_read_only=on`, no superuser, and SELECT only on `Companies` and the three statement tables. | Read only; nothing written |
| Chat, billing and memory writes | A disposable PostgreSQL 18 database (port 55441) with the Auth, Billing, Conversation, Memory and SemanticCatalog migrations. Its `Companies` and statement tables are a copy of the real rows, read through the same read-only principal. | No real customer account touched |
| Chat routing | The production composition root (`AddFinancialCopilotInfrastructure`) and `IAiQueryOrchestrationService`, the service behind `POST api/ai/v1/query`, with the request an authenticated web user sends | Same as production |
| MAF V2 | `AiOrchestration:Mode=MicrosoftAgentFrameworkV2`; every answered turn reported this mode | Yes |
| LLM providers | The configured live providers (semantic router plus the Feature 141 intent extractor) | Live calls |
| Billing | Each question used a new pilot user provisioned like a new web user (Free plan, initial credits). The pilot recorded the reservations, ledger entries and wallet balance before and after each turn. | Test users only |
| Pilot company universe | Tenant pilot scope: کگل, کگهر, فملی (real `Companies.Id` values). The shipped configuration has no reviewed universe (see blocker B5). | Pilot config only |
| Rollback | The same host with the flag off | Verified (below) |

The owner database account was not used. The only code change is a new env-gated pilot harness,
`tests/FinancialCopilot.IntegrationTests/Feature141ProductionDataPilotTests.cs`. It is skipped unless
`FEATURE141_RUN_PRODUCTION_PILOT=1` and `FEATURE141_PILOT_READONLY_CONNECTION_STRING` are set. No production code,
configuration, catalog or data changed. Run result: 11/11 harness checks passed in 4.3 minutes. These checks cover only
persistence, settled reservations and flag-off behavior; they do not check correctness. Per-question artifacts are in
`evaluation/artifacts/feature141-production-pilot/2026-10-29-1342*..1346*.json`.

## Expected values (independent read-only SQL)

The statements were linked to companies through the provider identity
(`Companies.ExternalCompanyId` + `ProviderName` = `FinancialStatements.ExternalCompanyId` + `ProviderName`), using
`NoavaranCurrentApi` IncomeStatement items 143 (net profit), 140 (operating profit) and 160 (EPS). Values are as stored, in
million rials (EPS in rials per share).

| Company | Latest period | Net profit | Operating profit | EPS |
|---|---|---|---|---|
| کگل | دوره سه‌ماهه منتهی به ۲۹ اسفند ۱۴۰۴ (2025-12-22..2026-03-20), separate | 37,001,630 | 52,914,136 | 62 |
| کگهر | دوره سه‌ماهه منتهی به ۲۹ اسفند ۱۴۰۴ (2025-12-22..2026-03-20), separate | 44,164,350 | 48,054,919 | 294 |
| فملی | دوره سه‌ماهه منتهی به ۳۱ خرداد ۱۴۰۵ (2026-03-21..2026-06-21), separate | 725,886,485 | 856,351,461 | 691 |

- No (start, end, type) period is common to all three companies; کگهر's fiscal year ends in Dey. A correct three-company
  ranking or filter must therefore say so rather than rank.
- The latest period common to کگل and کگهر is the three months ending ۲۹ اسفند ۱۴۰۴.
- The monthly expectations (P04, P05, P09) cannot be derived read-only: the read-only principal has no SELECT on
  `MonthlyReports` or `MonthlyReportLineItems` (`ERROR: permission denied for table MonthlyReports`).

## Results per question

Credits are those committed for the turn (ledger total). Time is wall time of one `ExecuteAsync` call.

| # | Question | Route selected | Actual answer | Expected (database) | Numeric | Period | Unit | Credits | Time | Result |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | سود خالص کگل در آخرین دوره چقدر است؟ | `symbol_metric_lookup` (clarification: `entity_not_found`), then Feature 141 `SingleCompanyValue`, Status=NoData | «برای کگل در آخرین دورهٔ گزارش واجد شرایط دادهٔ واجد شرایطی پیدا نشد.» | ۳۷٬۰۰۱٬۶۳۰ میلیون ریال, three months ending ۲۹ اسفند ۱۴۰۴ | ✗ (false "no data") | ✗ | — | 1 | 18.7 s | **FAIL** |
| 2 | سود عملیاتی کگهر در آخرین دوره چقدر است؟ | same as #1, Status=NoData | «برای کگهر … دادهٔ واجد شرایطی پیدا نشد.» | ۴۸٬۰۵۴٬۹۱۹ میلیون ریال, three months ending ۲۹ اسفند ۱۴۰۴ | ✗ (false "no data") | ✗ | — | 1 | 16.1 s | **FAIL** |
| 3 | سود هر سهم فملی چقدر است؟ | `symbol_metric_lookup` answered (specialist; Feature 141 not invoked) | «واحد: میلیون ریال — برای نماد فملی مقدار قابل اتکایی برای EPS در داده‌های فعلی موجود نیست.» | ۶۹۱ ریال, three months ending ۳۱ خرداد ۱۴۰۵ | ✗ | ✗ | ✗ (EPS labeled million rials) | 1 | 5.9 s | **BLOCKED** (specialist path; its data is not fully reproducible read-only, see note) |
| 4 | تولید گندله کگل در آخرین ماه گزارش‌شده چقدر بوده؟ | agent route, no grounded result, then Feature 141: plan not eligible (`monthly_product_binding_required`) | «این درخواست با تعریف‌های تأییدشدهٔ پرس‌وجو قابل اجرا نیست.» | not derivable (no read grant) | — | — | — | 1 | 24.9 s | **BLOCKED** (B3) |
| 5 | مبلغ فروش گندله کگهر در آخرین ماه چقدر بوده؟ | `product_sales_value` (NoData), then Feature 141 not eligible (`monthly_product_binding_required`) | same refusal text as #4 | not derivable (no read grant) | — | — | — | 1 | 22.4 s | **BLOCKED** (B3) |
| 6 | سود خالص کگل و کگهر را در دوره مشترک مقایسه کن. | `symbol_metric_lookup` (`entity_not_found`), then Feature 141 `Compare` with explicit `commonPeriod`: `statement_query_shape_not_supported` | same refusal text as #4 | کگل ۳۷٬۰۰۱٬۶۳۰ vs کگهر ۴۴٬۱۶۴٬۳۵۰ میلیون ریال, three months ending ۲۹ اسفند ۱۴۰۴ | — (no answer) | — | — | 1 | 28.5 s | **FAIL** |
| 7 | شرکت‌ها را بر اساس سود خالص رتبه‌بندی کن. | `monthly_sales_quality_ranking` (NoData), then Feature 141 `Ranking` descending, Status=NoData | «در دامنهٔ شرکت‌های مجاز، مقدار معتبری برای سود خالص پیدا نشد.» | No period common to all three: a "no common period" disclosure | ✗ (false "no valid value"; right outcome class, wrong reason) | ✗ | — | **2** (duplicate) | 19.3 s | **FAIL** |
| 8 | شرکت‌هایی با سود خالص بالای ۱۰۰۰ میلیارد ریال را پیدا کن. | `stock_screening` (`required_input_missing`), then Feature 141 `Filter`: typed Unsupported `requested_result_unit_conversion_not_supported` | «این پرسش را نمی‌توانم به‌صورت قابل اتکا پاسخ بدهم …» | No common period across the universe (all three exceed 1,000,000 million rials in their own latest periods) | — (safe refusal) | — | billion-rial threshold not supported | 0 | 15.2 s | **FAIL** (unanswered) |
| 9 | کدام شرکت‌ها گندله تولید می‌کنند؟ | agent route, then Feature 141 `DiscoverPopulation`, Status=Failed (read permission denied on `MonthlyReports`) | «اجرای امن پرس‌وجو کامل نشد؛ نتیجهٔ مالی ارائه نمی‌شود.» | not derivable (no read grant) | — | — | — | 1 | 25.0 s | **BLOCKED** (B3) |
| 10 | کدام شرکت‌ها سود خالص بالای ۱۰۰۰ میلیارد ریال و EPS بالای ۲۰۰۰ ریال دارند؟ | `stock_screening`, then Feature 141 `Filter` (AND): typed Unsupported `requested_result_unit_conversion_not_supported` | same as #8 | No common period. In their own latest periods none has EPS > 2,000, so the result would be empty | — (safe refusal) | — | billion-rial threshold not supported | 0 | 18.9 s | **FAIL** (unanswered) |

Totals: 0 PASS, 6 FAIL, 4 BLOCKED. No answer contained a wrong number, a Gregorian date or a silently dropped filter. The
two threshold questions were refused with a typed outcome rather than run unfiltered.

Note on #3 and the specialist routes: the pilot's chat database holds only the tables the read-only principal can read. The
specialists' symbol resolution answered `entity_not_found` for کگل and کگهر, and #3 reported EPS as unavailable. That
behavior may come from tables that could not be copied read-only. It is therefore not evidence about production specialists,
and #3 is not counted against Feature 141.

## Chat and billing verification

| Check | Result |
|---|---|
| Routing and specialist priority | Preserved. Feature 141 ran only after the selected specialist route failed to answer (clarification, NoData or no grounded agent result). In #3 the specialist answered and Feature 141 was not invoked. |
| Conversation persistence | 11/11: the user message and the assistant answer are stored exactly as returned |
| Reservations settled | 11/11: no reservation left in `Reserved`, wallet `ReservedAmount` 0 after every turn, and the wallet decrease equals the ledger total |
| Typed refusals | #8 and #10 were finalized as `ValidationFailed` with 0 credits |
| Charges for non-answers | **Defect.** Credits were committed as `Completed` for "no data" (#1, #2, #7), "not executable" (#4, #5, #6) and a failed execution (#9) |
| Duplicate billing | **Defect.** In #7 the turn reservation (specialist `monthly_sales_quality_ranking`, NoData) and the separate `:feature141` reservation were both committed: 2 credits for one non-answer |
| Flag rollback | Verified. With the flag off, question #6 runs: Feature 141 not invoked, 0 queries, 0 credits, and the existing route's own clarification is returned and persisted |
| Jalali dates | No Feature 141 answer contained a Gregorian date; none contained a date at all, since none returned data |

## Remaining production blockers

1. **B1 — Statements are not linked to companies in production data.** *(Resolved by the Noavaran company mapping fix
   above.)* Feature 141 compiles statement queries with
   `FinancialStatements.CompanyId = <company>` and `ProviderName = 'NoavaranCurrentApi'`. Every one of the 37,346
   `NoavaranCurrentApi` statements in the real database has `CompanyId = NULL`; only `CyclicalWaves` statements are linked.
   Every statement question (single company, comparison, ranking, filter) therefore returns NoData. The answer tells the
   user no qualifying data exists, which is false. Fix needs an owner decision: either backfill `CompanyId` for Noavaran
   statements in ingestion, or compile the join through the provider identity (`ExternalCompanyId` + `ProviderName`).
2. **B2 — Company labels use an empty column.** Population answers (ranking, filter) name companies from `Companies.Ticker`,
   and the statement population compiler requires it. `Ticker` is empty for the real companies; the symbol is in
   `CompanySymbol`. Even with B1 fixed, ranked and filtered companies would be unlabeled.
3. **B3 — The read-only principal cannot read monthly reports.** `feature141_app_reader` has no SELECT on
   `MonthlyReports` or `MonthlyReportLineItems`. Monthly discovery fails (#9), and monthly product questions are refused
   (#4, #5). The reviewed setup script `tools/SemanticCatalog/sql/feature141-read-role-setup.sql` grants statement columns
   only, so a DBA grant (and a script update) is needed.
4. **B4 — Billing charges non-answers and can double-charge.** Turns where Feature 141 returns NoData, is not eligible to
   execute, or fails execution are committed as `Completed` (1 credit). When a specialist reservation is also committed,
   the user pays twice (#7). A failed execution should be zero-charge like the other provider failures, and one turn
   should settle at most once.
5. **B5 — No reviewed company universe in the shipped configuration.** `Feature141:CompanyDiscoveryScopes` is empty and no
   `ReviewedTenantCompanyBindings` are configured. Ranking, filter and discovery are unavailable to every real tenant until
   the owner defines the universe. The pilot supplied one for its own tenant only.

Non-blocking limitations seen in this pilot (Feature 141 refuses them safely):
- Thresholds stated in billion rials (`requested_result_unit_conversion_not_supported`).
- An explicit «در دوره مشترک» comparison (`statement_query_shape_not_supported`).
- The executor does not log the cause of a failed execution (#9 was diagnosed by a direct read-only check).

## Recommendation

**NO-GO** for real users. B1 alone makes every statement answer a false "no data". B3 disables the monthly capabilities.
B4 charges users for those non-answers. Re-run this pilot unchanged (same harness, same 10 questions) once B1–B5 are
resolved.
