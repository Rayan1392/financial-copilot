# Design Proposal: Sapio forward P/E and P/S estimate on the valuation gauge cards (OQ-136)

Status: Gate A: Approved by the owner on 2026-11-26 (OQ-143 to OQ-147 answered as proposed)

Revision 2 (2026-11-26). Revision 1 proposed waiting for a forward field from تحلیل‌اپ. On 2026-11-26 the owner
decided that the exact TahlilApp number is not required and that a forward P/E under Sapio's own name is acceptable.
This revision replaces revision 1; the provider path is kept as an alternative in section 8.

## Feature Folder

`159-capability-source-map-comprehensive-analysis/changes/valuation-gauge-forward-ratio`

## Layers / Area

backend · frontend · telegram-gateway (text only) · AI / FinancialData. No database change is proposed.

## 1. Request and goal

TahlilApp draws a dashed «P/E آینده‌نگر» needle on its gauge; Sapio's gauge cards do not, because no provider route
supplies the value (OQ-136). The owner wants Sapio to show its own forward estimate, clearly labelled as Sapio's.

## 2. Discovery summary

Evidence: `tools/research/forward-pe-reverse-engineering/REPORT.md` (research only, untracked, local database synced to
2026-11-22; the production database was not examined).

- TahlilApp's forward P/E could not be reproduced exactly (`NO_RELIABLE_MATCH`, 168 fixed formulas). It behaves like
  the output of a prediction model.
- TahlilApp's current (TTM) P/E and P/S are reproduced exactly from data Sapio already stores: market value divided by
  the sum of the last four discrete quarters of the TahlilApp income statements.
- Dividing TahlilApp's forward P/S by its forward P/E gives its implied forward net margin, so the sales forecast and
  the margin can be studied separately.
- **Sales half, validated out of sample.** "Last 12 monthly sales × year-over-year growth of the last 4 months"
  reproduces TahlilApp's forward P/S with a mean error of 6.9% on the 9 symbols it was chosen on and 7.3% on 14 fresh
  symbols (12 of 14 within 10%).
- **Margin half, not validated.** No margin rule is stable on both sets. Five rules were compared end to end on all 24
  symbols (`results/margin-options.csv`); all land at a mean P/E error of 17–18% against TahlilApp:

  | Margin rule | Mean error, first 9 | Mean error, fresh 15 | Mean, all 24 | Median, all 24 | Within 20% | Same side of TTM P/E as TahlilApp |
  |---|---|---|---|---|---|---|
  | Mean of the last 8 quarterly net margins | 7.8% | 23.1% | 17.4% | 9.5% | 19 | 22 |
  | Median of the last 8 | 13.0% | 20.9% | 17.9% | 13.5% | 18 | 23 |
  | Median of the last 6 | 16.0% | 17.3% | 16.8% | 9.7% | 18 | 23 |
  | Trimmed mean of 8 (drop 2 + 2) | 11.4% | 20.4% | 17.0% | 11.5% | 17 | 23 |

  The mean breaks on one-off quarters (ستران and حفاری have quarters with a net margin above 70% from non-operating
  income). These rules were compared after seeing the data, so the figures are in-sample.
- Exceptions seen: شپنا (TahlilApp shows no forward P/S and a forward P/E above the current one; every rule misses by
  about 70%), کگل (TahlilApp's TTM gauge is not reproducible from registered capital), آپ (fiscal-year change leaves a
  quarter without a discrete value).
- The estimate assumes that the recent year-over-year sales growth continues for twelve months. In the 24 samples this
  growth factor ranges from 0.9 to 4.05 (median about 2). This is a strong assumption and is the main product risk.

## 3. Proposed behaviour

For a security with a usable P/E or P/S gauge card, Sapio adds a forward estimate when all inputs are present:

```text
forecast sales   = sum of the last 12 monthly sales × (last 4 months ÷ the same 4 months one year earlier)
forward margin   = median of the last 8 quarterly net margins (net profit ÷ revenue, discrete quarters)   [OQ-143]
forward P/S      = current P/S × (revenue of the last 4 quarters ÷ forecast sales)
forward P/E      = forward P/S ÷ forward margin
```

- "Current P/S" is the gauge `close` Sapio already shows, so the estimate needs no price, share count or capital.
  Planning must confirm that the P/E and P/S gauges imply the same market value (the research derived it from P/E).
- Monthly sales come from the accepted monthly activity reports (the month itself, never cumulative rows). Quarterly
  revenue and net profit come from the TahlilApp income statements Sapio already stores (Feature 148).
- **No value is shown** (the card stays as today) when any of these holds; proposed thresholds are OQ-145:
  - fewer than 16 consecutive monthly reports, or the latest month is older than the staleness limit;
  - fewer than 8 quarters with revenue and net profit;
  - the forward margin is zero or negative (no forward P/E; the forward P/S may still be shown);
  - the growth factor is outside the allowed range;
  - the company has no monthly sales reports (banks, insurers, investment companies) or is on an exclusion list
    (refineries, pending evidence).
- The forward value never decides the cheap or expensive label, which stays the provider's `status`.
- Missing data is never replaced by a guess, and a failed or timed-out forward read only hides the forward value.

### User-facing wording (OQ-144)

