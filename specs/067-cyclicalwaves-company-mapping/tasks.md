# Tasks — Spec 067 · CyclicalWaves Company Mapping

## Phase 1 — Schema & Migration

### TASK-001 · Add `Ticker` and `EnTicker` columns to `Companies`

- [ ] Inspect the existing `Companies` entity class:
  - If `IsinCode` already exists and is semantically equivalent to `EnTicker`, map `EnTicker` to
    that column (add a property alias or rename) — do **not** add a duplicate column.
  - If neither exists, add `Ticker` (nvarchar, nullable) and `EnTicker` (varchar, nullable).
- [ ] Add `Ticker` (nvarchar, nullable) if not present.
- [ ] Add partial unique index on `Companies.Ticker` (WHERE `Ticker IS NOT NULL`).
- [ ] Add partial unique index on `Companies.IsinCode` / `EnTicker` (WHERE column IS NOT NULL).
- [ ] Update EF Core `IEntityTypeConfiguration<Company>` with column lengths and index definitions.
- [ ] Generate EF Core migration `AddTickerEnTickerToCompanies`.
- [ ] Verify migration up/down runs cleanly against a local dev database.
- **Related US:** US-002

---

### TASK-002 · Add `CompanyId` FK to CyclicalWaves financial tables

- [ ] Identify all tables that store CyclicalWaves financial data (e.g. `FinancialStatements`,
  `MonthlyReports`, or any table with `ProviderName = "CyclicalWavesApi"`).
- [ ] For each such table: add nullable `CompanyId` (FK → `Companies.Id`, `ON DELETE SET NULL`).
- [ ] Update EF Core entity classes and `IEntityTypeConfiguration` for each table.
- [ ] Generate EF Core migration `AddCompanyIdToCyclicalWavesFinancialTables`.
- [ ] Add non-unique index on the new `CompanyId` column for query performance.
- [ ] Verify migration up/down runs cleanly.
- **Related US:** US-003

---

## Phase 2 — Persian String Normalization

### TASK-003 · `PersianSymbolNormalizer` utility

- [ ] Create `PersianSymbolNormalizer` static class in the `Domain` or `SharedKernel` project.
- [ ] Implement `Normalize(string? input) → string`:
  - Trim leading/trailing whitespace.
  - Remove Unicode invisible/directional characters: U+200C (ZWNJ), U+200D (ZWJ), U+200F (RLM),
    U+202B (RLE), U+FEFF (BOM), U+00AD (soft hyphen).
  - Normalize Arabic Ye (ي U+064A) → Persian Ye (ی U+06CC).
  - Normalize Arabic Kaf (ك U+0643) → Persian Kaf (ک U+06A9).
  - Return empty string for null / whitespace-only input.
- [ ] Unit tests (see TASK-011).
- **Related US:** US-001, US-004

---

## Phase 3 — Company Resolution Service

### TASK-004 · `ICompanyResolverService` interface and implementation

- [ ] Define interface in Application layer:
  ```csharp
  public interface ICompanyResolverService
  {
      Task<Company?> ResolveBySymbolAsync(string symbol, CancellationToken ct = default);
  }
  ```
- [ ] Implement `CompanyResolverService` in Infrastructure layer.
- [ ] Resolution order (each step calls `PersianSymbolNormalizer.Normalize` before comparison):
  1. Exact match on `Companies.Ticker` (case-sensitive after normalize).
  2. Exact match on `Companies.IsinCode` / `EnTicker` (case-insensitive).
  3. Exact match on `Companies.InsCode` (if column exists on `Companies` or via `Instruments`).
  4. Normalized fallback: repeat steps 1–3 with `normalized` value if raw input differs.
- [ ] Return `null` (never throw) when no match is found.
- [ ] Register `ICompanyResolverService → CompanyResolverService` as `Scoped` in DI.
- [ ] Log unresolved symbols at `Debug` level with the raw input value.
- **Related US:** US-001

---

