# User Story: Option Comparison Answers

## Feature Folder

`168-option-comparison-answers`

## Layers / Area

Layers: backend | frontend | telegram-gateway (text only)
Area: AI, FinancialData, Notifications

## Priority

P1

## Complexity

XL (five delivery steps; each is releasable on its own)

## Depends On

`167-option-chain-data-and-exercise-ranking` — implemented on `develop`; its definition of done is
open on AC-13 (live sync) and the rollout. Steps 1 to 4 of this feature can be built and tested
with fakes before that; they show real data only after the Feature 167 polling switches are on.

## References

- `specs/168-option-comparison-answers/design-proposal.md` (approved at Gate A, 2026-11-28, with the amendment recorded in its status)
- `specs/167-option-chain-data-and-exercise-ranking/user-story.md`
- `specs/POLICY-V1-FREEZE.md` — V2 path only
- `docs/domain-glossary.md` — "Option Market (Feature 167)"; new terms added by task 8
- `docs/tsetmc-webservice-datasource.md`
- Model capability: Feature 159 valuation gauge cards (`ValuationGaugeIntentRules`, `ValuationGaugeCapabilityExecutor`, `ValuationGaugeBillingPolicy`, `valuation-gauge-cards.tsx`)
- Data model: `FinancialIngestionDbContext` (`OptionContracts`, `OptionOrderBookLevels`, `LatestMarketQuotes`, `DailyInstrumentTrades`); conditional tracker rules (`AlertRule`, `AlertRuleGovernance`)
- Open questions: OQ-177, OQ-178, OQ-179 in `specs/open-questions.md`

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As an investor who trades or considers options on Iranian shares,
I want to ask Sapio about the options of a share and see their costs, prices and yields side by
side, with fees and real order-book volume, and be told when a call prices the share below the
market,
so that I can judge a call, a put or a covered call against holding or trading the share itself.

## Business Context

Feature 167 stores option contracts and order books but shows them only through an API. Traders
compare options by cash needed, cost of deferral, break-even and liquidity, not by one ordering.
This feature turns the stored data into assistant answers for web and Telegram and into one alert,
without naming a "best" position.

## Scope

Delivery steps, in order:

1. Measures with fees and five-level fill; call view; assistant capability, web card, Telegram
   text, entry points.
2. Put view and covered-call view.
3. Quantity and expiry taken from the question.
4. Theoretical price and implied volatility columns.
5. Discount alert.

## Out Of Scope

- Order placement, portfolio awareness (whether the user holds the share), writer margin.
- Multi-leg strategies other than the covered call.
- A standalone option-chain page.
- Restricting the starter card's symbol picker to symbols with options (OQ-150 option b).
- Dividend-adjusted theoretical prices.
- Changing the Feature 167 ranking endpoint.

## Functional Requirements

### Measures

1. **Fees.** Trade fee 0.001 of premium, exercise fee 0.0005 of strike, transfer tax 0.005 of
   strike for the side delivering a share. The three rates are validated configuration with these
   defaults. Every answer states the rates it used.
2. **Underlying class.** Each underlying is classified as share or fund unit (OQ-177). The transfer
   tax applies to shares only. An unknown class is treated as a share and the answer says so.
3. **Fill price.** For a quantity in contracts, the volume-weighted price over order-book levels 1
   to 5: ask side for a buyer, bid side for a writer. If the levels cannot fill the quantity the
   row is "not fillable" and carries the available volume; a side with no order is "no order".
4. **Call view.** Per call: fill ask, effective purchase cost, break-even distance, intrinsic
   value, time value, implied annual rate, leverage, cash now, cash at expiry, maximum loss, open
   interest; as defined in the design proposal, section 1.
5. **Put view.** Per put: fill ask, effective sale price, distance from market, protection cost
   and its annualized value, open interest.
6. **Covered-call view.** Per call: fill bid, premium income, yield if exercised and if not
   exercised, each also annualized, downside cushion, open interest.
7. **Annualized figures** are present only with at least 3 days to expiry; the implied annual rate
   additionally only for in-the-money calls.
8. **Highlights.** Per expiry and view, only when at least two rows are fillable: calls — lowest
   effective purchase cost, lowest implied annual rate; puts — highest effective sale price;
   covered call — highest annualized yield if exercised. Ties go to the larger fillable volume,
   then the symbol.
