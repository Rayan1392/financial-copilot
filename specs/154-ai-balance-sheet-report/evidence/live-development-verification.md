# Feature 154: live Development verification

**Run:** 2026-11-05 (last run `executedAtUtc` 2026-11-05T14:47:15Z, review round 3), Implementation Agent.
**Scope:** AC-S01, AC-M03, AC-M04, AC-C02, AC-C03, AC-C06 and AC-C13 against the application Development database (`financial_copilot`, localhost:5432).
**Result:** 17 of 17 live checks are **PASS-live**. No Feature 154 defect was found. One test expectation was wrong and was corrected; see §6.

**Template selection** in rows 1–3 is labelled **PASS-live (classification substituted)**, because it rests on the classification substitute in §3 (F154-V-04). Values, PD-22 placement, baselines and text in those rows are PASS-live.

| File | Contents |
|---|---|
| [`live-development-verification.json`](live-development-verification.json) | Machine output of the last evidence run, one record per check: stored vs payload values for each row, anchor and baseline IDs, every candidate's state and date, ratio and pp, a text excerpt, and `templateSelection` for rows 1–3. |
| [`live-development-queries.sql`](live-development-queries.sql) | The independent SQL behind the expected values. |
| [`live-development-discovery.sql`](live-development-discovery.sql) / [`-output.txt`](live-development-discovery-output.txt) | Discovery done before the run: the eligible companies, every standalone balance-sheet snapshot with its fiscal-year end, and the AC-S01 identity check. |
| `tests/FinancialCopilot.IntegrationTests/BalanceSheetReport154LiveDevelopmentTests.cs` | The runner, a `[SkippableFact]`. |

None of these files contains a secret. The connection string is read from local configuration when the run starts and is never written out.

## 1. Method

The runner sends each request through the **normal V2 endpoint path**: `POST /api/ai/v1/query` with `AiOrchestration:Mode=MicrosoftAgentFrameworkV2`. It runs the full HTTP pipeline, then the V2 semantic route, the balance-sheet use case, the EF reader and the text renderer. Requests use API-key authentication.

| Context / dependency | Where it pointed |
|---|---|
| `FinancialIngestionDbContext` (companies, company resolution, statements, line items, source-item catalog) | **Live Development**, role `feature141_app_reader`, session option `-c default_transaction_read_only=on`, no pooling |
| `AuthDbContext`, `SemanticCatalogDbContext`, `FinancialProviderDbContext` | A fresh disposable PostgreSQL 18 database (127.0.0.1:55441), created per run |
| Billing, conversation and memory stores | In-memory, from the standard `AiFacadeApiFactory` harness |
| `IAiModelClient` | The harness fake. The balance-sheet requests resolve deterministically; no model call computes or selects a value. |
| Eligible-company classification (`GetEligibleCompanyGroupsAsync`) | A substitute; see §3 |

**Expected values** come from independent SQL on a separate connection with the same read-only role (`live-development-queries.sql`). That SQL applies the §5.4 ranking: it keeps eligible standalone `MillionRials` snapshots whose source scope agrees, then orders by `PeriodEnd` desc, `IsAudited` desc, `IsRepresented` desc, `PublishedAt` desc (nulls last), and finally `ExternalStatementId` and `Id` desc. The test derives the ratio, change, percent and pp from the **stored** values, never from the payload. It then compares the payload's `balanceSheetReport` and the `textAnswer`.

**Checks per request:**

- **Default report:** the anchor statement ID, the template, every displayed row (payload vs stored), PD-22 placement (only 47 and 51 sit under a current or non-current subgroup), the equity-to-assets ratio, and total assets in the text answer.
- **Comparison:**
  - anchor and baseline statement IDs, the selected baseline kind, and the candidate's state, reason and date;
  - every row's anchor and baseline value against stored;
  - change = anchor − baseline;
  - percent only when baseline > 0;
  - pp from unrounded ratios;
  - no quarter wording on a difference.
- **Unavailable baselines:** state `unavailable`, `previous_report` in the alternatives (never selected silently), «گزارش قبلی» in the text, and for S-9 the named dates.