## Phase 4 — Mapping Service (NADPCO → Companies)

### TASK-005 · `CyclicalWavesCompanyMappingService`

- [ ] Create `CyclicalWavesCompanyMappingService` in Infrastructure layer.
- [ ] Inject: existing NADPCO company repository/service, `ICompaniesRepository` (write), `ILogger`.
- [ ] `SyncMappingAsync(CancellationToken)` algorithm:
  1. Fetch the full NADPCO company list (eligible companies only: equities, `PrecedencyRight = 0`,
     markets بورس / فرابورس / پایه — reuse `NoavaranEligibleCompanies` view / existing filter).
  2. For each NADPCO company record:
     - **Primary match:** look up `Companies` WHERE `IsinCode = nadpco.IsinCode` (normalized).
     - **Fallback match:** look up `Companies` WHERE `Ticker = nadpco.Ticker` (normalized).
  3. On confirmed match: if `Companies.Ticker` is null or `Companies.EnTicker` is null, update
     and save. Do **not** overwrite a confirmed non-null value with a weaker fallback match.
  4. On no match: skip, increment unmatched counter.
- [ ] At end of run: log `Information` with `{ matched: N, updated: M, skipped: K, unmatched: U }`.
- [ ] Service is idempotent: re-running produces the same result.
- [ ] Register as `Scoped` in DI.
- **Related US:** US-004

---

## Phase 5 — Ingestion Wiring

### TASK-006 · Resolve `CompanyId` during CyclicalWaves data ingestion

- [ ] Locate the existing CyclicalWaves ingestion normalizer / upsert handler (from spec `020`).
- [ ] Inject `ICompanyResolverService` into the normalizer/handler.
- [ ] Before saving each record: call `ResolveBySymbolAsync(ticker)` and set `CompanyId`.
- [ ] If resolution returns `null`: set `CompanyId = null`, log at `Warning` with:
  ```
  [CyclicalWaves] CompanyId unresolved for ticker={Ticker} enticker={EnTicker}
  ```
- [ ] Do **not** block ingestion on an unresolved symbol — save the record with `CompanyId = null`.
- **Related US:** US-003

---

## Phase 6 — Backfill

### TASK-007 · `BackfillCyclicalWavesCompanyIdService`

- [ ] Create service in Application or Infrastructure layer.
- [ ] `RunAsync(CancellationToken)` algorithm:
  1. Query all CyclicalWaves financial records WHERE `CompanyId IS NULL` — in batches of 500.
  2. For each record: call `CompanyResolverService.ResolveBySymbolAsync(record.Ticker)`.
  3. If resolved: update `CompanyId`, add to save batch.
  4. If not resolved: add `Ticker` to unresolved list.
  5. `SaveChangesAsync()` per batch.
  6. After all batches: log `Warning` per unresolved ticker.
- [ ] Return `BackfillResult { int Resolved, int Unresolved }`.
- [ ] Service is safe to run multiple times (skips already-resolved rows).
- **Related US:** US-005

---

### TASK-008 · Backfill admin endpoint

- [ ] Add `POST /api/v1/admin/cyclicalwaves/backfill-company-id` to the admin controller.
- [ ] Authorize with `[Authorize(Roles = "DataAdmin")]`.
- [ ] Call `BackfillCyclicalWavesCompanyIdService.RunAsync(ct)`.
- [ ] Return `200 OK` with body `{ "resolved": N, "unresolved": M }`.
- [ ] Return `403` for non-DataAdmin callers.
- **Related US:** US-005

---

## Phase 7 — Query Handler Enforcement

### TASK-009 · Update AI query handlers to use `CompanyResolverService`

- [ ] Identify all AI tool adapters and scanner tool adapters that query financial data by symbol.
- [ ] For each handler:
  1. Remove any direct `WHERE Ticker = @symbol` LINQ on financial tables.
  2. Add call to `CompanyResolverService.ResolveBySymbolAsync(symbol)` as first step.
  3. If `null` returned: respond with `NotFound` / `ClarificationRequired` as appropriate.
  4. Use `company.Id` for financial table queries; use `company.InsCode` / `company.IsinCode`
     to resolve `Instrument` for price/index table queries.