9. **Freshness.** Measures that need the order book are computed only when the last successful
   order-book sync is not older than the trading session of the underlying's latest quote.
   Otherwise rows carry trade prices and open interest only, and the answer is marked "order book
   not current".
10. **Theoretical price and implied volatility** (step 4). Black–Scholes price per row from
    underlying price, strike, days to expiry, the configured risk-free rate and the underlying's
    historical volatility; implied volatility from the fill price. Absent when an input is missing,
    expiry is today, or the solver does not converge. Inputs are stated in the answer (OQ-178).

### Assistant capability

11. **Routing.** A message with an option word and one company token routes to
    `option_comparison` on the V2 path, before generic single-symbol routing. View from wording:
    call by default; «اختیار فروش» → put; «کاورد کال» or «فروش اختیار خرید» → covered call;
    «زنجیره اختیار» → call. Messages asking whether to buy or sell keep getting the existing
    refusal. A message naming a single contract symbol (for example «ضمخا9021») routes to the view
    of that contract's side, limited to its expiry, with that row marked.
12. **Quantity and expiry** (step 3). «N قرارداد» sets the quantity (1 to 100,000). An expiry is
    taken from a Jalali month name, «نزدیک‌ترین سررسید», or a Jalali date; an expiry that is not
    listed returns the listed ones. Without them: quantity 1 and the three nearest expiries.
13. **Availability.** The capability answers only when `OptionAnswers:Enabled` is true; otherwise
    routing behaves as today. Availability is exposed to clients like other capabilities.
14. **Payload.** The response carries `OptionComparison` (versioned; only optional members may be
    added later): view, underlying symbol, class, price and its time, quantity, expiries, per
    expiry the rows with the view's measures and row state, highlights, fee rates, order-book and
    open-interest observation times, book-current flag, theoretical-price inputs when present. It
    is persisted with the message and returned when a conversation is reopened.
15. **Text.** Every answer also has a Persian text rendering used by Telegram and as the web
    fallback: title, underlying price and time, one line per fillable row of the nearest expiry (at
    most eight rows, nearest to the money first), highlights, notes, data time.
16. **Outcomes without data** (not charged): no symbol → clarification; unknown symbol → the
    existing unknown-symbol answer; symbol without unexpired listed options → one sentence;
    no underlying price → one sentence; provider data never synchronized → one sentence.
17. **Credits.** One credit per answered turn that shows at least one row; nothing otherwise.

### Presentation

18. **Web card**, one component for desktop chat and `/m`: view tabs (only views that have rows),
    expiry tabs, rows by strike with the row nearest to the money visually marked, highlights as
    labels, rows without orders collapsed behind «نمایش بدون سفارش‌ها», underlying line, fee line,
    data time, per-view note block, and the "order book not current" state. Numbers in Persian
    digits; percentages with one decimal; RTL; both themes; keyboard reachable tabs.
19. **Wording.** Titles and labels as in the design proposal, section 3. The words «بهترین»,
    «مناسب», «پیشنهاد» and «فرصت» do not appear in any label, note, text rendering or alert.
20. **Entry points.** A starter card («اختیار خرید یک نماد») with the symbol picker; an item in
    the symbol actions menu; a follow-up button under answered single-symbol turns. The menu item
    and follow-up button appear only for symbols that have listed options. All are hidden when the
    capability is unavailable.

### Discount alert (step 5)

21. **Rule.** A new tracker rule type on an underlying, optionally one expiry: fires when a call
    is fillable for the rule's quantity and its effective purchase cost is at or below the
    underlying's last price minus the rule's margin (default 0, OQ-179).
22. **Evaluation.** After each successful order-book sync, inside the session, only on a current
    book. Fires at most once per contract per session; existing cooldown and plan limits apply.
23. **Notification.** Names the contract, strike, fill ask, effective purchase cost, underlying
    price, fillable volume and data time, with the notes on volume and on paying the strike.
24. **Creation and management.** From the call card («خبرم کن») and by a question such as «وقتی
    اختیار خرید اخابر زیر قیمت سهم شد خبر بده»; listed, paused and removed where other trackers
    are.

## Data / Schema

- No new table for steps 1 to 4. `OptionComparison` is stored inside the existing persisted
  message payload, like `ValuationGaugeCards`.
