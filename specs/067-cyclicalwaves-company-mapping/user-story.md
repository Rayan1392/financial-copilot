# Spec 067 — CyclicalWaves Company Mapping

## Context

CyclicalWaves is a financial data provider that delivers quarterly and monthly financial metrics
(sales, profit margins, PE, PS, etc.) per stock symbol. Its records carry only two symbol
identifiers: `ticker` (Persian symbol, e.g. `شغدیر`) and `enticker` (ISIN-like code, e.g.
`IRO1PGDR0001`). Neither field maps directly to the internal `Companies` table, which is backed
by NADPCO as the authoritative company catalog and uses `ExternalCompanyId` (NADPCO `comId`) as
its canonical external reference.

Because no FK from CyclicalWaves financial tables to `Companies` exists, any AI query that
references a symbol must either fall back to a fragile string match or fail silently. Additionally,
price and index data from Tsetmc is stored under `Instruments` (linked via `InsCode`/`IsinCode`),
and the same resolution gap exists there.

This spec establishes a durable, authoritative mapping between CyclicalWaves symbol identifiers
and `Companies` records, and enforces a single resolution path used by all query handlers.

---

## User Stories

### US-001 · Company resolution by any symbol identifier

```
As an AI query handler,
I want to resolve any incoming symbol string (Persian ticker, EnTicker / IsinCode, or InsCode)
to a single Companies record,
so that I always have a canonical CompanyId before querying any financial or price table.
```

**Acceptance criteria:**
- `CompanyResolverService.ResolveBySymbolAsync(string symbol)` returns the matching `Company` or
  `null` — it never throws on a no-match.
- Resolution order: exact `Ticker` → exact `IsinCode` / `EnTicker` → exact `InsCode` →
  normalized fallback (trim + remove invisible Unicode / RTL marks).
- Persian string normalization is applied at every step (trim whitespace, strip zero-width and
  RTL Unicode characters such as U+200C, U+200F, U+202B).
- The service is registered in DI and usable from any application layer.

---

### US-002 · Companies table carries CyclicalWaves symbol columns

```
As a data engineer,
I want the Companies table to store Ticker (Persian) and EnTicker columns,
so that CyclicalWaves records can be matched to a Company without ad-hoc string scanning.
```

**Acceptance criteria:**
- `Companies` has a nullable `Ticker` (nvarchar) column for the Persian symbol.
- `Companies` has a nullable `EnTicker` (varchar) column for the ISIN-like code.
- If `IsinCode` already exists on `Companies` and `EnTicker` is the same semantic value,
  `EnTicker` maps to the same column — no duplication.
- Unique indexes are added on `Companies.Ticker` and `Companies.IsinCode` (partial: non-null
  rows only) to prevent duplicate resolution results.
- An EF Core migration is produced; no raw SQL schema changes.

---

### US-003 · CyclicalWaves financial tables carry a CompanyId FK

```
As a data engineer,
I want all CyclicalWaves financial data tables to reference Companies via a CompanyId FK,
so that joins from any financial query to the company catalog are type-safe and indexed.
```

**Acceptance criteria:**
- Every CyclicalWaves financial data table that currently uses `Ticker` (string) as its only
  company reference gains a nullable `CompanyId` (FK → `Companies.Id`) column.
- An EF Core migration adds the column and FK constraint with `ON DELETE SET NULL` (ingestion
  records must not cascade-delete when a company is removed).
- On new data ingestion, `CompanyId` is resolved and populated before the record is saved;
  unresolved symbols are saved with `CompanyId = null` and logged at `Warning`.

---

### US-004 · Mapping service populates Companies from NADPCO list

```
As a data engineer,
I want a CyclicalWavesCompanyMappingService that reads the NADPCO company list and writes
Ticker / EnTicker onto matching Companies rows,
so that the mapping columns are populated without manual data entry.
```

**Acceptance criteria:**
- `CyclicalWavesCompanyMappingService.SyncMappingAsync(CancellationToken)` iterates the NADPCO
  company list (via existing repository / service).
- For each NADPCO company, attempts to match a CyclicalWaves symbol using:
  - **Primary:** `EnTicker == IsinCode` (case-insensitive, normalized).
  - **Fallback:** `Ticker == Persian symbol` (normalized).
- On a confirmed match, updates `Companies.Ticker` and `Companies.EnTicker` if null or stale.
- Unmatched companies are skipped silently; the service logs a summary count at `Information`.
- The service is idempotent: re-running does not create duplicate rows or overwrite confirmed
  matches with a weaker fallback.

---

### US-005 · Backfill existing CyclicalWaves records with CompanyId

```
As a data engineer,
I want a one-time backfill service that resolves CompanyId for all existing CyclicalWaves
financial records that currently have CompanyId = null,
so that historical data is queryable through the canonical company resolution path.
```

**Acceptance criteria:**
- `BackfillCyclicalWavesCompanyIdService.RunAsync(CancellationToken)` iterates all financial
  records where `CompanyId IS NULL` in batches (default batch size: 500).
- For each record, calls `CompanyResolverService.ResolveBySymbolAsync` using the stored `Ticker`.
- Successfully resolved records are updated with the `CompanyId` and saved.
- Unresolved tickers are written to a structured log entry at `Warning` with `Ticker` value for
  manual review.
- The service exposes a trigger via admin endpoint `POST /api/v1/admin/cyclicalwaves/backfill-company-id`
  (role: `DataAdmin`); the response returns `{ resolved: N, unresolved: M }`.
- The service is safe to run multiple times: already-resolved rows are skipped.

---

### US-006 · Unified query pattern enforced across all handlers

```
As an AI query handler,
I want a documented and enforced resolution chain (symbol → Company → ExternalCompanyId →
financial tables; symbol → Instrument → price/index tables),
so that no handler duplicates ad-hoc symbol resolution logic.
```

**Acceptance criteria:**
- All query handlers (Scanner tool adapters, AI tool adapters) that need financial data call
  `CompanyResolverService.ResolveBySymbolAsync` as their first step.
- Handlers never perform direct `WHERE Ticker = @symbol` queries against financial tables.
- An architecture test asserts that no class outside `CompanyResolverService` references
  `Companies.Ticker` in a LINQ `Where` clause.
- For price / index lookups, handlers resolve via `Instruments` (matching on `InsCode` or
  `IsinCode` sourced from the `Company` record) — not via raw ticker string on price tables.

---

## Out of Scope

- Changing the NADPCO sync pipeline or the `ExternalCompanyId` ingestion path.
- Modifying Tsetmc ingestion (the `InsCode`/`IsinCode` linkage from Tsetmc → Instruments →
  Companies is already established).
- Exposing the mapping data through any public API endpoint.
- Automatic company creation from CyclicalWaves tickers (CyclicalWaves is not a company catalog
  source per spec `020` change-request order 48).

---

## Dependencies

| Spec | Reason |
|---|---|
| `001` project-foundation | DI, EF Core, conventions |
| `002` auth-and-tenancy | `DataAdmin` role gate on backfill endpoint |
| `003` financial-domain-model | `Companies`, `Instruments` entity definitions |
| `020` cyclicalwaves-data-provider | existing CyclicalWaves financial tables being amended |
| `051` noavaran-archive-and-current-api-strategy | `ExternalCompanyId`, `ProviderSources`, `NoavaranCurrentApi` conventions |
