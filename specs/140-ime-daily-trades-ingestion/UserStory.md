# Feature 140 — IME Daily Trades Raw Ingestion

## Status

Planned. Specification-only package. No production repository was available while this feature was authored, so repository-specific class names, routes, scheduler technology, EF conventions, and existing admin authorization policies must be verified during implementation.

## Product intent

FinancialCopilot needs a durable daily history of Iran Mercantile Exchange (IME) transactions before analytical behavior is added on top of it.

The immediate feature is deliberately narrow: **collect the daily IME transaction payload, preserve it faithfully, and make the same import operation callable from both the Worker schedule and an Admin API trigger.**

The strategic purpose is to build a first-party historical dataset that can later support:

- company-level commodity-exchange transaction questions;
- daily and monthly price/volume trends;
- abnormal price, demand, supply, or traded-volume alerts;
- pre-Codal estimates of monthly company sales using observed IME transactions;
- reconciliation of IME transaction activity with published monthly production/sales reports;
- later AI tools that answer questions from deterministic stored data rather than query-time scraping.

None of those analytical capabilities are implemented by Feature 140.

## User stories

### Story 1 — Scheduled daily acquisition

As a FinancialCopilot operator, I want IME daily trades collected automatically after the normal trading day, so that historical trade data accumulates without manual intervention.

### Story 2 — Manual/replay acquisition

As an administrator, I want to trigger the exact same importer for a specified Persian date through the API, so that I can backfill, replay, or repair a day without running Worker code manually.

### Story 3 — Faithful raw preservation

As a future analytics developer, I want the source response and all known trade fields preserved without analytical transformation, so that later features can derive metrics without having to reacquire historical source data.

## Source contract

### Endpoint

```text
POST https://ime.co.ir/subsystems/ime/services/home/imedata.asmx/GetAmareMoamelatList
Content-Type: application/json
```

### Request

```json
{
  "Language": 8,
  "fari": false,
  "GregorianFromDate": "1405/07/15",
  "GregorianToDate": "1405/07/15",
  "MainCat": 0,
  "Cat": 0,
  "SubCat": 0,
  "Producer": 0
}
```

Despite the provider field names `GregorianFromDate` and `GregorianToDate`, the observed request uses a **Persian/Jalali date string**. Feature 140 must preserve this provider contract and must not rename or reinterpret it at the HTTP boundary.

For a daily import both date fields are the same target date, formatted as `yyyy/MM/dd`.

### Response envelope

The service returns an outer JSON object whose `d` property contains the trade array serialized as a JSON string:

```json
{
  "d": "[{\"GoodsName\":\"...\",\"Symbol\":\"...\"}]"
}
```

The client therefore performs two-stage deserialization:

```text
HTTP JSON body
  -> deserialize envelope
  -> read d
  -> deserialize d string
  -> trade rows
```

An HTTP 200 with `d = "[]"` is a valid successful import containing zero trades.

## Known source row fields

Feature 140 must preserve all observed fields:

```text
GoodsName
Symbol
ProducerName
ContractType
MinPrice
Price
MaxPrice
arze
ArzeBasePrice
arzeMinPrice
taghaza
taghazavoroudi
taghazaMaxPrice
Quantity
TotalPrice
date
DeliveryDate
Warehouse
ArzehKonandeh
SettlementDate
Category
xTalarReportPK
bArzehRadifTarSarresid
cBrokerSpcName
ModeDescription
MethodDescription
MinPrice1
Price1
Currency
Unit
arzehPk
Talar
PacketName
Tasvieh
```

Field names are source terminology. Feature 140 must not reinterpret their business meaning beyond safe type parsing.

## Scope

Feature 140 includes:

1. A typed IME HTTP client for the source endpoint.
2. Two-stage response parsing.
3. Daily-date request construction using Persian/Jalali date formatting.
4. Durable persistence of the original provider response for audit/schema-drift recovery.
5. Durable persistence of each parsed trade row with all known source fields.
6. Deterministic idempotency for repeated imports of the same source rows.
7. A Worker schedule for Saturday through Wednesday at 17:00 Iran time.
8. A manual Admin API trigger accepting a requested Persian date.
9. Shared import/application logic between Worker and Admin API.
10. Structured operational telemetry and import-result reporting.
11. Unit/integration coverage for parsing, persistence, replay, scheduling, failures, and concurrency.