- Step 4 reads daily closes from `DailyInstrumentTrades`.
- Step 5 adds a rule type to the conditional tracker model. Whether that needs a migration depends
  on how rule definitions are stored; task 7.1 settles it before code and reports back if a schema
  change is needed.
- Configuration: `OptionAnswers` (enabled flag, three fee rates, annualization floor in days,
  maximum expiries, risk-free rate, volatility window).

## API (if any)

| Method | Route | Request | Response | Notes |
|---|---|---|---|---|
| existing AI query endpoints | — | — | adds optional `optionComparison` | Facade contract and persisted message contract. |
| GET | existing capability availability endpoint | — | adds the option capability | |
| GET | `/api/v1/market/options/comparison` | `underlying`, `view`, `expiry` (optional), `quantity` (optional) | `OptionComparison` | Same policy as the Feature 167 read endpoints; lets the card refresh without a new chat turn. Not charged. |
| existing tracker endpoints | — | new rule type | — | Step 5. |

## AI Query Path (if any)

Capability `option_comparison`, gate-routed only, V2 path. Deterministic intent rules; no model
call is needed to route or to compose the answer. Missing data and unsupported questions as in
requirement 16.

## UI (if any)

Web: the card in `MessageList` (desktop and `/m`), the starter card, the symbol actions menu item,
the follow-up button, and the tracker list entry for the new rule type. Telegram: text only.

## Non-Functional Requirements

- **Layering.** Measures, fees, fill and highlight rules are pure application/domain code, unit
  tested without database or network; web, Telegram, API and alert all use them.
- **Performance.** One answer uses a fixed number of queries regardless of chain size; target
  under 300 ms server time for a chain of 60 contracts on the development database.
- **Read-only.** The answer path never calls the provider.
- **Bounds.** At most three expiries per answer unless one is requested; quantity capped.
- **Validation.** Option settings validated at start-up (rates between 0 and 0.05, floor ≥ 0).
- **Authorization.** Read endpoint as Feature 167; alerts through the existing tracker permissions.
- **Observability.** One structured log line per answered turn: symbol, view, expiries, rows,
  fillable rows, book-current flag, duration. Alert evaluations report evaluated and fired counts.
- **Compatibility.** `OptionComparison` JSON is a persisted contract: version field, additive
  changes only.

## Acceptance Criteria

Step 1

- [x] AC-01: With fees 0.001 and 0.0005, strike 1,000, asks 544 × 10 contracts, underlying 1,489: effective purchase cost is 1,545.044, break-even distance 3.8%, time value 55, leverage 2.74.
- [x] AC-02: Quantity 15 against asks 544 × 10 and 550 × 10 fills at 546; quantity 25 is "not fillable" with available volume 20; a call with no ask is "no order".
- [x] AC-03: Implied annual rate is present only for in-the-money calls with at least 3 days to expiry.
- [x] AC-04: Highlights appear only with two or more fillable rows and follow the tie rule.
- [x] AC-05: With an order book older than the underlying's session, rows carry trade prices and open interest only and the answer is marked "order book not current".
- [x] AC-06: «اختیار خرید اخابر» and «زنجیره اختیار اخابر» route to the call view on the V2 path; «اخابر بخرم؟» does not; with the flag off none of them routes to the capability.
- [x] AC-07: Each outcome of requirement 16 returns its sentence and is not charged; an answer with rows is charged one credit.
- [x] AC-08: The payload survives persistence: reopening the conversation shows the same card.
- [x] AC-09: The web card renders views, expiries, highlights, collapsed empty rows, notes and the not-current state on desktop and `/m`, in both themes, with contrast of at least 4.5:1 for text.
- [~] AC-10 (text content tested; MarkdownV2 validity of an option answer not tested): The Telegram text of an answer contains the title, underlying price, at most eight rows, highlights, notes and data time, and is valid MarkdownV2.
- [x] AC-11: No label, note, text rendering or alert contains «بهترین», «مناسب», «پیشنهاد» or «فرصت» (automated check over the string tables).
- [x] AC-12 (starter card and menu item done and tested; the follow-up button was taken out of scope by the owner, OQ-185): The starter card, menu item and follow-up button produce a question that routes to the capability; the menu item and follow-up button are absent for a symbol without options and when the capability is unavailable.

Step 2