## 2. Safety confirmation

- `SHOW default_transaction_read_only` → `on` on the verification session. The application connection carries the same option.
- Write attempt through the app's `FinancialIngestionDbContext`: `UPDATE "Companies" SET "Name" = "Name" WHERE false` → **SQLSTATE 25006** «cannot execute UPDATE in a read-only transaction». The test asserts this before sending any request.
- The runner refuses to start unless the connection is `localhost`, database `financial_copilot` and user `feature141_app_reader`.
- No permission was granted, no owner account was used, and no migration was applied to Development. Every writable context points at the disposable database or at in-memory stores.

## 3. Substitution (the part that is not live)

The read-only role has `SELECT` only on `Companies`, `FinancialStatements`, `FinancialStatementLineItems` and `FinancialStatementSourceItems`. It has no access to the `NoavaranEligibleCompanies` view, `IndustryGroups` or `Industries` (see `T01-development-evidence.md`).

The production repository reads classification from the view. The run therefore replaces **only** `IQuarterlyFinancialReportsRepository.GetEligibleCompanyGroupsAsync` with the test seam `LivePredicateClassification`:

- It reads `GroupId` from live `Companies` using the **deployed** view's predicate: the 4 MarketIds from `pg_views`, `PrecedencyRight = 0`, and non-null `GroupId`/`IndustryId`.
- The non-null columns stand in for the view's two INNER JOINs. `FK_Companies_IndustryGroups_GroupId` and `FK_Companies_Industries_IndustryId` guarantee those joins.
- The income-statement method still delegates to the real repository.

So the **eligibility and template classification** part of each check (Banking vs General) is **substituted, not live**. In the JSON and in §5 it is labelled `templateSelection: PASS-live (classification substituted)`. Every other part — company resolution, statement selection, values, baselines, comparison math and text — read live Development data through the production code.

This substitution adds **no new Development gate**. It is the same predicate-plus-foreign-key method that verified E-5 for Development (`T01-development-evidence.md`).

It does not carry over to other environments. E-5 anywhere else must query **that environment's real deployed view** (`E-5-bank-groupid-per-environment.md`).

## 4. Discovery (live Development, read-only)

All candidates pass the live predicate.

| Company | ExternalCompanyId | GroupId | Template | Latest eligible standalone snapshots (PeriodEnd, PeriodType, audited) | Fiscal-year end |
|---|---|---|---|---|---|
| وبملت | 452 | c96520f9… | Banking | 409865 2026-09-22 6M (f); 403733 2026-06-21 3M (f); 403749 2026-03-20 12M (**t**) and 394247 2026-03-20 12M (f) at the same date; 385967 2025-12-21 9M; 384526 2025-09-22 6M (t and f rows) | Esfand (1405/12/29; previous 1404/12/29) |
| شگل | 167 | 97ac765e… | General | 388474 2026-03-20 3M (f); 387608 2025-12-21 12M (t and f rows); 382183 2025-09-22 9M; 383026 2025-06-21 6M; 373612 2025-03-20 3M | Non-Esfand (1405/09/30; previous 1404/09/30) |
| فاذر | 290 | baab0118… | General | 403362 2026-06-21 3M (f); 394349 2026-03-20 12M (394350 at the same date is excluded because its source scope disagrees) | Esfand (1405/12/29) |
| بکهنوج | 13097 | bda2fd40… | General | 387517 2025-12-21 12M (f, fiscal-year end 1404/09/30); 383632 2025-10-22 9M (fiscal-year end 1404/10/30) | **Calendar changed** from 10/30 to 09/30 (S-6) |
| ومعین | 12548 | 3fa1c65c… | General | 403340 2026-06-21 6M (t); 385614 2025-12-21 12M; 378304 2025-06-21 6M. **No 3M at 2026-03-20.** | Non-Esfand (1405/09/30) |
| ساربیل | 236 | b761049c… | General | 409030 2026-09-22 **9M** (f); 388181 2026-03-20 3M (f); 387010 2025-12-21 12M (t and f rows); 382074 2025-09-22 9M (f). **No 6M at 2026-06-21.** | Non-Esfand (1405/09/30; previous 1404/09/30), unchanged |
| منجیل | 15763 | bda2fd40… | General | 409088 2026-09-22 **9M** (f); 404753 2026-06-21 6M (t); 404745 2026-03-20 3M; 404769 2025-12-21 12M (t); 404751 2025-06-21 6M. **No 9M at 2025-09-22.** | Non-Esfand (1405/09/30; 1404/09/30), **unchanged** |

