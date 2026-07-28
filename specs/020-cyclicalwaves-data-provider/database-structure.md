# CyclicalWaves Data Provider - Database Structure

## Overview

CyclicalWaves ingestion writes raw provider JSON into `FinancialProviderDbContext` and normalized
financial observations into `FinancialIngestionDbContext`.

Current ownership rules:

* NADPCO/Noavaran is the authoritative company catalog source.
* CyclicalWaves does not create or update `Companies` catalog rows.
* The legacy `Symbols` table was removed by the companies-first refactor. CyclicalWaves linkage is
  resolved through existing `Companies` fields such as ticker, company symbol, and ISIN.
* `GET /api/custom-filtering/ticker/{ticker}` returns a combined financial and monthly snapshot.
  Full sync fetches this endpoint once per ticker, persists one raw payload, and routes that same
  payload to both CyclicalWaves normalizers.

## Raw Payload Storage

### `ProviderRawPayloads`

Stored in `FinancialProviderDbContext`.

| Column | Meaning |
|---|---|
| `ProviderName` | `CyclicalWaves` |
| `Dataset` | The dataset used for the original stored payload. For ticker detail this is normally `FinancialStatements`; the same payload can still be routed to monthly normalization. |
| `Endpoint` | `custom-filtering/ticker/{ticker}` for ticker-detail snapshots |
| `ExternalReference` | Persian ticker |
| `Payload` | Full JSON body |
| `Checksum` | SHA-256 hex of `Payload` |
| `ReceivedAt` | UTC receive timestamp |

Deduplication is by `(ProviderName, Checksum)`. The shared ticker-detail strategy means one raw
payload row is expected for one unchanged CyclicalWaves ticker-detail response, not one per logical
dataset.

## Normalized Tables

### `FinancialStatements`

CyclicalWaves writes three income-statement rows per ticker-detail payload:

| Row | Source fields | Period source |
|---|---|---|
| `Q0` | latest quarter sales, profit, margins, `pe`, `ps`, `average_4_quarter_sale` | `last_quarter_date` when parsed |
| `Q1` | penultimate quarter sales, profit, margins | derived from Q0 |
| `Q4` | last-year same-quarter sales, profit, margins | derived from Q0 |

Important columns:

| Column | Expected value |
|---|---|
| `ProviderName` | `CyclicalWaves` |
| `ExternalCompanyId` | The resolved NADPCO-backed company id when linkage succeeds; otherwise the CyclicalWaves `_id` fallback with a warning |
| `ExternalStatementId` | `{_id}:Q0`, `{_id}:Q1`, `{_id}:Q4` |
| `StatementType` | `IncomeStatement` |
| `PeriodType` | `ThreeMonths` |
| `VendorPeriodDate` | Parsed `last_quarter_date` on Q0; null on derived relative rows |
| `SourcePayloadChecksum` | Shared ticker-detail raw-payload checksum |

`last_quarter_date` parsing must support both real provider format `yyyyMMdd` and dashed
`yyyy-MM-dd`. Required examples:

* `"20260320"` -> `2026-03-20`
* `"2026-03-20"` -> `2026-03-20`
* blank, null, or invalid values -> null, with safe fallback to receive-time period resolution

### `FinancialStatementLineItems`

| MetricCode | Rows | Source field |
|---|---|---|
| `REVENUE` | Q0, Q1, Q4 | `last/penultimate/last_year_same_quarter_sale` |
| `NET_PROFIT` | Q0, Q1, Q4 | `*_net_profit` |
| `GROSS_PROFIT` | Q0, Q1, Q4 | `*_gross_profit` |
| `OPERATING_PROFIT` | Q0, Q1, Q4 | `*_operating_profit` |
| `NET_PROFIT_MARGIN` | Q0, Q1, Q4 | `*_net_profit_margin` |
| `GROSS_PROFIT_MARGIN` | Q0, Q1, Q4 | `*_gross_profit_margin` |
| `OPERATING_PROFIT_MARGIN` | Q0, Q1, Q4 | `*_operating_profit_margin` |
| `PE_RATIO` | Q0 only | `pe` |
| `PS_RATIO` | Q0 only | `ps` |
| `AVG_4Q_REVENUE` | Q0 only | `average_4_quarter_sale` |

### `MonthlyReports`

CyclicalWaves writes three monthly rows per ticker-detail payload:

| Row | Source fields | Period source |
|---|---|---|
| `M0` | `last_month_sale`, `average_12_month_sale` | `last_month_sale_date` when parsed |
| `M1` | `penultimate_month_sale` | derived from M0 |
| `M12` | `last_year_same_month_sale`, `last_year_average_12_month_sale` when present | derived from M0 |

Important columns:

| Column | Expected value |
|---|---|
| `ProviderName` | `CyclicalWaves` |
| `ExternalCompanyId` | The resolved NADPCO-backed company id when linkage succeeds; otherwise the CyclicalWaves `_id` fallback with a warning |
| `ExternalReportId` | `{_id}:M0`, `{_id}:M1`, `{_id}:M12` |
| `VendorPeriodDate` | Parsed `last_month_sale_date` on M0; null on derived relative rows |
| `SourcePayloadChecksum` | Shared ticker-detail raw-payload checksum |

`last_month_sale_date` parsing must support both real provider format `yyyyMMdd` and dashed
`yyyy-MM-dd`. Required examples:

* `"20260521"` -> `2026-05-21`
* `"2026-05-21"` -> `2026-05-21`
* blank, null, or invalid values -> null, with safe fallback to receive-time period resolution

### `MonthlyReportLineItems`

| ProductCode | Rows | Source field |
|---|---|---|
| `REVENUE` | M0, M1, M12 | monthly sales fields |
| `AVG_12M` | M0 and M12 when supplied | `average_12_month_sale`, `last_year_average_12_month_sale` |

CyclicalWaves does not supply product-level quantity or rate fields. Production quantity, sales
quantity, and sales rate are not CyclicalWaves-supported source facts.

## DerivedMetrics Provider Isolation

`DerivedMetrics` uses the existing uniqueness key:

```text
ExternalCompanyId + MetricCode + MetricVersion + CalculationPolicyVersion + PeriodEnd
```

This key remains unchanged for this fix. Provider isolation is handled before calculation:

* Recalculation requests originate from a source dataset and source payload checksum.
* The processor resolves the provider that produced the source payload.
* Normalized metric input readers filter source rows by `ProviderName` for provider-specific
  recalculation.
* CyclicalWaves passthrough, average, growth, PE, PS, and margin calculations must read only
  CyclicalWaves-origin normalized rows.
* Noavaran and Codal rows must not be mixed into CyclicalWaves recalculation results.
* Any future cross-provider fallback must be explicit in policy/version naming and covered by
  tests. It must not happen accidentally through unfiltered reads.

Rationale for keeping the key unchanged: current CyclicalWaves policy versions already identify
provider-specific passthrough/source behavior, and the immediate bug is accidental input mixing.
Changing the `DerivedMetrics` key would require a broader migration and reader precedence decision.

## Required Regression Coverage

Tests must cover:

* compact monthly vendor date: `"20260521"`;
* compact quarterly vendor date: `"20260320"`;
* dashed dates still parse;
* invalid/blank/null vendor dates are safe and return null;
* M0/M1/M12 and Q0/Q1/Q4 periods align to parsed vendor dates;
* one fetched ticker-detail payload can populate both `FinancialStatements` and `MonthlyReports`;
* CyclicalWaves recalculation uses only CyclicalWaves-origin normalized rows when other providers
  have rows for the same `ExternalCompanyId`, metric, and period.

## SQL Verification

Monthly vendor dates:

```sql
SELECT "ProviderName", "ExternalCompanyId", "ExternalReportId",
       "PeriodStart", "PeriodEnd", "VendorPeriodDate"
FROM public."MonthlyReports"
WHERE "ProviderName" = 'CyclicalWaves'
ORDER BY "PeriodEnd" DESC;
```

Quarterly vendor dates:

```sql
SELECT "ProviderName", "ExternalCompanyId", "ExternalStatementId",
       "PeriodStart", "PeriodEnd", "VendorPeriodDate"
FROM public."FinancialStatements"
WHERE "ProviderName" = 'CyclicalWaves'
ORDER BY "PeriodEnd" DESC;
```

Provider evidence isolation:

```sql
SELECT "MetricCode", "PeriodEnd", "Value", "CalculationPolicyVersion", "SourceEvidenceJson"
FROM public."DerivedMetrics"
WHERE "ExternalCompanyId" = '3'
  AND "SourceEvidenceJson"::text ILIKE '%CyclicalWaves%'
ORDER BY "PeriodEnd" DESC, "MetricCode";
```