- Web card: a second, dashed needle with a ring on the same angle geometry (design already prepared in
  `valuation-gauge-cards-visual-refresh` §4.1) and a chip «P/E آینده‌نگر (برآورد ساپیو) ‹مقدار›». A value outside the
  axis pins at the end as the current needle does.
- «جزئیات»: one sentence on the method and its assumption, for example «برآورد ساپیو بر پایهٔ فروش ۱۲ ماه اخیر، رشد
  سالانهٔ ۴ ماه اخیر و میانهٔ حاشیهٔ سود ۸ فصل؛ پیش‌بینی نیست و توصیهٔ سرمایه‌گذاری نیست.»
- Text answer and Telegram: one line «P/E آینده‌نگر (برآورد ساپیو): ‹مقدار›», only when the value exists.
- The source line keeps «تحلیل‌اپ» for the gauge. The forward value is never attributed to TahlilApp.

## 4. Architecture

- **Domain / Application (`FinancialData/ValuationGauges`):** a pure `ForwardRatioEstimator` that owns the formula,
  the guards and the reasons for "no estimate". Inputs and output are plain records (monthly sales series, quarterly
  revenue and net profit, current ratios → forward P/S, forward P/E, the figures behind them, or a reason code). No EF,
  HTTP or provider types.
- **Port:** `IForwardRatioInputsReader`, owned by the application layer, returns the two series for one security.
- **Infrastructure:** one adapter that reads the accepted monthly reports and the stored TahlilApp quarterly income
  statements. It translates rows to the plain input records and decides nothing.
- **Use case:** `ValuationGaugeCardsUseCase` reads the inputs once per request, inside its own time budget and only
  when a P/E or P/S card is displayable. `ValuationGaugeProjector` adds an optional `forward` member to those cards
  (additive in payload version 1; stored cards without it still render).
- **Configuration:** a flag `ValuationGauges:ForwardEstimate:Enabled` (default off) and the guard thresholds, validated
  at startup. Turning the flag off restores today's cards, which is the rollback path.
- **Unchanged:** routing, billing (one credit per usable card), snapshot reads, stale and timeout rules, the LLM path
  (none is involved).
- **Observability:** the existing gauge observer records estimate shown / not shown with the reason code and the read
  latency.

## 5. Data and database impact

- No migration and no new table: the estimate is computed at read time from data already stored.
- To verify in planning (read-only, with the owner's help for production): coverage of 16 months of monthly reports and
  8 quarters per company in production, and how many gauge companies would get an estimate.
- The rule «Forward P/E is not in the provider contract (OQ-136) and is never estimated» in
  `docs/providers/cyclicalwaves-v2/README.md` and the "Forward P/E, future phase" line in `docs/domain-glossary.md`
  are replaced by the approved definition. Approving this proposal approves that rule change.

## 6. Testing and verification

- Unit tests of the estimator: formula, every guard, negative and zero denominators, irregular quarters, rounding.
- A fixture test with the 24 research symbols pins the estimator to the research script's own results (not to
  TahlilApp's numbers), so the production formula cannot drift from the evidence.
- Projector, text composer and web card tests for "with" and "without" a forward value on the same fixtures.
- Adapter tests at the seam with a test database; architecture tests stay green. No live LLM or provider call.

## 7. Risks

- **Users read the estimate as a forecast or as advice.** Mitigation: the Sapio label, the method sentence, the flag.
- **The number differs from TahlilApp's page** (mean about 17%, occasionally far more). Mitigation: Sapio's own name
  and the guards; the owner accepted this difference on 2026-11-26.
- **Growth extrapolation overstates sales** when recent growth is a price shock. Mitigation: the growth-factor range
  (OQ-145).
- **Monthly report quality** (a double-counting defect was fixed before: `specs/bugfix-kesaveh-monthly-sales-double-counting`).
  Mitigation: accepted revisions only, consecutive-month check, no estimate on gaps.
- **Extra read on a 2-second budget.** Mitigation: own time budget; on timeout only the forward value is dropped.
- The evidence is 24 symbols on one date. Behaviour on other dates and industries is unmeasured.

## 8. Alternatives

- **Wait for a forward field from تحلیل‌اپ** (revision 1). Exact parity, but blocked on the provider. It can still
  replace or sit beside the estimate later.
- **Forward P/S only.** The validated half; no margin assumption. Smaller value to users who ask for forward P/E.
- **Precompute and store estimates after each sync.** Faster reads and an audit trail, at the cost of a table, a
  migration and a job. Not needed for one security per request; can follow if the read budget proves too tight.
- **Leave the forward needle out.** No work; the gap remains.

## 9. Open questions (owner decisions)

- **OQ-143** margin rule. Recommended: median of the last 8 quarters (robust to one-off quarters; no rule is measurably
  better against TahlilApp).
- **OQ-144** the user-facing name and the method sentence.
- **OQ-145** guard thresholds. Proposed: growth factor between 0.5 and 4; latest monthly report at most 75 days old;
  refineries excluded until evidence exists.
- **OQ-146** scope: forward P/E and forward P/S (recommended), or forward P/E only.
- **OQ-147** whether the research folder `tools/research/forward-pe-reverse-engineering/` is committed as evidence.
- **OQ-136** is answered in direction (a Sapio estimate) and closes when this proposal is approved or rejected.