Other issuers with a fiscal-calendar change or transitional year in Development: 12620, 12939, 13150 and 13156.

**AC-S01 identity (live):** statement `4ce25b02-7ca8-44c2-a71a-7520ac65cf90` is the only row with ExternalStatementId 409865. Provider `NoavaranCurrentApi`, ExternalCompanyId 452, **`CompanyId` is NULL**.

## 5. Results per criterion

Amounts are in million rials. "Stored" means the independent SQL; "payload" means Sapio's `balanceSheetReport`; "text" means Sapio's `textAnswer`.

| # | AC | Request (exact) | Company | Anchor (selected = expected) | Baseline (selected = expected) | Expected vs actual (key values) | Result |
|---|---|---|---|---|---|---|---|
| 1 | AC-S01 + AC-M03 (template: classification substituted) | «ترازنامه وبملت» | وبملت 452 | 409865, 1405/06/31, 6M, `4ce25b02…` | — | Banking template.<br>All 16 rows equal stored, including: cash 3,536,996,227; item 4 2,207,758,628; loans (291) 45,209,234,799; deposits (223) 27,590,264,476; item 39 8,454,139,269; capital (57) 3,382,000,000; treasury (170) (181,354,538).<br>Subtotals 135/137/139/140: 51,498,227,670 / 7,612,077,011 / 52,501,759,193 / 126,830,069.<br>Totals: assets 59,110,304,681; liabilities 52,628,589,262; equity 6,481,715,419; L+E 59,110,304,681.<br>Ratio 10.9655% = stored.<br>No General row. Total assets appears in the text. | **PASS-live** |
| 2 | AC-M04 (template: classification substituted) | «ترازنامه شگل» | شگل 167 | 388474, 1404/12/29, 3M, `edaa0f04…` | — | General template. All 20 rows equal stored, including capital 6,149,600, 306 = 13,869,662 and 307 = 14,406,886.<br>PD-22: only 47 (9,611,743) sits under current liabilities. Rows 1, 306, 18, 19, 21, 307 and 45 sit at section level. 135/137/139/140 are subtotal rows.<br>Ratio 30.4427% = stored. | **PASS-live** |
| 3 | AC-M04 (template: classification substituted) | «ترازنامه فاذر» | فاذر 290 | 403362, 1405/03/31, 3M, `4cc567ec…` | — | General template. All 23 rows equal stored, including capital 18,126,475, 306 = 36,753,822 and 307 = 30,611,284.<br>47 = 4,388,395 sits under current and 51 = 0 under non-current. The other details sit at section level.<br>Ratio 63.4400% = stored. | **PASS-live** |
| 4 | AC-C02 | «ترازنامه وبملت را با پایان سال مالی قبل مقایسه کن» | وبملت 452 | 409865 (1405/06/31) | `fiscal_year_end` → 403749, 1404/12/29, 12M, **audited**, `f797c618…`. The unaudited 394247 at the same date loses on §5.4. | The text names «پایان سال مالی قبل (۱۴۰۴/۱۲/۲۹)»; the date comes from the fiscal-year-end evidence.<br>All rows: change = a − b, and percent = stored.<br>Total assets 59,110,304,681 vs 47,947,640,334 (+11,162,664,347, +23.28%).<br>pp −0.73268 = stored. | **PASS-live** |
| 5 | AC-C03 | «ترازنامه شگل را با پایان سال مالی قبل مقایسه کن» | شگل 167 | 388474 (1404/12/29) | `fiscal_year_end` → 387608, **1404/09/30**, 12M, audited, `6e377554…`. This is not 1404/12/29 (the Esfand date). For this anchor the previous report falls on the same date, so the "not the previous report" distinction cannot be seen live here (see §7). | The text names «پایان سال مالی قبل (۱۴۰۴/۰۹/۳۰)».<br>Total assets 38,342,655 vs 30,902,203 (+7,440,452, +24.08%).<br>pp −4.45639 = stored. | **PASS-live** |
| 6 | AC-C06 | «ترازنامه وبملت در مقایسه با مدت مشابه سال قبل» | وبملت 452 | 409865 (6M, 1405/06/31) | `same_stage_previous_year` → 384526, **1404/06/31, 6M**, audited, `88c595cd…` | Same stage is matched by fiscal start plus months.<br>Total assets 59,110,304,681 vs 33,069,709,555 (+78.74%).<br>pp −4.70974 = stored. | **PASS-live** |
| 7 | AC-C06 | «ترازنامه شگل در مقایسه با مدت مشابه سال قبل» | شگل 167 | 388474 (3M, 1404/12/29) | `same_stage_previous_year` → 373612, **1403/12/30, 3M**, `975cd431…` | The fiscal year starts 10/01, and 1403/12/30 is the 3-month end of the previous fiscal year (Esfand 30 in a leap year).<br>Total assets +57.77%.<br>pp +8.97496 = stored. | **PASS-live** |
| 8 | AC-C06 (S-6) | «ترازنامه بکهنوج در مقایسه با مدت مشابه سال قبل» | بکهنوج 13097 | 387517 (12M, 1404/09/30) | `same_stage_previous_year` **unavailable**, `fiscal_calendar_changed` | Alternatives are [`fiscal_year_end`, `previous_report`]; neither is selected.<br>The text says «… در دسترس نیست (سال مالی ناشر تغییر کرده است). مبناهای در دسترس: پایان سال مالی قبل (۱۴۰۳/۱۰/۳۰)، گزارش قبلی (۱۴۰۴/۰۷/۳۰)». | **PASS-live** |
| 9 | AC-C13 | «ترازنامه وبملت را با فصل قبل مقایسه کن» | وبملت 452 | 409865 (6M, 1405/06/31) | `previous_fiscal_quarter_end` → 403733, **1405/03/31**, 3M, `d0855f5e…` | Labelled «پایان فصل قبل (۱۴۰۵/۰۳/۳۱)». No quarter wording on differences.<br>Total assets +11.01%.<br>pp −0.28965 = stored. | **PASS-live** |
| 10 | AC-C13 | «ترازنامه شگل را با فصل قبل مقایسه کن» | شگل 167 | 388474 (**3M** anchor) | `previous_fiscal_quarter_end` → 387608, 1404/09/30 (the previous fiscal-year end), 12M, audited | A 3-month anchor resolves to the previous fiscal-year end.<br>pp −4.45639 = stored. | **PASS-live** |
| 11 | AC-C13 (transitional) | «ترازنامه بکهنوج را با فصل قبل مقایسه کن» | بکهنوج 13097 | 387517 (12M, 1404/09/30) | **Unavailable**, `fiscal_calendar_changed` | Alternatives are [`previous_report`, `fiscal_year_end`].<br>The text offers «گزارش قبلی (۱۴۰۴/۰۷/۳۰)» and «پایان سال مالی قبل (۱۴۰۳/۱۰/۳۰)»; neither is selected. | **PASS-live** |
| 12 | AC-C13 (S-9 shape) | «ترازنامه ومعین را با فصل قبل مقایسه کن» | ومعین 12548 | 403340 (6M, 1405/03/31) | **Unavailable**, `no_snapshot_at_date`, named date 2026-03-20 | The text says «مبنای مقایسه «پایان فصل قبل (۱۴۰۴/۱۲/۲۹)» در دسترس نیست (در این تاریخ ترازنامه جداگانه موجود نیست). مبناهای در دسترس: گزارش قبلی (۱۴۰۴/۰۹/۳۰)، …».<br>«فصل قبل» stays distinct from «گزارش قبلی»; the date is not moved and the alternative is not selected. | **PASS-live** |