- [x] AC-13: Put on a share, strike 1,400, fill ask 20: effective sale price is 1,400 × (1 − 0.0055) − 20.02 = 1,372.28; on a fund unit it is 1,400 × (1 − 0.0005) − 20.02 = 1,379.28.
- [x] AC-14: Covered call on a share, underlying 1,489, strike 1,500, fill bid 120, 30 days: premium income 119.88; yield if exercised (1,491.75 + 119.88 − 1,489) ÷ 1,489 = 8.2%; yield if not exercised 8.1%; both annualized.
- [x] AC-15: «اختیار فروش اخابر» routes to the put view and «کاورد کال اخابر» to the covered-call view; the card offers only views that have rows.

Step 3

- [x] AC-16: «اختیار خرید اخابر ۵ قرارداد سررسید دی» answers for quantity 5 and the expiry in Dey; an unlisted expiry returns the listed ones; a quantity outside the range is rejected with a sentence.

Step 4

- [x] AC-17: For reference inputs the Black–Scholes call and put prices match published values to four decimals, and the implied volatility of a price computed at 60% returns 60% ± 0.1.
- [x] AC-18: The columns are absent, with a reason, when the rate is not configured, the volatility window has too few closes, or expiry is today.

Step 5

- [~] AC-19 (rule and evaluation implemented; repeats follow the rule's cooldown and re-arm policy, not once per contract per session; no automated test): A rule on an underlying fires when a call becomes fillable at an effective purchase cost at or below the underlying price minus the margin, and not again for the same contract in the same session.
- [~] AC-20 (implemented: no observation without a current book or a fillable call, and a 20-minute maximum age; no automated test): A rule does not fire on a book that is not current, outside the session, or when the quantity is not fillable.
- [-] AC-21 (out of scope by the owner's decision of 2026-11-28, OQ-186: API only; deferred to `specs/backlog.md`): The rule can be created from the card and from a question, appears in the tracker list, and can be paused and removed; plan limits apply.

All steps

- [~] AC-22 (flag-off E2E and the related unit and integration groups pass; the full integration suite was not rerun): Routing, payloads and credits of existing capabilities are unchanged with the flag off and on (existing routing regression tests pass).
- [ ] AC-23 (owner, real environment): with Feature 167 polling on, «اختیار خرید اخابر» returns a card whose figures match the option chain endpoint for the same moment.

## Deviations From The Approved Plan (2026-11-28)

- **Follow-up button** (requirement 20) not built: it would replace one of the four existing buttons under single-symbol answers. Approved by the owner on 2026-11-28 (OQ-185).
- **Discount alert** (requirements 21 to 24): the rule type and its evaluation exist; creation from the card or a question, a tracker list entry and a dedicated notification text do not; the owner approved API-only creation and management on 2026-11-28 (OQ-186). The alert measures the discount in percent of the share price (`OPTION_CALL_DISCOUNT_PCT`), because tracker thresholds are constants: "cost at or below price minus margin" is "discount ≥ margin %". Evaluation is on the tracker timer, and repeats follow the rule's cooldown.
- **Outcomes without data** (requirement 16): an unknown symbol gets the existing unknown-symbol answer from the dialogue gate; "provider data never synchronized" has no sentence of its own and reads as "no listed options".
- **Order book not current** (requirement 9): rows are reported with state `NoOrder` and no measures; the answer-level flag tells clients why.
- **Read endpoints**: `GET /api/v1/market/options/underlyings` was added so the menu item can be limited to symbols with options.
- **Phone starter icon**: the option card reuses the valuation icon; no new icon was drawn.
- Option enums are serialized as strings, also on the Feature 167 chain and ranking endpoints (`side`, ineligibility reason), which are not released yet.

## Assumptions

- The fee rates of the named source apply to every listed option on both exchanges; no cap or minimum exists. They are configuration, so a correction needs no release.
- The underlying's last price from `LatestMarketQuotes` is the reference price for every measure.
- Days to expiry count calendar days from the trading date of the underlying's quote to the expiry date.
- OQ-177, OQ-178 and OQ-179 are implemented as the agent proposed unless the owner answers otherwise at Gate B; the risk-free rate has no default value, so step 4 columns stay absent until the owner sets it.
- Conditional trackers can carry a rule whose subject is an underlying with a computed measure; task 7.1 confirms this before step 5 is built.
