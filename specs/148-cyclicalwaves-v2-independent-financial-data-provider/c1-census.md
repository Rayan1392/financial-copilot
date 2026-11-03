# Feature 148 — C1 provenance census (T148-06.6)

Read-only (`default_transaction_read_only=on`) queries on the local **Development** database `financial_copilot`,
2026-10-31. SQL: `c1_census.sql` / `c1_mixed.sql` (kept in the session scratchpad, reproduced in the report appendix).

## Results

| Measure | Rows |
|---|---|
| `DerivedMetrics` total | 181 466 |
| (a) structured `sourceProvider = "CyclicalWaves"` in source or dependency evidence | **37 099** |
| (b) current substring marker (`SourceEvidenceJson` / `DependencyEvidenceJson` / `CalculationPolicyVersion`) | **37 099** |
| (c) mixed providers in one row (CyclicalWaves + NoavaranCurrentApi) | **4 130** |
| (d) marker only in `CalculationPolicyVersion` | **0** |
| marker in evidence text but not in the structured field | 0 |
| rows with no provider evidence at all | 68 803 |

JSON property casing actually used: **camelCase** (`"sourceProvider"`) in 112 663 rows, PascalCase in 0.

Provider combinations: NoavaranCurrentApi 75 564 · none 68 803 · CyclicalWaves only 32 969 · CyclicalWaves + NoavaranCurrentApi 4 130.

### (c) What the mixed rows are

All 4 130 are **growth metrics** whose two operands come from different providers (both recorded in `SourceEvidenceJson`):
`MONTHLY_SALES_GROWTH_MOM` 808 · `MONTHLY_SALES_GROWTH_YOY` 782 (periods 2026-02-28 … 2026-05-31) ·
`OPERATING/NET/GROSS_PROFIT_GROWTH_QOQ/YOY` and `REVENUE_GROWTH_QOQ/YOY` 313–321 each (period 2026-03-20).

### (e) Affected latest values per metric (companies whose latest row is legacy / companies left with no valid row)

| Metric | legacy rows | latest changes | becomes missing |
|---|---|---|---|
| MONTHLY_PRODUCTION_QUANTITY, MONTHLY_SALES, MONTHLY_SALES_RATE, MONTHLY_SALES_QUANTITY | 2 868 each | 195 | 181 |
| GROSS/OPERATING/NET_PROFIT_MARGIN | 2 338 | 607 | 607 / 607 / 604 |
| NET_PROFIT | 2 306 | 228 | 33 |
| AVG_12M_MONTHLY_SALES | 2 259 | 195 | 0 |
| MONTHLY_SALES_GROWTH_YOY / _MOM | 1 729 / 1 236 | 194 / 186 | 181 / 181 |
| REVENUE / GROSS_PROFIT / OPERATING_PROFIT | 1 444 / 1 439 / 1 388 | 233 / 232 / 213 | 36 / 36 / 14 |
| AVG_4Q_REVENUE | 852 | 607 | 607 |
| PE_TTM / PS_TTM | 634 each | 602 | 602 |
| *_GROWTH_YOY (4 codes) | 526–529 | 496–500 | 491–495 |
| *_GROWTH_QOQ (4 codes), EBIT | 516–519 | 145–166 | 140–161 |

## Gate outcome

- (a) = (b): **met** — the structured discriminator classifies exactly the rows the substring marker does.
- (d) = 0: **met**.
- (c) = 0: **not met** — 4 130 mixed rows.

Provenance is fully determinable for these rows (every operand's provider is recorded; nothing is inferred from values or
dates).

## C1-M — final rule (Product Owner decision, 2026-10-31)

**Mixed-provenance rows are permanently excluded.** The former `FinancialData:LegacyCyclicalWavesMixedProvenance` option
(`Exclude` | `Include`) and the `FinancialData:ExcludeLegacyCyclicalWavesDerivedMetrics` switch were removed; no
configuration can include legacy or mixed rows again. No row was modified or deleted.

### Follow-up census: rows that record no provider (read-only, 2026-10-31)

The 68 803 rows without any `sourceProvider` were broken down by calculation policy:

| `CalculationPolicyVersion` | Rows | Evidence | Classification |
|---|---|---|---|
| `nadpco-api-fundamental-index-source-v1` | 57 176 | `[{"source":"NoavaranCurrentApi", …}]` (provider in a different property) | NotLegacy (NoavaranAmin) |
| `codal-ratio-source-v1` | 3 740 | source evidence present, dependency empty | NotLegacy (CodalDB) |
| `cw-avg-12m-monthly-sales-passthrough-v1` | **7 887** | both arrays empty | **Legacy**: this policy reads only the `AVG_12M` line item, which only `CyclicalWavesMonthlyReportNormalizer` writes; every attributed row of the policy (2 259) is CyclicalWaves |

The substring census (b) missed these 7 887 rows because the policy name is `cw-…`, not `CyclicalWaves`. They are now
classified by the registered policy (`LegacyCalculationPolicy`). The policy name alone is **not** used where it is shared:
`cw-net-profit-passthrough-v1` and other `cw-*` statement policies also carry NoavaranCurrentApi rows, so a recorded
provider always wins. Evidence that is not a JSON array: 0 rows (such rows would be excluded as undetermined).

### Final classification (read-only, 2026-10-31)

| Class | Rows | Companies | Read by NoavaranAmin Chat |
|---|---|---|---|
| ExclusivelyLegacy | 32 969 | 607 | never |
| MixedWithLegacy | 4 130 | 517 | never |
| LegacyCalculationPolicy | 7 887 | 625 | never |
| NotLegacy | 136 480 | 1 193 | yes |

Effect: `AVG_12M_MONTHLY_SALES` has no valid row left in `DerivedMetrics` (10 146 rows, 625 companies, all legacy).
NoavaranAmin answers for that metric come only from paths that do not read these rows; otherwise they are Missing.