| 13 | AC-C03 (not the previous report) | «ترازنامه ساربیل را با پایان سال مالی قبل مقایسه کن» | ساربیل 236 | 409030 (9M, 1405/06/31) | `fiscal_year_end` → 387010, **1404/09/30**, 12M, **audited**, `2d815cfe…` | Not 1404/12/29, and not the previous report: `previous_report` is 388181 at 1404/12/29, a different statement and date; the test asserts both.<br>Total assets 30,976,404 vs 27,089,654 (**+3,886,750**, +14.35%).<br>pp **−5.14104** = stored. | **PASS-live** |
| 14 | AC-C03 (contrast) | «ترازنامه ساربیل را با گزارش قبلی مقایسه کن» | ساربیل 236 | 409030 | `previous_report` → 388181, 1404/12/29, 3M, `a2d357e9…` | Total assets 30,976,404 vs 25,304,834 (**+5,671,570**, +22.41%).<br>pp +28.94041 = stored. | **PASS-live** |
| 15 | AC-C06 (exact dates) | «ترازنامه ساربیل در مقایسه با مدت مشابه سال قبل» | ساربیل 236 | 409030 (**9M, 1405/06/31**; fiscal year from 1404/10/01) | `same_stage_previous_year` → 382074, **9M, 1404/06/31**, `f1fb48ef…` (previous fiscal year from 1403/10/01) | Total assets 30,976,404 vs 21,966,926 (**+9,009,478**, +41.01%).<br>pp −0.35829 = stored. | **PASS-live** |
| 16 | AC-C13 (S-9 shape) | «ترازنامه ساربیل را با فصل قبل مقایسه کن» | ساربیل 236 | 409030 (9M, 1405/06/31) | **Unavailable**, `no_snapshot_at_date`, named date 1405/03/31 | The text says «مبنای مقایسه «پایان فصل قبل (۱۴۰۵/۰۳/۳۱)» در دسترس نیست (در این تاریخ ترازنامه جداگانه موجود نیست). مبناهای در دسترس: گزارش قبلی (۱۴۰۴/۱۲/۲۹)، …».<br>Not selected, not moved to a nearby date. | **PASS-live** |
| 17 | AC-C06 (missing match) | «ترازنامه منجیل در مقایسه با مدت مشابه سال قبل» | منجیل 15763 | 409088 (9M, 1405/06/31) | **Unavailable**, `no_matching_stage`, named date 1404/06/31; the fiscal calendar is **unchanged** | The text says «مبنای مقایسه «مدت مشابه سال مالی قبل (۱۴۰۴/۰۶/۳۱)» در دسترس نیست (گزارش هم‌دوره سال قبل موجود نیست). مبناهای در دسترس: پایان سال مالی قبل (۱۴۰۴/۰۹/۳۰)، پایان فصل قبل (۱۴۰۵/۰۳/۳۱)، گزارش قبلی (۱۴۰۵/۰۳/۳۱)».<br>The 6M 1404/03/31 is not used as a substitute. | **PASS-live** |

