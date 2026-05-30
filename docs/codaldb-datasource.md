# CodalDB Datasource — Reference & Integration Mapping

> **Status:** Reference document. No code, EF entities, or migrations are implied by this file.
> It describes an **external, read-only** MS SQL Server database and recommends how it could
> map into the Financial-Copilot domain (`003`) and provider abstraction (`004`).

## 1. Overview

**CodalDB** is an MS SQL Server database holding Iranian **Codal** (codal.ir) corporate
financial-disclosure data for the Tehran Stock Exchange / Iran Fara Bourse markets. It is a
**ready-populated, queryable SQL source** — not an HTTP API — and is distinct from the
project's own ingestion database. The project would consume it read-only.

```
Server   = localhost
Database = CodalDB
User Id  = sa
Password = (see secrets — never hardcode; read from configuration per 004 AC)
```

### Tables in scope (the 10 requested)

| Table | Rows | Role |
|---|---:|---|
| `Companies` | 2,362 | Company/issuer master (dimension): symbols, industry, ISIN, market |
| `Statements` | 51,081 | Financial-statement headers (period, fiscal year, audit/consolidation flags) |
| `BalanceSheetItems` | 220 | Balance-sheet line-item **catalog** (FA + EN titles) |
| `BalanceSheetItemAmounts` | 2,475,527 | Balance-sheet amounts (fact): statement × item |
| `IncomeItems` | 248 | Income-statement line-item **catalog** (FA + EN titles) |
| `IncomeItemAmounts` | 1,910,232 | Income-statement amounts (fact): statement × item |
| `FinancialRatioItems` | 427 | Ratio **catalog**, categorized (Current ratio, Altman Z, Acid ratio, …) |
| `FinancialRatios` | 5,151,000 | **Precomputed** ratio values (fact): company × period × item |
| `MonthlyActivity` | 3,903 | Monthly production/sales report headers |
| `MonthlyActivityAmounts` | 6,342 | Per-product produce/sale amount, rate, value (fact) |

### Headline opportunities

1. **Precomputed financial ratios** — 5.1M values across 427 named definitions, already
   calculated. Useful as a seed/validation source for the derived-metrics engine (`006`).
2. **English line-item titles** — `IncomeItems.ItemTitleEn` / `BalanceSheetItems.ItemTitleEn`
   give a ready semantic anchor (e.g. *Revenue*, *Cost of sales*) for mapping to `MetricCode`.
3. **Monthly production/sales** — maps directly onto the project's `MonthlyReport` concept.
4. **Broad coverage** — 2,362 issuers and ~51K statements spanning many fiscal periods.

## 2. Conventions used throughout CodalDB

- **Dual calendar.** Every dated row carries both a **Gregorian** column (`FiscalYearEnd`,
  `PeriodEnd`, `AnnouncementDate`) and a **Jalali** string column (`…Jalali`, e.g.
  `1402/09/30`). The Jalali↔Gregorian resolution built for the CyclicalWaves provider
  (`specs/020-cyclicalwaves-data-provider`) should be reused; do not re-derive it.
- **`PeriodType` = cumulative period length in months.** Values **3 / 6 / 9 / 12** dominate
  (quarterly-cumulative statements). Values 1,2,4,5,7,8,10,11 are interim/irregular periods
  (a few hundred rows each). See the translation table in §5.
- **Amount scale is NOT recorded per row.** The `Unit` column on the amount tables is `'N/A'`
  for ~99.9% of rows. **Amounts must be interpreted by Codal convention (million Rials) or by
  explicit configuration — never read scale from `Unit`.** This is a normalization caveat.
- **Statement qualifier flags** on `Statements` and `FinancialRatios`:
  - `IsAudited` — audited vs. unaudited.
  - `IsComposing` — **consolidated** (تلفیقی) vs. parent-only.
  - `IsRepresented` — restated / representment of a prior period.
  The same `(Company, Period)` can appear in multiple variants — a **canonical-selection
  policy** is required (see §5).
