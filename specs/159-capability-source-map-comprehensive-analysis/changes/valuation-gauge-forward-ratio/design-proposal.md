# Design Proposal: Forward P/E and P/S needle on the valuation gauge cards (OQ-136)

Status: Proposed — not approved (Gate A pending)

## Feature Folder

`159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-forward-ratio`

## Layers / Area

backend · database · frontend · AI / FinancialData

## 1. Request

On 2026-11-26 the owner asked to fix OQ-136: TahlilApp draws a second, dashed «P/E آینده‌نگر» needle on its gauge
(۲٫۹۶ for شاراک), and Sapio's gauge cards do not.

## 2. Findings

This is not a defect in Sapio. The value does not reach Sapio, and the approved rule is never to compute or invent it.

- The provider contract `GET /pe-ps-gauges/ticker/{ticker}` returns, per ratio, only `close`, `start`, `end`, `min`,
  `avg`, `max`, the day counts `a`–`f` and `status` (`docs/providers/cyclicalwaves-v2/gauge-equilibrium-api-contract.md`,
  transcribed from the provider PDF dated 2026-11-04). It has no forward value.
- The screener `GET /custom-filtering/ticker/{ticker}` has only the current P/E and P/S (`ScreenerMetricCatalog`, ids 28
  and 29).
- The gauge client and store keep only the contract fields (`CyclicalWavesV2GaugeDataClient`,
  `CyclicalWavesV2GaugeDetailRow`), and no raw response is stored. So Sapio cannot tell from its own data whether the
  live API has gained a field since the PDF.
- Sapio holds no forecast EPS or sales. Feature 148 recorded that "forward data and calculations are not supplied by
  these four routes" (`specs/148-cyclicalwaves-v2-independent-financial-data-provider/impact-analysis.md`).

## 3. What is needed from تحلیل‌اپ

A documented forward ratio inside each gauge of `GET /pe-ps-gauges/ticker/{ticker}`, for example:

```json
"pe": { "...": "...", "close": 5.9751, "forward": 2.96, "forwardDate": "1405-08-30" },
"ps": { "...": "...", "close": 1.7531, "forward": null }
```

The owner needs answers to these questions, and the contract PDF should be updated with them:

1. The field name, and whether it exists for P/E only or for P/S too.
2. Its meaning: which forecast is used (TahlilApp's own model or analysts' consensus), and for which fiscal year.
3. When it is `null` (no forecast, a loss expected) and whether it can be negative.
4. How often it changes, and whether it has its own date.

## 4. Proposed design (built only after تحلیل‌اپ documents the field)

The visual design was already prepared and approved in shape in `valuation-gauge-cards-visual-refresh` §4.1.

- **Contract and ingestion:** read the new nullable field in `CyclicalWavesV2GaugeDataClient`. Store it in one new
  nullable decimal on the gauge detail row, in the same encoding as `close` (one migration, additive, no backfill;
  existing rows stay null until the next sync). Validate it like `close`: a finite number, `null` kept as `null`, never
  turned into zero.
- **Projection:** `ValuationGaugeProjector` adds an optional `forward` value to the P/E and P/S cards. The API and
  history contract gain an optional field only.
- **Web card (`valuation-gauge-cards.tsx`, `ps-gauge.tsx`):** a second, dashed needle with a ring at the forward value
  on the same angle geometry, and a «P/E آینده‌نگر ‹مقدار›» chip next to «P/E امروز». A value outside the axis pins at the
  end as the current needle does. With no forward value, nothing extra is drawn.
- **Text answer and Telegram (`ValuationGaugeTextComposer`):** one line «P/E آینده‌نگر: ‹مقدار›», and only when the
  value exists.
- **Unchanged:** routing, billing (one credit per usable card), the flag, snapshot reads, the stale and timeout rules.
  The forward value never decides the cheap or expensive label, which stays the provider's `status`.

## 5. Alternatives

- **Leave the forward needle out** (today's behavior). No work. The gap with TahlilApp's own page remains.
- **Compute a forward ratio in Sapio.** Rejected: there is no forecast data, and the approved rule forbids it.
- **Check the live API first.** A one-off, read-only `GET` with the configured CyclicalWaves account would show whether
  the field already exists undocumented. It needs the owner's permission to use those credentials from a development
  machine. Even if a field exists, its meaning still needs the provider's confirmation (section 3).

## 6. Risks

- An undocumented field could change or disappear. The reader treats it as optional, so a missing field only hides the
  needle.
- A forward value that users read as advice. The chip text names it as TahlilApp's forward ratio, not a Sapio forecast.
- The migration is additive and nullable, so rollback leaves an unused column only.

## 7. Open questions

- **OQ-136** stays open: the source is blocked on تحلیل‌اپ. The owner chooses one of three paths: ask تحلیل‌اپ for the
  field (section 3), allow a one-off live check (section 5), or leave the needle out.
