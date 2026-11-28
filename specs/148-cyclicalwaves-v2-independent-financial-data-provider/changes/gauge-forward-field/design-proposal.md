# Design Proposal: ingest and store the provider `forward` value of the P/E and P/S gauges

Status: Gate A: Approved by the owner on 2026-11-28 («Gate A و Gate B تأیید است»)

## Feature Folder

`148-cyclicalwaves-v2-independent-financial-data-provider/changes/gauge-forward-field`

## Layers / Area

backend · database · FinancialData. No frontend, Telegram, routing or billing change.

## 1. Request and goal

On 2026-11-28 the owner asked تحلیل‌اپ about the forward P/E and P/S. Their answer: the gauge service returns a
`forward` field next to the gauge data of each symbol. The owner supplied a live response and asked to update the
gauge fetch and the database storage. Showing the value to users is not part of this request.

## 2. Discovery

- Live response supplied by the owner (`GET /pe-ps-gauges/ticker/فولاد`, 2026-11-28): each of `pe` and `ps` has a
  new member `forward` (a JSON number: `3.8198454018903374` for P/E, `0.8630831034441018` for P/S). All other members
  are the ones already documented.
- The recorded contract (`docs/providers/cyclicalwaves-v2/gauge-equilibrium-api-contract.md`, from the PDF of
  2026-11-04) has no `forward` member.
- `CyclicalWavesV2GaugeDataClient.ReadGauge` reads named members only, so `forward` is dropped today. No raw payload
  is stored, so past values cannot be recovered.
- Storage is `cyclicalwaves_v2."GaugeDetails"`, one row per ticker and gauge type. Every exact decimal is stored as
  five columns (`High`, `Middle`, `Low`, `Sign`, `Scale`); `Close` is the nullable example.
- `EfCoreCyclicalWavesV2GaugeStore.UpsertGaugeAsync` compares every stored member to decide "changed".
- Not known from one sample: whether `forward` is `null` or absent when the provider has no forecast (TahlilApp's page
  shows «محاسبه نشده» for the forward P/S of شپنا), and whether it can be negative.

## 3. Proposed behaviour

- **Contract.** `forward` is an optional member of a gauge object: an exact finite decimal, `null`, or absent. `null`
  and absent both mean "no forward value" and are stored as null; they are never turned into zero. A value that is
  not a number makes the gauge payload invalid, as for every other member (fail visibly, keep the stored row).
  No sign rule is enforced: the provider's value is stored as received.
- **Application contract.** `CyclicalWavesV2Gauge` gains `ExactDecimal? Forward`.
- **Storage.** `GaugeDetails` gains five nullable columns (`ForwardHigh`, `ForwardMiddle`, `ForwardLow`,
  `ForwardSign`, `ForwardScale`) with the same check constraints as `Close` (all-or-none parts, sign and scale
  ranges), added as new named constraints so the existing ones are untouched.
- **Upsert.** `Forward` takes part in the change comparison and is written with the other members. The
  "unchanged → only `FetchedAtUtc` moves" rule (BUG-052) is kept.
- **Readers.** No reader, card, text answer or answer path uses the new value in this change.

## 4. Data and migration

- One additive migration on `FinancialIngestionDbContext`: five nullable columns and two check constraints. No
  backfill, no data rewrite, no lock beyond the catalog change. Existing rows keep null until the next gauge detail
  sync writes them.
- Rollback: the `Down` migration drops the constraints and columns; the client and store tolerate their absence only
  through a code rollback, so migration and code roll back together.

## 5. Testing

- Client unit tests: `forward` present, `null`, absent, and not a number (rejected).
- Store tests on PostgreSQL: insert with a forward value, change of the forward value alone counts as "updated",
  value → null clears the columns, unchanged values move only `FetchedAtUtc`.
- Existing gauge sync, reader, card and E2E tests stay green unchanged. Architecture tests stay green.
- The live contract test asserts nothing new by default; a live run is the owner's (no live call by the agent).

## 6. Records

- `gauge-equilibrium-api-contract.md`: add `forward` with the note that it comes from a live response of 2026-11-28
  supplied by the owner, not from the PDF.
- `docs/providers/cyclicalwaves-v2/README.md`: the line "Forward P/E is not in the provider contract" becomes "is
  ingested and stored; not shown yet".
- `specs/open-questions.md`: OQ-136 notes that the provider value now exists and is stored.
- CHANGELOG: no user-facing entry (nothing visible changes).

## 7. Risks

- **One sample.** The null/absent and sign behaviour is assumed. Mitigation: both forms are accepted; after the
  first production sync a read-only count of null, negative and present values is reported.
- **Provider removes or renames the member.** The value becomes null on the next sync; nothing else fails.
- **Meaning of the number.** Whether it equals the dashed needle on TahlilApp's page is unverified. This change only
  stores it; the 25 research symbols allow a comparison after the first sync.

## 8. Out of scope (separate change, own gates)

- Showing the provider forward value on the gauge cards, and the fate of the Sapio estimate
  (`159-…/changes/valuation-gauge-forward-ratio`, flag off): replace it, or keep it as a fallback. Closing OQ-136.
- History of forward values (only the latest value per gauge is kept, like every other gauge member).

## 9. Implementation plan (for Gate B)

1. `CyclicalWavesV2Gauge` + `ReadGauge`: optional `forward`; client unit tests.
2. `CyclicalWavesV2GaugeDetailRow`, configuration and check constraints; migration `AddGaugeForwardValue` and model
   snapshot.
3. `UpsertGaugeAsync`: comparison and write; PostgreSQL store tests.
4. Fix every other construction of `CyclicalWavesV2Gauge` (tests, fakes) to compile; no behaviour change.
5. Records of section 6.
6. Verification: backend build without new warnings; unit, architecture and affected integration tests with no
   failure outside `specs/test-baseline.md`; migration applied to the local test database only.
7. Owner steps afterwards: commit, release and production migration; then the read-only value count of section 7.