- **Star-schema shape.** Each fact area is a small **catalog** table (slowly-changing item
  definitions) + a large **amounts** table joined by `ItemId`.

## 3. Table reference

### 3.1 `Companies` — issuer master (dimension)

Master record for each company/issuer. PK `CoID` is the internal Codal company id referenced
by every fact table.

| Column | Type | Null | Notes |
|---|---|---|---|
| `CoID` | int | NO | **PK**. Internal Codal company id. |
| `CoName` | nvarchar(450) | NO | Company name (Persian). |
| `CoNameEnglish` | nvarchar(450) | YES | Company name (English). |
| `CompanySymbol` | nvarchar(50) | YES | Trading symbol (2,184 distinct of 2,362). |
| `CoTSESymbol` | nvarchar(50) | YES | TSE symbol (2,304 distinct — best-populated identifier). |
| `GroupID` / `GroupName` | int / nvarchar(450) | YES | Super-sector grouping. |
| `IndustryID` / `IndustryName` | int / nvarchar(450) | YES | Industry classification. |
| `InstCode` | nvarchar(20) | YES | **TSETMC instrument code** (859 rows, all distinct). |
| `TseCIsinCode` | nvarchar(20) | YES | Company ISIN (e.g. `IRO1MAGS0006`; 850 distinct). |
| `TseSIsinCode` | nvarchar(20) | YES | Symbol/share ISIN. |
| `MarketID` / `MarketName` | int / nvarchar(10) | YES | Market (e.g. bourse / fara-bourse). |
| `InstrumentRef` | uniqueidentifier | YES | ⚠️ **Constant placeholder** — all 2,297 non-null rows hold the **same** GUID (`9455D05D-…`). **NOT a usable join key.** |
| `InsertDateTime` / `ModifiedDateTime` | datetime | NO / YES | Row audit timestamps. |

**Relationships:** referenced by `Statements`, `FinancialRatios`, `MonthlyActivity`,
(`Meeting`) via their `CompanyId → Companies.CoID`.

> **Linkage warning:** Despite its name, `InstrumentRef` cannot link a CodalDB company to the
> project's instrument identity — it is a single constant value. Use `InstCode`, ISIN, or
> `CoTSESymbol` instead (see §5 linkage strategy).

### 3.2 `Statements` — financial-statement header

One row per published financial statement (a specific company, period, and audit/consolidation
variant). PK `Id` is the join target for the income- and balance-sheet amount tables.

| Column | Type | Null | Notes |
|---|---|---|---|
| `Id` | bigint | NO | **PK**. Joined by `*ItemAmounts.StatementId`. |
| `StmtId` | bigint | NO | Source Codal statement id. |
| `CompanyId` | int | NO | **FK → `Companies.CoID`**. |
| `PeriodType` | tinyint | NO | Cumulative months (3/6/9/12 = quarterly-cumulative). See §5. |
| `FiscalYearEnd` | smalldatetime | NO | Fiscal year-end (Gregorian). |
| `FiscalYearEndJalali` | nvarchar(10) | YES | Fiscal year-end (Jalali). |
| `PeriodEnd` | smalldatetime | NO | Period end (Gregorian). |
| `PeriodEndJalali` | nvarchar(10) | YES | Period end (Jalali). |
| `AnnouncementDate` | smalldatetime | NO | Disclosure date (Gregorian). |
| `AnnouncementDateJalali` | nvarchar(10) | YES | Disclosure date (Jalali). |
| `IsAudited` | bit | YES | Audited flag. |
| `IsRepresented` | bit | YES | Restated/representment flag. |
| `IsComposing` | bit | YES | Consolidated (تلفیقی) flag. |
| `InsertDateTime` / `ModifiedDateTime` | smalldatetime | NO / YES | Row audit timestamps. |
| `TempId` | bigint | YES | Staging/import artifact. |
| `isDeleted` | bit | YES | Soft-delete flag — **filter `isDeleted = 0/NULL`** when reading. |

**Relationships:** `CompanyId → Companies.CoID`; referenced by `IncomeItemAmounts.StatementId`
and `BalanceSheetItemAmounts.StatementId` (and `CashFlowItemAmounts`, out of scope).

