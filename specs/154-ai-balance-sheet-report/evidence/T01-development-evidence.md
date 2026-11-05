# Feature 154 — T01 read-only Development evidence

- **Date:** 2026-11-04. **Database:** local Development `financial_copilot` (port 5432).
- **Access:** role `feature141_app_reader` (the repository's `ConnectionStrings:Feature141ReadOnly`), every session forced to `default_transaction_read_only=on` (verified: `SHOW default_transaction_read_only` = `on`). Nothing was written. Production was not inspected.
- **Queries:** [T01-queries.sql](T01-queries.sql). **Raw output:** [T01-query-output.txt](T01-query-output.txt).
- **Role privileges:** `SELECT` on `Companies`, `FinancialStatements`, `FinancialStatementLineItems`, `FinancialStatementSourceItems` only. The role has **no** access to the `NoavaranEligibleCompanies` view or the `IndustryGroups` table (they are not visible in `information_schema`). No permission was granted and no owner account was used.

## Summary per gap

| Gap | Result | Behavior in v1 |
|---|---|---|
| E-1 fallback equivalence | **Not verified.** Disagreements and unit anomalies found (below). No provider definition, no analyst sign-off. | Every fallback stays **disabled** (`verified=false`). |
| E-2 restated linkage | **Not verified.** 484 restated BS records (357 companies) exist; no stored field links them to a publishing report. | v1 uses the exact-date stored snapshot; no anchor-link wording. |
| E-3 fiscal evidence | **Verified for 452, 167, 290** (and all 12,448 BS records carry a parseable `jalaliFiscalYearEnd`). Transitional-year issuers exist. | Fiscal-dependent baselines enabled where evidence exists. |
| E-4 fixture persistence | **Partially verified.** 409865 and 403733 for 452 exist with the supplied identity, date and flags; totals 138/143/147/148, capital 57, treasury 170 and loans 291 equal the supplied values. Cash (1) and deposits (223) differ from the supplied values. 167 and 290 have **no** 1405/06/31 statement in Development. | Supplied values used as test fixtures only; Development values are reported as observed. |
| E-5 bank GroupId | **Verified in Development** using the live (deployed) view predicate, which has 4 MarketIds and INNER JOINs; see the correction below. | Banking template signed off for Development. Other environments still need their own check, against their own deployed view definition. |
| E-6 consolidated EXISTS | **Verified**: 452 and 290 have consolidated BS records, 167 has none. No company has only consolidated BS. | Suggestion shown only when the query-time EXISTS is true. |
| E-7 screenshot | Received (evidence/reference-codal-two-sided-comparison.png). | Visual review in T22. |
| E-8 current/non-current of 1, 306, 18, 19, 21, 307, 45 | **Not verified.** The catalog has no hierarchy; only the titles below. Long-term siblings 74, 79, 50 exist (suggestive only). | Rows stay at section level (PD-22). |

## E-5 — bank GroupId (release-relevant)

**Correction (F154-V-02, final verification).** The migration `20260610100455_AddMonthlyActivityBackfillAndLineItemDetails` defines `NoavaranEligibleCompanies` as `SELECT * FROM "Companies" WHERE "ProviderName"='NoavaranCurrentApi' AND "PrecedencyRight"=0 AND "MarketId" IN (3 market ids)`. **The live Development view is different.**
- Read read-only from `pg_views`, it has **4** MarketIds: it adds `78ea1ed8-3242-4081-9e09-e6dbf34090e5`.
- It **INNER JOINs** `IndustryGroups` (on `GroupId`) and `Industries` (on `IndustryId`).
- This is pre-existing schema drift that no migration contains.
- The first check below used the migration predicate. It was re-run with the **live** predicate (§"Live-predicate re-check"), with the same result.

**E-5 checks in any other environment must use that environment's deployed view definition** (`SELECT definition FROM pg_views WHERE viewname = 'NoavaranEligibleCompanies'`), not the migration text.

First check, with the migration predicate applied to `Companies` (which the role can read):

| ExternalCompanyId | Symbol | Eligible GroupId | ReportingType | Eligible rows |
|---|---|---|---|---|
| 452 | وبملت | `c96520f9-1d2e-444a-ab46-5bdffd3797d2` | 1000003 (Banking) | 1 |
| 167 | شگل | `97ac765e-c5d6-4e5d-b9de-eb9e0b4e806c` | 1000000 | 1 |
| 290 | فاذر | `baab0118-18a2-4adf-b668-c4c0bb05527d` | 1000005 | 1 |

- `QuarterlyFinancialReports:BankingIndustryGroupId` (appsettings.json) = `c96520f9-1d2e-444a-ab46-5bdffd3797d2` = the eligible GroupId of 452. **Match.**
- 167 and 290 have different, single GroupIds, so they are non-bank (General template).
- All 17 eligible companies with `ReportingType = 1000003` share this one GroupId, and the GroupId contains only those 17 banks (دی, سامان, سمایه, وبصادر, وبملت, وپارس, وپاسار, وپست, وتجارت, وخاور, وسالت, وسینا, وشهر, وکار, وگردش, وملل, ونوین).
- Caveat: this role cannot query the view, `IndustryGroups` or `Industries`, so the evidence applies the view's predicate to the base table. The group name «بانک‌ها» is confirmed only by membership, not by title.

**Live-predicate re-check (2026-11-04, `default_transaction_read_only=on`; queries `q4_live_view.sql` and `q5_live_e5.sql` appended to T01-queries.sql, output appended to T01-query-output.txt):**
- **Predicate used:** the 4 live MarketIds. The two INNER JOINs are represented by non-null `GroupId` and `IndustryId`, which `pg_constraint` backs with `FK_Companies_IndustryGroups_GroupId` and `FK_Companies_Industries_IndustryId`. Every non-null value therefore has its join row.
- **Result:** 452, 167 and 290 each still have exactly **one** eligible row, with the same GroupIds as above. 452 is in `c96520f9-1d2e-444a-ab46-5bdffd3797d2` (the configured bank GroupId); 167 and 290 are non-bank.
- The bank GroupId still has **17** eligible rows, all with ReportingType 1000003.
- **Conclusion:** E-5 is verified for Development with the deployed view definition.

## E-4 — fixture persistence (452)

| | Supplied (spec §11.1) | Development 409865 |
|---|---|---|
| Statement | Ext 409865, SixMonths, 2026-09-22, unaudited, not restated, standalone, PublishedAt 2026-10-22 | Same (Id `4ce25b02-7ca8-44c2-a71a-7520ac65cf90`; the spec's `cb18b9c3-…` is another environment's surrogate) |
| CompanyId | blank | blank (`CompanyId IS NULL`) |
| 1 cash | 409,287,621 | **3,536,996,227** |
| 57 capital | 3,382,000,000 | 3,382,000,000 |
| 291 loans | 45,209,234,799 | 45,209,234,799 |
| 223 deposits | 27,590,264,477 | **27,590,264,476** |
| 170 treasury | −181,354,538 | −181,354,538 |
| 135 / 137 / 139 / 140 | present | 51,498,227,670 / 7,612,077,011 / 52,501,759,193 / 126,830,069 |
| 138 / 143 / 147 / 148 | 59,110,304,681 / 52,628,589,262 / 6,481,715,419 / 59,110,304,681 | identical |
| 60 | 0 | 0 |
| item 2 (central bank) | — | absent in 409865 |
| Fallbacks present | 298–305 | only 300, 303, 304, 305 (equal to primaries) |

- 403733 (verified, consistent with spec S-1): ThreeMonths, 2026-06-21 (1405/03/31), unaudited, not restated, standalone, PublishedAt 2026-07-21, `jalaliFiscalYearEnd` 1405/12/29.
- Two discrepancies (cash, deposits) mean the supplied 452 values are not the Development values for those two rows. Tests use the supplied values as fixtures; live runs show the Development values.
- 167 (شگل) latest standalone BS in Development is 388474, ThreeMonths, 2026-03-20 (1404/12/29). 290 (فاذر) latest is 403362, ThreeMonths, 2026-06-21. Neither has the supplied 1405/06/31 snapshot, so AC-S01/AC-M04 live runs for 167 and 290 cannot pass against these fixtures in Development.

## E-3 — fiscal-calendar evidence

- 452: `jalaliFiscalYearEnd` 1403/12/30, 1404/12/29, 1405/12/29 (Esfand issuer). 409865 and 403733 carry 1405/12/29.
- 167: 1403/09/30, 1404/09/30, 1405/09/30 (non-Esfand, Azar). Present on IncomeStatement, BalanceSheet and CashFlow.
- 290: 1403/12/30, 1404/12/29, 1405/12/29 (Esfand).
- All 12,448 Noavaran BS records with array `WarningsJson` carry a `jalaliFiscalYearEnd` (0 missing).
- Transitional-year candidates (fiscal-year-end month changed): for example 12548 ومعین {09,10}, 12620 ومشان {02,09}, 12939 غپآذر {09,10}, 13156 آپ {10,12}.
- Live baselines for 452 at anchor 1405/06/31: fiscal-year end 1404/12/29 (2026-03-20; 403749 audited and 394247 unaudited exist, they disagree, audited ranks first); previous fiscal quarter-end 1405/03/31 (403733); previous report 403733; same stage 1404/06/31 (2025-09-22, 384526).

## E-1 — fallback inventory (all standalone Noavaran BS statements, 6,857)

| Primary | Fallback | Both present | Primary only | Fallback only | Disagree |
|---|---|---|---|---|---|
| 135 | 298 | 6,357 | 500 | 0 | 5 |
| 137 | 299 | 6,357 | 500 | 0 | 6 |
| 138 | 300 | 6,842 | 15 | 0 | 11 |
| 139 | 301 | 6,357 | 500 | 0 | 6 |
| 140 | 302 | 6,309 | 548 | 0 | 3 |
| 143 | 303 | 6,842 | 15 | 0 | 10 |
| 147 | 304 | 6,842 | 15 | 0 | 5 |
| 148 | 305 | 6,842 | 15 | 0 | 10 |
| 306 | 293 | 6,108 | 8 | 256 | 0 |
| 307 | 308 | 6,107 | 8 | 257 | 0 |

- Disagreements include material differences (for example company 14, statement 392100: 137 = 1,071,336 vs 299 = 3,693,183) and **unit anomalies** (company 15610, statement 382127: 135 = 4,332,666 vs 298 = 4,332,666,656,477, i.e. the «- کدال» value is in rials). Banks show ±1 rounding differences (394, 396, 448, 581).
- 256/257 statements have 293/308 without 306/307. Enabling those fallbacks would change rows for those statements, which needs a verified definition.
- No provider definition of the «- کدال» items or of 293/308 is available in the repository, and analyst sign-off cannot be given by an agent. **All fallbacks stay disabled.**

## E-2 — restated records

- 484 standalone/consolidated BS records with `IsRepresented=true` (357 companies), mostly TwelveMonths and audited. Most have an original record at the same `PeriodEnd`, sometimes with the **same** `ExternalStatementId` (for example company 1: 394572 restated and original) and sometimes a different one (company 10: 400270 restated, originals 375958/400121).
- No column or evidence field identifies the later report that published a restated comparative. **No deterministic link**; E-2 stays open. v1 behavior is unaffected.

## E-6 — consolidated existence

- 452: consolidated BS exists (for example 370048 at 2024-09-21). 290: exists. 167: none.
- No Development company has consolidated BS records without standalone ones, so the `no_standalone_balance_sheet` + suggestion path is exercised with synthetic data only.

## E-8 — subgroup evidence

- Catalog titles: 1 «موجودی نقد», 18 «موجودی مواد و کالا», 19 «سفارشات و پیش پرداخت‌ها», 21 «دارایی‌های ثابت مشهود», 45 «پیش دریافت‌ها», 306 «دریافتنی های تجاری و سایر دریافتنی ها», 307 «پرداختنی های تجاری و سایر پرداختنی ها». None states maturity. Siblings 74 «حسابها و اسناد دریافتنی بلند مدت», 79 «حصه بلندمدت پیش دریافت از مشتریان», 50 «حساب‌ها و اسناد پرداختنی بلندمدت» exist, which suggests 306/45/307 are current portions, but that is inference. **Not verified; rows stay at section level.**

## What could not be checked, and why

- `NoavaranEligibleCompanies` and `IndustryGroups` directly: the read-only role has no SELECT on them. E-5 used the view's exact predicate over `Companies` instead.
- Provider definitions (E-1, E-2) and analyst sign-off (E-1, E-8): not available to an agent.
- Supplied 167 and 290 1405/06/31 amounts: not present in Development.