- [ ] Do **not** change existing `IComprehensiveAnalysisQueryRepository` or Tsetmc query paths
  unless they contain a direct ticker string lookup.
- **Related US:** US-006

---

## Phase 8 — Architecture Tests

### TASK-010 · Architecture test — no direct ticker lookup outside resolver

- [ ] Add `ArchTests` (or existing architecture test project) assertion:
  - No class **other than** `CompanyResolverService` and `CyclicalWavesCompanyMappingService`
    may contain a LINQ `Where` predicate referencing `Companies.Ticker` or `Companies.EnTicker`.
- [ ] Add assertion: `BackfillCyclicalWavesCompanyIdService` must not reference EF `DbContext`
  directly — it must go through a repository interface.
- [ ] Add assertion: `ICompanyResolverService` interface must reside in Application layer, not
  Infrastructure.
- **Related US:** US-006

---

## Phase 9 — Tests

### TASK-011 · Unit tests — `PersianSymbolNormalizer`

- [ ] Resolves clean ASCII ticker unchanged.
- [ ] Strips ZWNJ (U+200C) from Persian symbol.
- [ ] Strips RLM (U+200F) and RLE (U+202B).
- [ ] Normalizes Arabic Ye → Persian Ye.
- [ ] Normalizes Arabic Kaf → Persian Kaf.
- [ ] Returns empty string for null input.
- [ ] Returns empty string for whitespace-only input.
- **Related US:** US-001

---

### TASK-012 · Unit tests — `CompanyResolverService`

- [ ] Resolves by exact Persian `Ticker`.
- [ ] Resolves by `IsinCode` / `EnTicker` (case-insensitive).
- [ ] Resolves by `InsCode`.
- [ ] Returns `null` (does not throw) when no company matches.
- [ ] Applies normalization before comparison — ZWNJ-polluted input resolves correctly.
- [ ] When multiple resolution steps are tried, returns first match and does not call DB again.
- Use an in-memory `ICompaniesRepository` stub; do not spin up a real DB.
- **Related US:** US-001

---

### TASK-013 · Unit tests — `CyclicalWavesCompanyMappingService`

- [ ] Matches company by primary key (`IsinCode`), updates `Ticker` and `EnTicker`.
- [ ] Falls back to ticker match when `IsinCode` match fails.
- [ ] Does **not** overwrite an already-populated `Ticker` when matched via weaker fallback.
- [ ] Logs correct summary counts.
- [ ] Idempotent: second call produces zero updates when all rows already matched.
- **Related US:** US-004

---

### TASK-014 · Integration tests — backfill endpoint

- [ ] `POST /api/v1/admin/cyclicalwaves/backfill-company-id` with `DataAdmin` role → `200` with
  `{ resolved, unresolved }` counts.
- [ ] Unresolved tickers appear in log output at `Warning`.
- [ ] Non-DataAdmin caller → `403`.
- [ ] Already-resolved rows are not double-counted in `resolved`.
- **Related US:** US-005

---

## Dependency Order

```
TASK-003 (normalizer)
  → TASK-004 (resolver service)
  → TASK-005 (mapping service)
  → TASK-006 (ingestion wiring)
  → TASK-009 (query handler enforcement)

TASK-001 (Companies migration)
  → TASK-002 (FK migration)
  → TASK-006
  → TASK-007 (backfill service)
  → TASK-008 (backfill endpoint)

TASK-003 → TASK-011 (normalizer unit tests)
TASK-004 → TASK-012 (resolver unit tests)
TASK-005 → TASK-013 (mapping service unit tests)
TASK-008 → TASK-014 (integration tests)

TASK-009 + TASK-004 → TASK-010 (architecture tests)
```
