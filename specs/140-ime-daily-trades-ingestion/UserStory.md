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
4. Durable persistence of the bounded raw provider response body as received at the application boundary for audit/schema-drift recovery.
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


### FR-03A — Admin date boundary and execution strategy

Admin imports must reject any target date later than the current date in `Asia/Tehran` before calling IME.

For execution mechanics, reuse an existing FinancialCopilot Admin long-running-operation/background-job pattern if one already exists. If no such established mechanism exists, execute the one-day import synchronously and return the typed import result. Do not introduce a new queue or background-processing subsystem solely for Feature 140.

### FR-04 — Raw response preservation

For every HTTP response actually received from IME, preserve a bounded raw response body in the import/batch record **before typed parsing determines success or failure**.

The preferred semantic contract is `RawResponseText`: the provider response body as received at the application boundary. If the repository has a mandatory established `jsonb` convention, it may be reused only when the field is documented as a lossless JSON-value representation rather than a byte-for-byte copy; property ordering, whitespace, formatting, and byte-level fidelity are then not guaranteed.

Raw evidence retention applies to successful responses, empty valid results, malformed outer JSON, malformed inner `d`, unexpected contract shapes, date-integrity failures, and non-success HTTP responses where a body was received. Network failures with no HTTP response naturally have no raw body to preserve.

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

### FR-07 — Idempotency and observation identity

Re-running the same date must not create duplicate exact provider observations. `SourceFingerprint` identifies an exact observed provider row; it is an observation identity, not necessarily a logical/business transaction identifier. `ImeDailyTrades` is an immutable provider-observation store and must not automatically be treated as a canonical/current-trade table by future analytical features.

Because `xTalarReportPK` is observed as `0` for some rows, it must not be treated as the sole universal source identifier.

Use a deterministic `SourceFingerprint` computed from the canonical serialized values of the complete known source row. The database must enforce uniqueness at the persistence boundary, for example:

```text
(Source = "IME", SourceFingerprint)
```

The implementation may additionally index `xTalarReportPK` and `arzehPk` for diagnostics/querying, but neither alone is defined as the universal idempotency key by this specification.

### FR-08 — Replay semantics

If the same source row is returned again, it is skipped as a duplicate.

If the provider later returns a materially different row for the same date/business identifiers, its different complete-row fingerprint is retained as a distinct raw observation rather than silently overwriting history. Future analytical features can define logical trade identity, revision grouping, or canonical/latest observation selection once provider correction semantics are understood. Feature 140 does not attempt to solve canonicalization.

This rule favors raw-source fidelity over premature interpretation of mutable provider identities.


### FR-08A — Requested-date integrity

For every non-empty daily response, every returned row's source `date` must match the requested target Persian date exactly in normalized `yyyy/MM/dd` form.

If any row has another date, the importer must preserve the raw response evidence, fail the import as a provider-contract/integrity failure, report the mismatch count, and must not silently rewrite the row date or report a partially successful import.

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


### FR-14A — Retry semantics for the IME POST

Although the provider uses HTTP POST, this operation is semantically read-only from FinancialCopilot's perspective. Bounded retries may therefore be used for transient failures when consistent with existing repository resilience conventions. Examples include timeout, connection reset, temporary network failure, and selected 5xx responses.

Do not retry malformed JSON, invalid `d`, date-integrity mismatches, schema/contract failures, or business validation failures. Do not introduce a custom retry framework when the repository already has one.

## Acceptance criteria

1. **AC-01:** A typed provider client calls the specified IME endpoint with `Content-Type: application/json` and the supplied constant filter values.
2. **AC-02:** Daily requests set both provider date fields to the same requested Persian date in `yyyy/MM/dd` form.
3. **AC-03:** The outer response and the JSON string inside `d` are both deserialized correctly.
4. **AC-04:** Every known source field listed in this specification has persistence coverage without analytical transformation.
5. **AC-05:** For every HTTP response actually received from IME, the bounded raw response is durably retained even when parsing or validation later fails.
6. **AC-06:** The raw-response storage type and semantics are explicit: `RawResponseText` is preferred; an established `jsonb` convention may be reused only as a lossless JSON-value representation, not as a byte-for-byte copy.
7. **AC-07:** Unknown additional JSON properties do not cause known-field data loss and remain recoverable from the retained raw response.
8. **AC-08:** `HTTP 200` with `d = "[]"` is a successful zero-row import.
9. **AC-09:** Non-success HTTP responses create a failed import and no successful trade batch is reported; any received bounded response body is retained.
10. **AC-10:** Malformed outer JSON creates a failed import and retains the received bounded raw body.
11. **AC-11:** Malformed JSON inside `d` creates a failed import and retains the received bounded raw body.
12. **AC-12:** A repeated import containing identical source rows does not duplicate persisted observations.
13. **AC-13:** Database-level uniqueness protects against concurrent Worker/Admin imports of identical observations.
14. **AC-14:** Idempotency does not rely solely on `xTalarReportPK`, because zero-valued rows are supported.
15. **AC-15:** `SourceFingerprint` is an exact-observation fingerprint and is not defined as a permanent provider business transaction identifier.
16. **AC-16:** A materially changed complete source row produces a distinct immutable observation; Feature 140 does not determine whether multiple observations represent revisions of the same logical trade.
17. **AC-17:** Every row in a non-empty daily result matches the requested Persian trade date; any mismatch or mixed-date response fails the import, preserves raw evidence, and does not persist a successful partial batch.
18. **AC-18:** Worker and Admin API invoke the same importer/service implementation.
19. **AC-19:** The Worker schedule runs Saturday-Wednesday at 17:00 `Asia/Tehran` and does not run Thursday/Friday.
20. **AC-20:** The schedule remains correct regardless of server-local timezone.
21. **AC-21:** An authorized admin can request a specific historical Persian date.
22. **AC-22:** Invalid date format/value is rejected before the provider call.
23. **AC-23:** A future Admin date later than the current `Asia/Tehran` date is rejected before provider invocation unless repository evidence requires a different established project-wide rule.
24. **AC-24:** Manual execution reuses an existing Admin long-running-operation/job pattern if present; otherwise the one-day import executes synchronously. No new queue is introduced solely for Feature 140.
25. **AC-25:** Import result exposes target date, status, received count, inserted count, skipped count, and an import identifier where supported.
26. **AC-26:** Cancellation propagates to the outbound HTTP call and persistence work.
27. **AC-27:** Bounded retries may apply to transient failures for this semantically read-only POST according to existing repository policy; parse/contract/date-integrity/business-validation failures are not retried.
28. **AC-28:** A parsing, validation, or persistence failure cannot be recorded as a successful complete import.
29. **AC-29:** Logging exposes start, provider outcome, parse/integrity failure where applicable, mismatch count where applicable, and final import outcome without routine full-payload logging.
30. **AC-30:** No company mapping, monthly aggregation, forecasting, alerts, LLM tools, charts, or user-facing commodity-exchange query behavior is introduced.
31. **AC-31:** Focused unit and integration tests pass, or environment/repository blockers are explicitly documented in the implementation report.

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
