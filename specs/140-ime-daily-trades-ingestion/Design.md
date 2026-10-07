# Feature 140 — Design: IME Daily Trades Raw Ingestion

## 1. Design status

**READY_FOR_REPOSITORY_VALIDATION**

This design is implementation-ready at the product/architecture level. Exact namespaces, project ownership, scheduler library, authorization attributes, EF configuration style, migration naming, and resilience helpers are intentionally not asserted because the repository was not available.

## 2. Design principles

1. **Raw first.** Preserve provider facts before deriving financial meaning.
2. **One importer, multiple triggers.** Worker and Admin API are orchestration boundaries only.
3. **Database-enforced idempotency.** Application checks are useful; database uniqueness is authoritative.
4. **Auditable acquisition.** Keep the original provider response alongside parsed rows.
5. **No premature company semantics.** IME identities are not assumed to equal TSETMC/company identities.
6. **Forward-compatible acquisition.** Unknown fields remain recoverable through raw payload storage.
7. **Explicit time semantics.** Schedule in Iran timezone; timestamps stored canonically in UTC.

## 3. Logical architecture

```text
                   +----------------------+
                   | FinancialCopilot     |
                   | Worker Scheduler     |
                   +----------+-----------+
                              |
                              | target Jalali date
                              v
+------------------+   +------+-------------------+   +--------------------+
| Admin API        +-->| IME Daily Trade Importer +-->| IME Provider Client|
| authorized POST  |   | application service      |   | typed HttpClient   |
+------------------+   +-------------+-------------+   +----------+---------+
                                    |                            |
                                    |                            v
                                    |                  IME GetAmareMoamelatList
                                    |
                         +----------+-----------+
                         | Persistence boundary |
                         +-----+-----------+-----+
                               |           |
                               v           v
                      Import/batch      Parsed raw
                      audit record      trade rows
```

No AI orchestration dependency exists in this feature.

## 4. Proposed components

Names are conceptual and must be aligned with repository conventions.

```text
Application
  IImeDailyTradeImporter
  ImeDailyTradeImporter
  ImeDailyTradeImportRequest
  ImeDailyTradeImportResult

Infrastructure / Provider
  IImeMarketDataClient
  ImeMarketDataClient
  ImeApiEnvelope
  ImeTradeRecordDto

Persistence
  ImeDailyTradeImport
  ImeDailyTrade
  configurations / migration / repository or DbContext access

Worker
  ImeDailyTradeImportJob

API
  Admin IME import endpoint/controller
```

If the repository already has provider abstractions, data-sync lifecycle objects, job infrastructure, or admin operation patterns, reuse them rather than creating parallel frameworks.

## 5. Request contract

Provider request DTO:

```text
Language              = 8
fari                  = false
GregorianFromDate     = target Jalali yyyy/MM/dd
GregorianToDate       = target Jalali yyyy/MM/dd
MainCat               = 0
Cat                   = 0
SubCat                = 0
Producer              = 0
```

The odd naming (`Gregorian*` carrying Jalali values) is provider-owned and must remain unchanged at the boundary.

## 6. Response parsing

### 6.1 Outer envelope

```csharp
// conceptual only
sealed record ImeApiEnvelope(string? d);
```

### 6.2 Inner payload

`d` contains a JSON string representing an array. A successful parser performs:

```text
body -> ImeApiEnvelope -> envelope.d -> IReadOnlyList<ImeTradeRecordDto>
```

### 6.3 Failure distinction

Treat these differently:

```text
200 + d="[]"                 => Success, zero rows
200 + d missing/null          => Parse/contract failure unless repository evidence proves provider uses it for no-data
200 + malformed d             => Parse failure
non-2xx                       => Provider failure
request timeout/network error => Provider failure
```

This prevents contract failures from being misclassified as valid empty trading days.

## 7. Persistence model

### 7.1 Import/batch table

Proposed logical table: `ImeDailyTradeImports`.

| Column | Purpose |
|---|---|
| `Id` | UUID/import identity |
| `Source` | Stable source value such as `IME` |
| `TargetPersianDate` | Requested date string or validated structured value |
| `TriggerType` | Scheduled/Admin |
| `Status` | Running/Succeeded/Failed using existing conventions |
| `StartedAtUtc` | execution start |
| `CompletedAtUtc` | completion |
| `HttpStatusCode` | nullable provider status |
| `RecordsReceived` | parsed source rows |
| `RecordsInserted` | newly inserted rows |
| `RecordsSkipped` | duplicate rows |
| `RawResponse` | original response JSON, preferably `jsonb` |
| `ErrorCode/ErrorMessage` | bounded failure diagnostics |

Do not enforce one-import-per-date: operations need an audit history of retries/replays. Trade-row uniqueness handles duplication.

### 7.2 Trade row table

Proposed logical table: `ImeDailyTrades`.

Ingestion metadata:

```text
Id                  UUID
ImportId            FK to import record
Source              IME
SourceFingerprint   deterministic hash
FetchedAtUtc
CreatedAtUtc
```

Source fields (preserve provider names semantically; database casing may follow repository convention):

| Source field | Suggested .NET type | Notes |
|---|---|---|
| GoodsName | string? | preserve text |
| Symbol | string? | IME commodity symbol; not a TSETMC symbol |
| ProducerName | string? | no company mapping here |
| ContractType | string? | preserve localized text |
| MinPrice | decimal? | avoid binary floating-point persistence |
| Price | decimal? | same |
| MaxPrice | decimal? | same |
| arze | decimal? | preserve field meaning/name; no translation assumption |
| ArzeBasePrice | decimal? | |
| arzeMinPrice | decimal? | |
| taghaza | decimal? | |
| taghazavoroudi | decimal? | |
| taghazaMaxPrice | decimal? | |
| Quantity | decimal? | |
| TotalPrice | decimal? | may exceed 32-bit integer |
| date | string or validated provider-date value | keep source representation available |
| DeliveryDate | string? | source Jalali format observed |
| Warehouse | string? | |
| ArzehKonandeh | string? | |
| SettlementDate | string?/date? | observed Gregorian-like values and nulls; do not assume without repo/provider validation |
| Category | string? | hierarchical source code |
| xTalarReportPK | long?/decimal? | zero appears in observed data; not universal identity |
| bArzehRadifTarSarresid | string? | nullable source field; semantics not inferred |
| cBrokerSpcName | string? | |
| ModeDescription | string? | |
| MethodDescription | string? | |
| MinPrice1 | decimal? | preserve separately |
| Price1 | decimal? | preserve separately |
| Currency | string? | e.g. ریال |
| Unit | string? | e.g. تن |
| arzehPk | string? | preserve source representation |
| Talar | string? | |
| PacketName | string? | |
| Tasvieh | string? | |

### 7.3 Why decimals

Price, quantity, and total fields must not use `float`/`double` for persistence. Decimal/numeric types avoid binary floating-point artifacts and are safer for future financial aggregation. Exact precision/scale must be selected from observed ranges and repository conventions during implementation.

### 7.4 Raw + parsed dual persistence

This design intentionally keeps both:

- original response: provider evidence and schema-drift recovery;
- parsed columns: efficient future deterministic querying.

This is not duplicate business data. They serve different operational purposes.

## 8. Idempotency design

### 8.1 Problem

No single observed field is proven to be a globally reliable unique ID. In particular, `xTalarReportPK` can be `0`.

### 8.2 Canonical fingerprint

Build a stable canonical representation of every known source field in a fixed field order, with explicit null representation and culture-invariant numeric formatting, then hash it (e.g. SHA-256).

Conceptual input:

```text
GoodsName=<...>|Symbol=<...>|ProducerName=<...>|...|Tasvieh=<...>
```

Prefer canonical JSON serialization if the solution already has a stable checksum convention.

### 8.3 Database constraint

Recommended unique constraint:

```text
UNIQUE (Source, SourceFingerprint)
```

`ImportId` is not part of uniqueness because the same row may be returned by multiple replay runs.

### 8.4 Insert algorithm

Conceptually:

```text
Begin import lifecycle
Call provider
Parse complete response
Begin persistence transaction
Insert rows using conflict-safe semantics against unique fingerprint
Count inserted vs skipped
Persist raw response / finalize import success
Commit
```

If the existing repository prefers EF exception handling over provider-specific `ON CONFLICT`, follow the established pattern, but retain database uniqueness.

### 8.5 Changed-source observation

If any known source field changes, the canonical fingerprint changes and a new observation is stored. This prevents Feature 140 from inventing update semantics for a provider whose revision behavior is not yet documented.

Future features can decide how to choose latest/canonical revisions.

## 9. Scheduling design

Required schedule:

```text
Saturday-Wednesday
17:00
Asia/Tehran
```

If Quartz/Hangfire/another scheduler is already present, express the schedule using that scheduler's timezone-aware mechanism.

Do not implement the business rule as `if DayOfWeek ...` inside a continuously polling loop when the repository already supports cron/calendar scheduling.

If cron is used, validate day-of-week numbering for the actual scheduler; do not copy a cron expression blindly across libraries because Sunday numbering differs between implementations.

## 10. Admin API design

Conceptual request:

```json
{
  "date": "1405/07/15"
}
```

Conceptual response:

```json
{
  "importId": "...",
  "date": "1405/07/15",
  "status": "Succeeded",
  "recordsReceived": 129,
  "recordsInserted": 129,
  "recordsSkipped": 0
}
```

Repository inspection must determine:

- actual route prefix/versioning;
- admin authorization policy;
- response envelope/problem-details conventions;
- whether the API should execute synchronously or enqueue an existing background command.

For the requested capability, either execution model is acceptable only if both ultimately call the same importer and return/enable inspection of the import result. Avoid introducing a new queue solely for this feature unless it matches the existing architecture.