## Functional requirements

### FR-01 — Shared importer

There must be one application-level import operation conceptually equivalent to:

```text
ImportDailyTrades(targetPersianDate, triggerContext, cancellationToken)
```

Both Worker and Admin API call this operation. HTTP acquisition, parsing, persistence, and idempotency logic must not be duplicated in trigger-specific code.

### FR-02 — Scheduled trigger

The Worker triggers the import:

- Saturday;
- Sunday;
- Monday;
- Tuesday;
- Wednesday;
- at 17:00 using `Asia/Tehran` explicitly.

Thursday and Friday are excluded.

The schedule must not rely on the host operating system's local timezone.

### FR-03 — Admin trigger

An authorized Admin API operation must accept a Persian date, for example:

```json
{
  "date": "1405/07/15"
}
```

The exact route and authorization mechanism must follow existing repository conventions after repo inspection. The endpoint must return an operational result containing at least target date, outcome, received count, inserted count, skipped/duplicate count, and import/run identifier when available.

### FR-04 — Raw response preservation

For every provider response that reaches the persistence phase, preserve the original response content in an import/batch record (prefer `jsonb` when the repository uses PostgreSQL JSON persistence; otherwise use the nearest established raw-payload convention).

This raw copy exists for:

- auditability;
- provider schema-drift diagnosis;
- reparsing without reacquiring data;
- evidence when future analytics disagree with parsed values.

It is not the primary query model for future analytics.

### FR-05 — Parsed source-row persistence

Persist each trade as an explicit row containing all known source fields plus ingestion metadata. Do not aggregate, normalize to company-level facts, convert units, calculate derived prices, map symbols, or alter source values in this feature.

### FR-06 — Forward-compatible schema drift

A newly added provider field must not make the entire raw response unrecoverable. Unknown JSON fields should be tolerated by deserialization unless existing solution policy explicitly requires strict contracts. The original payload remains stored so a future migration can recover new fields.

A removed or type-incompatible field that prevents safe parsing must fail the import rather than silently produce corrupted rows.

### FR-07 — Idempotency

Re-running the same date must not create duplicate trade rows.

Because `xTalarReportPK` is observed as `0` for some rows, it must not be treated as the sole universal source identifier.

Use a deterministic `SourceFingerprint` computed from the canonical serialized values of the complete known source row. The database must enforce uniqueness at the persistence boundary, for example:

```text
(Source = "IME", SourceFingerprint)
```

The implementation may additionally index `xTalarReportPK` and `arzehPk` for diagnostics/querying, but neither alone is defined as the universal idempotency key by this specification.

### FR-08 — Replay semantics

If the same source row is returned again, it is skipped as a duplicate.

If the provider later returns a materially different row for the same date/business identifiers, its different complete-row fingerprint is retained as a distinct raw observation rather than silently overwriting history. Future analytical features can define correction/revision semantics if needed.

This rule favors raw-source fidelity over premature interpretation of mutable provider identities.

### FR-09 — Import lifecycle

Track import execution separately from trade rows. At minimum capture:

```text
ImportId
Source
TargetPersianDate
TriggerType (Scheduled | Admin)
StartedAtUtc
CompletedAtUtc
Status
HttpStatusCode when available
RecordsReceived
RecordsInserted
RecordsSkipped
Error summary when failed
RawResponse
```

Follow existing repository lifecycle/status conventions when they exist.

### FR-10 — Atomic persistence

Once parsing succeeds, parsed-row persistence and successful import completion must be atomic where practical. A partially inserted batch must not be reported as a successful complete import.

Database uniqueness remains the final concurrency/idempotency guard.

### FR-11 — Provider failures

Timeout, connection failure, non-success status, malformed outer JSON, missing/unusable `d`, and malformed inner JSON are failed imports. They must produce structured diagnostics and must not be converted to an empty successful day.

### FR-12 — Empty successful day

`HTTP 200` plus a valid empty inner array is successful and records:

```text
RecordsReceived = 0
RecordsInserted = 0
```

### FR-13 — Observability

Use existing structured logging conventions. Recommended event names:

```text
IME_IMPORT_STARTED
IME_API_REQUEST
IME_API_SUCCESS
IME_API_FAILURE
IME_PARSE_FAILURE
IME_IMPORT_COMPLETED
IME_IMPORT_FAILED
```

Recommended fields:

```text
ImportId
TriggerType
TargetPersianDate
RecordsReceived
RecordsInserted
RecordsSkipped
ElapsedMilliseconds
HttpStatusCode
```

Do not log the full payload routinely; the persisted raw response is the audit artifact.

### FR-14 — Cancellation and timeout

The Admin request and Worker shutdown cancellation tokens must flow through the importer and HTTP call. Use the existing solution timeout/resilience policy after repository inspection. Do not create unbounded retries.

## Acceptance criteria

1. **AC-01:** A typed provider client calls the specified IME endpoint with `Content-Type: application/json` and the supplied constant filter values.
2. **AC-02:** Daily requests set both provider date fields to the same requested Persian date in `yyyy/MM/dd` form.
3. **AC-03:** The outer response and the JSON string inside `d` are both deserialized correctly.
4. **AC-04:** Every known source field listed in this specification has persistence coverage without analytical transformation.
5. **AC-05:** The original provider response is preserved on the import/batch record for audit and future reparsing.
6. **AC-06:** Unknown additional JSON properties do not cause known-field data loss and remain available in the raw response.
7. **AC-07:** `HTTP 200` with `d = "[]"` is a successful zero-row import.
8. **AC-08:** Non-success HTTP responses create a failed import and no successful trade batch is reported.
9. **AC-09:** Malformed outer JSON creates a failed import.
10. **AC-10:** Malformed JSON inside `d` creates a failed import.
11. **AC-11:** A repeated import containing identical source rows does not duplicate persisted trades.
12. **AC-12:** Database-level uniqueness protects against concurrent Worker/Admin imports of identical rows.
13. **AC-13:** Idempotency does not rely solely on `xTalarReportPK`, because zero-valued rows are supported.
14. **AC-14:** A materially changed complete source row produces a distinct raw observation rather than silently overwriting the previous observation.
15. **AC-15:** Worker and Admin API invoke the same importer/service implementation.
16. **AC-16:** The Worker schedule runs Saturday-Wednesday at 17:00 `Asia/Tehran` and does not run Thursday/Friday.
17. **AC-17:** The schedule remains correct regardless of server-local timezone.
18. **AC-18:** An authorized admin can request a specific historical Persian date.
19. **AC-19:** Invalid date format/value is rejected before the provider call.
20. **AC-20:** Import result exposes target date, status, received count, inserted count, skipped count, and an import identifier where supported.
21. **AC-21:** Cancellation propagates to the outbound HTTP call and persistence work.
22. **AC-22:** A parsing or persistence failure cannot be recorded as a successful complete import.
23. **AC-23:** Logging exposes start, provider outcome, parse failure where applicable, and final import outcome without routine full-payload logging.
24. **AC-24:** No company mapping, monthly aggregation, forecasting, alerts, LLM tools, charts, or user-facing commodity-exchange query behavior is introduced.
25. **AC-25:** Focused unit and integration tests pass, or environment/repository blockers are explicitly documented in the implementation report.

## Out of scope

- Mapping IME `Symbol`, `ProducerName`, or `ArzehKonandeh` to FinancialCopilot companies.
- Mapping IME commodity symbols to TSETMC symbols.
- Monthly sales aggregation or prediction.
- Codal reconciliation.
- Price trend metrics.
- Supply/demand indicators.
- Price/volume alerts.
- AI/LLM tools and semantic routes.
- Chat answers about IME data.
- Admin UI changes; only the backend trigger contract is in scope.
- Historical bulk backfill orchestration beyond manually importing a specified day.
- Provider correction/revision business semantics beyond preserving changed raw observations.

## Product follow-up candidates

Future features should be separate and depend on Feature 140 rather than expanding its scope:

1. IME producer/company identity mapping.
2. Daily commodity/company aggregation.
3. Monthly IME sales estimation and Codal pre-publication forecast.
4. Commodity price/supply/demand trend metrics.
5. Alert rules for material price or traded-volume changes.
6. Deterministic FinancialCopilot tools for company IME questions.
7. AI-generated proactive IME insights based on deterministic metrics.