### 3.3 `BalanceSheetItems` — balance-sheet line-item catalog

| Column | Type | Null | Notes |
|---|---|---|---|
| `ItemId` | int | NO | **PK**. Line-item code. |
| `ItemTitle` | nvarchar(100) | NO | Title (Persian). |
| `ItemTitleEn` | nvarchar(100) | YES | Title (English) — e.g. *Cash*, *Trade receivables*, *Inventories*, *Tangible fixed assets*. |

### 3.4 `BalanceSheetItemAmounts` — balance-sheet amounts (fact)

| Column | Type | Null | Notes |
|---|---|---|---|
| `Id` | bigint | NO | **PK**. |
| `StatementId` | bigint | NO | **FK → `Statements.Id`**. |
| `ItemId` | int | NO | **FK → `BalanceSheetItems.ItemId`**. |
| `Amount` | decimal | NO | Reported amount (scale by convention — see §2). |
| `Unit` | nvarchar(50) | NO | Effectively `'N/A'`; do not rely on it. |
| `InsertDateTime` / `ModifiedDateTime` | smalldatetime | NO / YES | Audit timestamps. |

**Canonical join:**
```sql
SELECT s.CompanyId, s.PeriodEndJalali, s.PeriodType, bi.ItemTitleEn, ba.Amount
FROM Statements s
JOIN BalanceSheetItemAmounts ba ON ba.StatementId = s.Id
JOIN BalanceSheetItems       bi ON bi.ItemId      = ba.ItemId;
```

### 3.5 `IncomeItems` — income-statement line-item catalog

| Column | Type | Null | Notes |
|---|---|---|---|
| `ItemId` | int | NO | **PK**. Line-item code. |
| `ItemTitle` | nvarchar(100) | NO | Title (Persian). |
| `ItemTitleEn` | nvarchar(100) | YES | Title (English) — e.g. *Revenue* (`ItemId 15`), *Cost of sales* (`1`), *Finance costs* (`12`), *Income taxes payments* (`13`). |

### 3.6 `IncomeItemAmounts` — income-statement amounts (fact)

| Column | Type | Null | Notes |
|---|---|---|---|
| `Id` | bigint | NO | **PK**. |
| `StatementId` | bigint | NO | **FK → `Statements.Id`**. |
| `ItemId` | int | NO | **FK → `IncomeItems.ItemId`**. |
| `Amount` | decimal | NO | Reported amount (scale by convention — see §2). May be `0` when not disclosed. |
| `Unit` | nvarchar(50) | NO | Effectively `'N/A'`. |
| `InsertDateTime` / `ModifiedDateTime` | smalldatetime | NO / YES | Audit timestamps. |

**Canonical join (e.g. Revenue):**
```sql
SELECT s.CompanyId, s.PeriodEndJalali, s.PeriodType, ia.Amount
FROM Statements s
JOIN IncomeItemAmounts ia ON ia.StatementId = s.Id
JOIN IncomeItems       ii ON ii.ItemId      = ia.ItemId
WHERE ii.ItemTitleEn = 'Revenue';
```

### 3.7 `FinancialRatioItems` — ratio catalog

Categorized definitions of each ratio. Categories include liquidity, solvency, bankruptcy
models (Altman Z, Springate, Zavgin, Fulmer), and banking-specific deposit/facility ratios.

| Column | Type | Null | Notes |
|---|---|---|---|
| `Id` | int | NO | **PK**. Ratio definition id. |
| `Title` | nvarchar(250) | NO | Title (Persian). |
| `TitleEn` | varchar(250) | YES | Title (English) — e.g. *Current ratio*, *Acid Ratio*, *Cash flow from operations*, *Altman Z (p)*. |
| `CategoryId` | int | NO | Category code. |
| `CategoryTitle` | nvarchar(250) | NO | Category label (Persian). |

### 3.8 `FinancialRatios` — precomputed ratio values (fact)

