# Feature 161 — T01 Development evidence

Date: 2026-11-14. The `feature141_app_reader` and, with explicit owner authorization, `maahfit` connected to Development `financial_copilot` on localhost; `SHOW default_transaction_read_only` returned `on`. No provider or production connection was used. [SQL](T01-queries.sql) and [tab-separated output](T01-output.txt) record the survey. The SQL parameter `bank_group` is the configured `QuarterlyFinancialReports:BankingIndustryGroupId` from `appsettings.json`.

The population comprises 12,228 stored standalone `NoavaranCurrentApi` cash-flow statements, including revisions, not distinct issuers or selected versions. Fiscal-year grouping comes from `WarningsJson` fiscal-year-end metadata. Company groups are provisionally classified from `Companies` with `PrecedencyRight = 0`, using a unique GroupId and the configured bank GroupId. The initial `feature141_app_reader` survey cannot read `NoavaranEligibleCompanies`; subsequent queries labelled `Eligible-view` use the live view through the authorized `maahfit` connection. The initial classification and population are retained for audit; use the eligible-view results for eligible companies. The live view contains 17 banks, all represented in the section survey. The section survey sums a candidate set of details and cannot prove individual P-item placements.

## Key results

- 197 statements have nonzero 120 (169 in fiscal year 1403 and 28 in 1404). No duplicate catalog-item rows were observed per surveyed statement.
- On the 12,031 statements with 120 zero or unreported, item 128 was reported on 7,752 (64.43%). This is a proxy population, not proof that every record is a three-section statement. Restricting to records with both 121 and 123 reported yields 7,460 candidates, all 7,460 with item 128 present. On the 7,492 with all inputs, V3 `128 + 121 = 122` held for 7,487; R1 `128 + 121 + 123 = 124` held for 7,455 of 7,460 comparable records. Missing values are excluded from each denominator; equality is exact.
- Across all 12,228, 128 was missing on 4,279; 121 or 123 was missing on 4,571. Zero is treated as reported.
- FX: 4,684 statements have item 35 absent or null; 3,925 have explicit zero and 3,619 have nonzero FX. Among nonzero FX, one general-classified statement already closes `33 + 124 = 125`; 3,497 close only with the reported FX under the separate test. The comparable denominators differ because balances or 124 may be absent. Of absent/null FX records, 277 have `33 + 124 = 125` and one has a comparable mismatch; the remainder lack other required inputs.
- The candidate investing and financing sums are tabulated by 829 general-classified and 17 bank-classified companies; they include proposed rows and do not establish placement or completeness. Operating detail is not independently decomposable from the available subtotal and tax item. The eligible-view recheck covers all 17 eligible banks; it does not establish individual item placement.
- The 2026-09-22 standalone headers for 255 and 452 have six-month periods, fiscal year end `1405/12/29`, `MillionRials` scale, and the reported totals match the supplied fixture amounts in the raw output. This is T01 data inspection, not T20 live feature validation. Both supplied datasets remain test fixtures pending T20.

## Eligible-view recheck

The live eligible-company view selects 11,163 standalone stored statements (229 bank and 10,934 general). Excluding the 197 nonzero-120 records leaves 10,966 candidates: 7,752 have item 128. All 7,460 candidates with both Codal totals 121 and 123 reported also have 128. V3 holds in 7,487 of 7,492 comparable statements; R1 holds in 7,455 of 7,460. All 17 eligible banks have cash-flow records and appear in the candidate section-closure survey, together with 829 general companies. These are counts of stored statement versions, not selected distinct reporting periods.

Among eligible records, 35 is absent or null in 3,619; it is explicitly zero in 3,925 and nonzero in 3,619. One nonzero-FX record closes without FX and 3,497 close with FX. For absent/null FX, 277 close without FX and one comparable record fails. Other records lack required inputs.

## Evidence gaps

| Gap | Finding and limitation |
|---|---|
| E-01 | Catalog and presence by live eligible template/fiscal year exported; the role-limited initial survey is retained separately. |
| E-02 | Signs and explicit zeros/nulls surveyed per item; sign alone does not establish accounting placement. |
| E-03 | Item 128 coverage and exact V3/R1 counts above; 126/121 and 127/123 pairwise counts are in the raw output. No fallback is authorized. |
| E-04 | 197 nonzero-120 records by year; missing Codal totals are counted. The alternate legacy-only test has not been separated out. |
| E-05 | Not closable from subtotal observations alone: item 1 and tax placement lack independent accounting proof. |
| E-06 | Candidate section-sum survey covers all 17 eligible banks and 829 general companies; individual P placements are not closable without analyst review. |
| E-07 | FX counts above; no adjustment to 124 is inferred. |
| E-08 | Fixture headers, flags, metadata, group IDs and all cash-flow items plus income-statement 15/143/313 recorded for the target date. T20 has not run. |
| E-09 | No persisted null values among the surveyed catalog-linked rows; absent and explicit-zero counts recorded. Provider payload normalization cannot be established from the database survey alone. |
| E-10 | Configured Development bank GroupId matches the 452 eligible-view row; other environments remain unverified. |
| E-11 | Not closable from a database query; visual sign-off remains pending. |

No P item has been changed or approved for display. The mapping matrix remains unchanged pending the owner's review.
