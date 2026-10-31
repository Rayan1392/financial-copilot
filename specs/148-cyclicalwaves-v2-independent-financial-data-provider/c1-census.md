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

Provenance is fully determinable (every operand's provider is recorded; nothing is inferred from values or dates), so
the C1 filter was implemented under the Product Owner's 2026-10-31 instruction ("stop only if provenance cannot be
reliably determined"). The open question is classification, not provenance:

- **Default (design §10.6, "any evidence item from CyclicalWaves"):** mixed rows are excluded —
  `FinancialData:LegacyCyclicalWavesMixedProvenance = Exclude`.
- **Alternative:** `Include` keeps the mixed growth rows and excludes only rows whose evidence is exclusively CyclicalWaves.

**REQUIRES_PRODUCT_OWNER_DECISION (C1-M):** confirm `Exclude` (current default) or switch to `Include`. The switch is
configuration only; no code or data change is needed either way.
