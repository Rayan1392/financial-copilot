# Tasks - Trading Instrument Unification

## Phase 1 — Remove Provider Filter from Instrument Lookup

### 1.1 — `InstrumentMapByInsCodeAsync` (TsetmcDirectFeedSyncService)

Remove the `ProviderName == _options.ProviderName` filter from the instrument lookup used by all
`Persist*Async` methods:

```csharp
// Before
.Where(row => row.ProviderName == _options.ProviderName && codes.Contains(row.InstrumentCode))

// After
.Where(row => codes.Contains(row.InstrumentCode))
```

This single change unblocks intraday trades immediately: instruments previously inserted by
`StockMarketDbSyncService` are now found, so trade records are no longer silently dropped.

### 1.2 — Auto-create instrument stubs for unseen InsCodes

In `PersistIntradayTradesAsync`, if an `InsCode` is not found in the map after the lookup, create
a minimal stub row instead of silently skipping:

```csharp
// Before
if (!instruments.TryGetValue(source.InsCode, out var instrument)) continue;

// After — create stub if missing
if (!instruments.TryGetValue(source.InsCode, out var instrument))
{
    instrument = new TradingInstrumentRow
    {
        Id = Guid.NewGuid(),
        ExternalInstrumentId = BuildInstrumentGuid(source.InsCode),
        InstrumentCode = source.InsCode,
        Symbol = source.Symbol,             // available from TradeLastDay response
        ProviderName = _options.ProviderName,
        LastSynchronizedAt = timeProvider.GetUtcNow()
    };
    dbContext.TradingInstruments.Add(instrument);
    instruments[source.InsCode] = instrument;
}
```

Apply the same pattern to `PersistDailyTradesAsync` and `PersistIntradayIndicesAsync` /
`PersistDailyIndicesAsync` where index records carry an `InsCode` that may not yet have a row.

### 1.3 — Stop StockMarketDbSyncService from writing TradingInstruments

`StockMarketDbSyncService.PersistInstrumentsAsync` currently upserts `TradingInstrumentRow`
records. Because TSETMC is now the single owner of this table:

- Remove the instrument-upsert logic from `StockMarketDbSyncService`.
- `StockMarketDbSyncService` should only *look up* existing instrument IDs by `InstrumentCode`
  when persisting trade rows (the existing `InstrumentMapAsync` usage in `PersistIntradayTradesAsync`
  is already a read-only lookup — keep it but remove the `ProviderName` filter there too).

> **Migration note:** After deployment, rows with `ProviderName = "StockMarketDb"` in
> `TradingInstruments` will remain. A one-off SQL script should delete or merge them once TSETMC
> instruments sync has run and the `InstrumentCode` uniqueness is confirmed clean:
> ```sql
> DELETE FROM "TradingInstruments"
> WHERE "ProviderName" = 'StockMarketDb'
>   AND "InstrumentCode" IN (
>       SELECT "InstrumentCode" FROM "TradingInstruments"
>       WHERE "ProviderName" = 'TsetmcWebService'
>   );
> ```

## Phase 2 — Noavaran Amin Company Linkage via TSETMC Codes

### 2.1 — Store TSETMC codes on NormalizedCompanyRow

The Noavaran Amin company catalog (`/api/v3/BaseInfo/Companies`) returns three TSETMC identity
fields per company:

| Field | Meaning |
|---|---|
| `tseCode` | Numeric `InsCode` as string (e.g. `"9987529074833218"`) |
| `tseCIsinCode` | 12-char ISIN-like code (e.g. `"IRO7ABYP0004"`) — same as TSETMC `InstrumentID` |
| `tseSIsinCode` | 12-char share ISIN (e.g. `"IRO7ABYP0001"`) |

Add three nullable columns to `NormalizedCompanyRow` (EF migration required):
- `TseCode` (`string?`) — stores `tseCode` from the Noavaran Amin company record
- `TseCIsinCode` (`string?`) — stores `tseCIsinCode`
- `TseSIsinCode` (`string?`) — stores `tseSIsinCode`

Populate them in `NadpcoApiCompanyNormalizer` from the company catalog response.

### 2.2 — Use TSETMC codes as the cross-source join key

Update `TsetmcDirectFeedSyncService.PersistInstrumentsAsync` to use `tseCode`
(mapped to `InsCode` as `long`) as the join key when resolving `NormalizedCompanyId`:

```csharp
// Before — joins on InstrumentCode string
var companies = await dbContext.Companies
    .Where(row => row.InstrumentCode != null && codes.Contains(row.InstrumentCode))
    ...

// After — also checks TseCode (which equals InsCode as string)
var companies = await dbContext.Companies
    .Where(row => (row.TseCode != null && codes.Contains(row.TseCode))
               || (row.InstrumentCode != null && codes.Contains(row.InstrumentCode)))
    ...
```

The existing `InstrumentCode` fallback is kept for backward compatibility with companies that
were linked before `TseCode` was populated.

### 2.3 — Add unique constraint on InstrumentCode

After Phase 1 cleanup has ensured no duplicate `InstrumentCode` values remain, add a unique
index on `TradingInstruments.InstrumentCode` (non-null rows only) to prevent future duplicates:

```sql
CREATE UNIQUE INDEX "IX_TradingInstruments_InstrumentCode_Unique"
ON "TradingInstruments" ("InstrumentCode")
WHERE "InstrumentCode" IS NOT NULL;
```

This becomes an EF migration once the cleanup confirms zero duplicates.

## Phase 3 — Tests and Architecture Guard

- Unit test: `TsetmcDirectFeedSyncService.SynchronizeIntradayTradesAsync` persists trade rows
  even when no prior `SynchronizeInstrumentsAsync` call has been made (stub auto-creation).
- Unit test: `StockMarketDbSyncService.SynchronizeAsync(IntradayTrades)` resolves instruments
  from the shared table without the provider filter.
- Architecture test: no production code outside `TsetmcDirectFeedSyncService` calls
  `dbContext.TradingInstruments.Add(...)` or `.Update(...)`.