The ساربیل total-assets changes and pp in rows 13–15 equal the verifier's independent measurements (+3,886,750 / pp −5.141; +9,009,478; +5,671,570).

Across all comparisons, no amount appeared without both stored values, every amount equals anchor − baseline, and no percent appeared for a non-positive baseline. The full per-row values are in the JSON.

## 6. Defects and test corrections during this round

- **No product defect found.**
- **Test expectation corrected (not a product change).** The first version of the unavailable-baseline check required `previous_report` to be the **first** alternative. Row 8 lists it second. Spec §8.2 and design §5 say alternatives are "the other kinds with state available"; their order is not specified. The check now requires `previous_report` to be **present** and offered in the text. The rerun passed.
- Rows 8 and 12 were added this round so that the transitional and S-9 sub-clauses of AC-C06 and AC-C13 are covered live rather than only by fixtures.
- **Review round 3 (F154-V-05):** rows 13–17 were added on ساربیل and منجیل. They move these clauses from fixture-only to live:
  - AC-C03 "not the previous report";
  - AC-C06 exact 9M dates;
  - AC-C06 missing match with an unchanged calendar;
  - a 9M S-9 shape.
- The check also asserts the expected unavailable reason for rows 8, 11, 12, 16 and 17.

## 7. Exact fixture absent (kept separate from the results)