The largest table (5.1M rows). One value per company × period × ratio definition. Note this
table keys on **`CompanyId` + period columns directly** (it does **not** reference
`Statements.Id`), and carries its own period/qualifier columns.

| Column | Type | Null | Notes |
|---|---|---|---|
| `Id` | bigint | NO | **PK**. |
| `CompanyId` | int | NO | **FK → `Companies.CoID`**. |
| `FiscalYearEnd` / `JalaliFiscalYearEnd` | date / varchar(10) | NO | Fiscal year end (Gregorian + Jalali). |
| `PeriodEnd` / `JalaliPeriodEnd` | date / varchar(10) | NO | Period end (Gregorian + Jalali). |
| `PeriodType` | int | NO | Cumulative months (see §5). |
| `AnouncementDate` | varchar(20) | YES | Disclosure date *(note source spelling "Anouncement")*. |
| `IsAudited` / `IsRepresented` / `IsComposing` | bit | NO | Same qualifier semantics as `Statements`. |
| `ItemID` | int | NO | **FK → `FinancialRatioItems.Id`**. |
| `ItemValue` | float | NO | Precomputed ratio value. |
| `InsertDateTime` / `ModifiedDateTime` | smalldatetime | NO / YES | Audit timestamps. |

**Canonical join:**
```sql
SELECT fr.CompanyId, fr.JalaliPeriodEnd, fr.PeriodType, fri.TitleEn, fr.ItemValue
FROM FinancialRatios     fr
JOIN FinancialRatioItems fri ON fri.Id = fr.ItemID;
```

### 3.9 `MonthlyActivity` — monthly production/sales header

One row per company per reported activity month.

| Column | Type | Null | Notes |
|---|---|---|---|
| `Id` | bigint | NO | **PK**. |
| `CompanyId` | int | NO | **FK → `Companies.CoID`**. |
| `Month` | tinyint | NO | Month (1–12, Jalali calendar). |
| `Year` | int | NO | Year (Jalali). |
| `FiscalYearEnd` | nvarchar(10) | NO | Fiscal year-end (Jalali string). |
| `InsertDateTime` / `ModifiedDateTime` | smalldatetime | NO / YES | Audit timestamps. |

### 3.10 `MonthlyActivityAmounts` — per-product monthly amounts (fact)

| Column | Type | Null | Notes |
|---|---|---|---|
| `Id` | bigint | NO | **PK**. |
| `MonthlyActivityId` | bigint | NO | **FK → `MonthlyActivity.Id`**. |
| `ProductId` | int | NO | Product code. |
| `ProductTitle` | nvarchar(250) | NO | Product name. |
| `ProductProduceAmount` | bigint | NO | Quantity produced. |
| `ProductSaleAmount` | bigint | NO | Quantity sold. |
| `ProductSaleRate` | decimal | NO | Unit sale price/rate. |
| `ProductSaleValue` | bigint | NO | Sale value (amount × rate). |
| `ProductUnit` | nvarchar(50) | YES | Unit of measure for the product (may be null). |
| `InsertDateTime` / `ModifiedDateTime` | smalldatetime | NO / YES | Audit timestamps. |

## 4. Entity-relationship diagram (in-scope tables)

```
                         ┌─────────────────┐
                         │    Companies    │  PK CoID
                         └───────┬─────────┘
        ┌────────────────────────┼───────────────────────────┐
        │ CompanyId              │ CompanyId                  │ CompanyId
        ▼                        ▼                            ▼
┌───────────────┐      ┌──────────────────┐         ┌──────────────────┐
│  Statements   │PK Id │ FinancialRatios  │         │ MonthlyActivity  │ PK Id
└──────┬────────┘      │ (keyed by Company│         └────────┬─────────┘
       │ StatementId   │  + period cols)  │                  │ MonthlyActivityId
   ┌───┴────────────┐  └────────┬─────────┘                  ▼
   ▼                ▼           │ ItemID          ┌──────────────────────────┐
┌──────────────┐ ┌────────────┐│                 │ MonthlyActivityAmounts   │
│IncomeItem    │ │BalanceSheet││                 └──────────────────────────┘
│Amounts       │ │ItemAmounts ││
└──────┬───────┘ └─────┬──────┘▼
       │ItemId         │ItemId ┌─────────────────────┐
       ▼               ▼       │ FinancialRatioItems │ PK Id
┌────────────┐ ┌──────────────┐└─────────────────────┘
│ IncomeItems│ │BalanceSheet  │
└────────────┘ │Items         │
               └──────────────┘
```