## 11. Date handling

Create/reuse a Persian calendar date service rather than embedding conversion in Worker/API.

Responsibilities:

```text
Validate yyyy/MM/dd input
Format current Iran-local date as yyyy/MM/dd
Reject impossible Jalali dates
Avoid culture-dependent parsing
```

The Worker computes the target date from an Iran-local clock. Store operational timestamps in UTC.

Use an injected clock/time provider if the repository supports it so schedule/date tests are deterministic.

## 12. Resilience

Use existing HTTP resilience infrastructure after repository inspection. Desired behavior:

- finite timeout;
- bounded retries only for transient errors;
- no retry for deterministic parse/contract errors;
- cancellation-aware calls;
- no infinite provider retry inside a scheduled execution;
- structured final failure.

If retries exist, one logical import run should remain one import lifecycle unless existing solution conventions model attempts separately.

## 13. Concurrency

Possible collision:

```text
17:00 Worker import(date=X)
Admin import(date=X)
```

Correctness is guaranteed by unique row fingerprints. Optionally add an application/distributed lock per target date if the repository already has a locking mechanism, to reduce duplicate provider traffic and write contention.

Do not make a new distributed-lock subsystem mandatory solely for Feature 140. Database uniqueness is required regardless.

## 14. Transaction boundary

Recommended:

- create/update Running import state before/around provider acquisition using existing lifecycle conventions;
- after successful parse, persist trade rows and success counters atomically;
- a database failure leaves the import Failed, not Succeeded;
- raw response should be retained on successful HTTP acquisition whenever existing transaction/lifecycle architecture allows it, including parse failures if safely possible.

Exact lifecycle transaction boundaries must follow repository patterns to avoid long-lived DB transactions across network calls.

## 15. Security and operational constraints

- No credentials are present in the supplied IME call; do not assume none will ever be needed.
- Do not expose the manual import endpoint without existing admin authorization.
- Do not accept arbitrary target URLs from the API.
- Apply existing request rate/operational safeguards to manual backfill calls.
- Bound logged error bodies/messages.

## 16. Performance/indexing

Initial expected volume (129 rows in the supplied example) is small enough for a single daily transaction, but design for history.

Recommended indexes, subject to repository/query conventions:

```text
UNIQUE (Source, SourceFingerprint)
INDEX (date)
INDEX (Symbol, date)
INDEX (ProducerName, date)
INDEX (arzehPk)
INDEX (xTalarReportPK)   -- non-unique
INDEX (ImportId)
```

Do not add speculative analytical/materialized indexes yet.

## 17. Testing strategy

### Unit

- request construction;
- Jalali validation/formatting;
- outer envelope parsing;
- inner `d` parsing;
- empty array;
- malformed outer/inner JSON;
- fingerprint stability;
- null and Persian text handling;
- schedule calendar behavior.

### Integration

- migration/schema;
- raw payload persistence;
- all known fields persisted;
- duplicate rerun;
- concurrent duplicate import;
- changed row creates new observation;
- transaction failure does not mark success;
- Admin authorization;
- Admin and Worker resolve same importer.

### HTTP/provider contract fixture

Use a representative fixture from the supplied payload, including:

- a normal row with non-zero `xTalarReportPK`;
- a row where `xTalarReportPK = 0`;
- null `SettlementDate`;
- a non-null `SettlementDate`;
- Persian strings;
- decimal values.

No live IME endpoint dependency should be required for normal CI tests.

## 18. Deployment notes

Before enabling the Worker schedule:

1. Apply migration.
2. Verify provider connectivity from deployment environment.
3. Run one admin import for a known date.
4. Verify raw import record and parsed row count.
5. Rerun the same date and verify inserted count is zero for identical rows.
6. Enable schedule.
7. Observe the first scheduled run and confirm timezone/date.

No historical bulk backfill is required for feature completion.

## 19. Repository-validation checklist

The implementing agent must inspect and record:

- actual Worker scheduler technology and timezone support;
- existing `TimeProvider`/clock abstraction;
- existing Admin controller/authorization pattern;
- current data-ingestion lifecycle/run entities;
- current raw-payload/checksum conventions;
- HTTP client + resilience registration conventions;
- EF naming/type/index conventions;
- migration/testing infrastructure;
- whether PostgreSQL `jsonb` is already used;
- whether bulk insert / conflict-ignore helpers already exist.

Where an existing equivalent exists, use it instead of the conceptual component names in this document.

## 20. Explicit non-goals

No Feature 140 code should answer questions like:

```text
فولاد خوزستان امروز در بورس کالا چقدر فروخت؟
قیمت شمش فولاد خوزستان نسبت به هفته قبل چقدر تغییر کرده؟
فروش ماهانه فخوز را قبل از کدال پیش‌بینی کن
```

Feature 140 only ensures the source facts required for such later features are reliably available.