These are spec values from the supplied or synthetic fixtures that the current Development load does not contain. The behavior was verified live on the nearest real data (§5). The exact figures remain verified only by fixtures and unit tests.

| AC | Spec fixture value | Development | Status |
|---|---|---|---|
| AC-S01 | Statement 409865 with Id `cb18b9c3-…` | 409865 exists with Id `4ce25b02-…` (a different load of the same statement). Provider, ExternalCompanyId 452 and blank `CompanyId` all match. | Selection is live; the exact Id is absent |
| AC-M03 | cash 409,287,621; deposits 27,590,264,477 | cash 3,536,996,227; deposits 27,590,264,476. Loans 45,209,234,799, capital 3,382,000,000 and treasury (181,354,538) **match** the fixture. | Exact cash and deposits are absent; Sapio shows the stored values |
| AC-M04 | §11.1 snapshots at 1405/06/31 | Latest is شگل 1404/12/29 3M and فاذر 1405/03/31 3M. Capital 6,149,600 and 18,126,475 **match**. | Exact snapshot date is absent |
| AC-C02 | Anchor 1405/06/31 → 1404/12/29 | Same dates, live (row 4) | Live |
| AC-C03 | S-2 synthetic values (assets 60,000,000; +2.10 pp) | S-2 is synthetic by definition; live شگل gives −4.46 pp | Exact figures are fixture-only; the behavior (1404/09/30 baseline) is live |
| AC-C06 | 9M anchor 1405/06/31 → 9M 1404/06/31 | No 9M **شگل** at 1405/06/31. The **same dates** are live on ساربیل (row 15). | Clause **live**; only the شگل fixture identity is absent |
| AC-C13 | S-9: 9M anchor 1405/09/30, missing 1405/06/31, previous report 1405/03/31 | The shape is live on ومعین (row 12) and on a 9M ساربیل anchor (row 16, missing 1405/03/31, previous report 1404/12/29) | **Exact S-9 dates absent** (E-4); the shape is live |
| AC-C03 | Baseline differs from the previous report | Live on ساربیل (row 13): fiscal-year end 1404/09/30 vs previous report 1404/12/29. (For شگل the two coincide.) | Clause **live** |
| AC-C06 | "Missing match" case (no same-stage snapshot, calendar unchanged) | Live on منجیل (row 17), `no_matching_stage` | Clause **live** |

**Remaining fixture-only items (E-4):**
- the S01 surrogate Id `cb18b9c3`;
- the supplied M03 cash and deposits;
- the supplied M04 1405/06/31 values;
- the S-2 +2.10 pp;
- the exact S-9 dates.

## 8. How to rerun

```bash
# Export the read-only connection from local configuration (never echo it), then:
export FEATURE154_LIVE_DEV_READONLY_CONNECTION_STRING=...   # Host=localhost; Database=financial_copilot; Username=feature141_app_reader
export FINANCIAL_COPILOT_TEST_POSTGRES_CONNECTION_STRING=... # disposable PostgreSQL
export Authentication__Email__Enabled=false
dotnet test tests/FinancialCopilot.IntegrationTests --filter "FullyQualifiedName~BalanceSheetReport154LiveDevelopmentTests"
# Regenerate the checked-in evidence JSON only on purpose:
export FEATURE154_LIVE_DEV_WRITE_EVIDENCE=1
```

- Without `FEATURE154_LIVE_DEV_READONLY_CONNECTION_STRING`, the test is **skipped** and is never counted as a pass.
- By default a run writes its JSON to the temp folder (`%TEMP%/feature154-live-development-verification.json`) and leaves the checked-in evidence untouched.
- Only with `FEATURE154_LIVE_DEV_WRITE_EVIDENCE=1` does it overwrite `evidence/live-development-verification.json`. The current file came from such a run on 2026-11-05T14:47:15Z.