## 5. Integration mapping to Financial-Copilot

### 5.1 Provider abstraction (`004`)

CodalDB is a **queryable SQL source**, not an HTTP API, so it does not fit the
`ConfiguredFinancialDataProviderClient` (typed `HttpClient`) shape. The Clean-Architecture-
consistent approach is a new **outer-layer SQL gateway adapter** — e.g.
`CodalDbFinancialDataGateway` — that implements the **same Application-layer provider
interfaces** the existing `MockFinancialDataProvider` implements (symbol retrieval,
financial-statement retrieval, monthly production/sales retrieval, health check). Application
and Scanner use cases stay unaware of SQL/Codal specifics.

Contrast with CyclicalWaves: there is no HTTP body to store in `ProviderRawPayloads`. If raw-
payload auditability (`005`) is still wanted, the "raw payload" equivalent is the **source row
set + a SHA-256 checksum** of it, persisted for idempotency/dedup the same way.

> **Decision point (left to the architect):** read CodalDB live through the gateway on demand,
> **or** run a normalizer that ingests it into the project's own DB (mirroring the CyclicalWaves
> normalizer pipeline in `005`). This document does not choose; it presents both.

### 5.2 Domain-model mapping (`003`)

Domain types confirmed in `specs/003-financial-domain-model`:

| CodalDB | Domain concept (`003`) |
|---|---|
| `Companies` | `Company` / `Symbol` / `Industry` |
| `Statements` | `FinancialStatement` (header) |
| `IncomeItemAmounts` + `IncomeItems` | `FinancialStatementLineItem` (income) |
| `BalanceSheetItemAmounts` + `BalanceSheetItems` | `FinancialStatementLineItem` (balance) |
| `MonthlyActivity` + `MonthlyActivityAmounts` | `MonthlyReport` / `MonthlyReportLineItem` |
| `FinancialRatios` + `FinancialRatioItems` | candidate **source** for `DerivedMetric` (see §5.5) |

### 5.3 `PeriodType` → domain period type

The project's `FiscalPeriod` value object supports monthly, 3/6/9/12-month, latest month,
latest quarter, and TTM.

| CodalDB `PeriodType` | Meaning | Domain period type |
|---:|---|---|
| 3 | 3-month cumulative | 3-month |
| 6 | 6-month cumulative | 6-month |
| 9 | 9-month cumulative | 9-month |
| 12 | 12-month (full year) | 12-month |
| 1,2,4,5,7,8,10,11 | interim / irregular cumulative months | map to nearest, or carry a stale/interim **warning** and exclude from period comparisons |

### 5.4 Metric vocabulary (`003` / `015`)

`ItemTitleEn` (income/balance) and `TitleEn` (ratios) are the available **semantic anchors**.
They must be mapped to the project's extensible `MetricCode` registry via an explicit
**title → MetricCode mapping table** (do not hardcode a switch). Persian `ItemTitle`/`Title`
render as `?` in a non-UTF console but are intact in the DB — the mapping should key on
`ItemId`/ratio `Id` (stable) with `…En` as the human label.

### 5.5 Precomputed ratios — decision point

`FinancialRatios` provides 5.1M ready values across 427 definitions. Per Clean Architecture,
the `006` derived-metrics engine must **own calculation policy and versioning** — so treat
CodalDB ratios as an **alternative / cross-validation source**, not ground truth:
- **Seed/backfill** the engine where a definition matches a project `MetricCode`.
- **Cross-check** engine output against CodalDB to flag discrepancies.
- Record provenance (source = CodalDB) and the `CalculationPolicyVersion` so explanations
  (`009`) can cite which value came from where.

### 5.6 Normalization caveats (Release-It! / Pragmatic)

- **Amount scale.** `Unit = 'N/A'` → assume million Rials (Codal convention) **via
  configuration**, and surface the assumption; never read scale from the row.
- **Canonical-variant selection.** For each `(Company, PeriodEnd, PeriodType)`, multiple rows
  exist across `IsAudited` / `IsComposing` / `IsRepresented`. Define an explicit selection
  policy (e.g. prefer audited > unaudited, latest representment, consolidated-or-parent by
  configuration) and carry the chosen flags as evidence. Also filter `Statements.isDeleted`.
- **Calendar.** Reuse the existing Jalali↔Gregorian fiscal-period resolution; both calendars
  are present, so no parsing of Persian dates is required when the Gregorian column exists.
- **Company linkage (replaces the unusable `InstrumentRef`).** Recommended priority:
  1. `InstCode` (TSETMC instrument code) — stable, but only ~859 companies populated.
  2. `TseCIsinCode` / `TseSIsinCode` (ISIN) — globally stable when present (~850 distinct).
  3. `CoTSESymbol` — best coverage (2,304 distinct of 2,362).
  4. `CompanySymbol` — fallback.
  Symbols are reused across delisted/renamed issuers, so prefer `InstCode`/ISIN where present.

## 6. Out of scope but available in CodalDB

The database also contains adjacent high-value tables **not** covered here, available if
promoted into scope later: `CashFlowItems` / `CashFlowItemAmounts` (cash-flow statements,
~1.17M rows), `Capitals` / `CapitalIncreaseLicense` / `RegisterCapitalIncrease`
(capital-increase history), `MarketRatio` / `MarketRatioItem` (market ratios linked to
`InstrumentRefined`), `Meeting` / `MeetingItems` / `MeetingItemAmounts` (shareholder
meetings), `CodalStatements` / `CodalPublishers` (raw Codal letters/publishers),
`InstrumentRefined` (929 instruments), `EslamiPortfolio`, `LetterTypes`, `Token`, and
`Calendar` (Jalali/Gregorian calendar, ~91K rows).

## 7. Appendix — verification queries (read-only)

```sql
-- Table inventory + row counts
SELECT t.name, p.rows FROM sys.tables t
JOIN sys.partitions p ON t.object_id=p.object_id AND p.index_id IN (0,1) ORDER BY t.name;

-- Columns for the 10 tables
SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, CHARACTER_MAXIMUM_LENGTH, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME IN ('Companies','Statements','BalanceSheetItems','BalanceSheetItemAmounts',
  'IncomeItems','IncomeItemAmounts','FinancialRatioItems','FinancialRatios',
  'MonthlyActivity','MonthlyActivityAmounts')
ORDER BY TABLE_NAME, ORDINAL_POSITION;

-- PeriodType distribution (Statements)
SELECT PeriodType, COUNT(*) FROM Statements GROUP BY PeriodType ORDER BY PeriodType;

-- InstrumentRef is a constant placeholder (1 distinct value over 2,297 rows)
SELECT COUNT(*) AS NonNull, COUNT(DISTINCT InstrumentRef) AS Distinct_
FROM Companies WHERE InstrumentRef IS NOT NULL;

-- Linkage-column coverage
SELECT COUNT(InstCode) AS InstCode, COUNT(DISTINCT InstCode) AS InstCodeDistinct,
       COUNT(CoTSESymbol) AS TSESym, COUNT(DISTINCT CoTSESymbol) AS TSESymDistinct,
       COUNT(DISTINCT TseCIsinCode) AS IsinDistinct FROM Companies;

-- Unit is effectively 'N/A'
SELECT Unit, COUNT(*) FROM IncomeItemAmounts GROUP BY Unit;
```

> All facts in this document were verified against the live `CodalDB` instance on
> `localhost` using the queries above (read-only). Row counts are approximate
> (`sys.partitions`) and current as of the review date.
