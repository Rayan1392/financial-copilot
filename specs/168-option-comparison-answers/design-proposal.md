# Option Answers: Call Comparison, Put Comparison, Covered Call and Discount Alert — Design Proposal

**Status: Gate A approved by the owner on 2026-11-28**, with one amendment: the items first listed
as "not in this feature" (discount alert, covered-call yield comparison, put comparison, quantity
and expiry in the question, theoretical price and implied volatility) are part of this feature.
The owner accepted the agent's proposals for OQ-172 to OQ-176 and named the fee source.
Gate B (implementation plan in `tasks.md`) not approved. Drafted 2026-11-28.

## Layers / Area

backend, frontend, telegram-gateway (text only) · AI, FinancialData, Notifications

## Depends On

`167-option-chain-data-and-exercise-ranking` — data, option chain reader and ranking policy are on
`develop` (`aae83fef`), not released. This feature shows nothing useful until the two option
polling switches of Feature 167 are on in the target environment.

## Goal

Let a user ask the assistant about the options of a share and get a comparison card that answers
the question an option trader actually has:

- **buyer of a call**: what does it cost to own the share through each call, how much cash now and
  at expiry, how liquid is the offer;
- **buyer of a put**: at what price can the share be sold through each put;
- **holder of the share**: what does writing each call against it yield;
- and be told when a call lets the share be bought below its market price.

The answers compare. They do not name a "best" or "suitable" position.

## Why the Feature 167 ranking is not enough

It ranks calls by effective purchase cost (strike + best ask). That measure almost always puts the
deepest in-the-money call first, because its time value is smallest. The decision is a trade-off
between three things the ranking does not show: cash paid now against cash due at expiry; the price
of that deferral (time value relative to the deferred amount, annualized); and the maximum loss.

Illustration with the order book stored on 2026-11-28 and an assumed underlying price of 1,489
(the figure from the owner's screenshot, not a live quote), 11 days to expiry, without fees:

| Call | Pay now | Pay at expiry | Effective cost | Time value | Implied annual rate |
|---|---|---|---|---|---|
| ضمخا9013 | 1,024 | 500 | 1,524 | 35 | about 230% |
| ضمخا9021 | 544 | 1,000 | 1,544 | 55 | about 180% |

## Fees (OQ-172, answered)

Source named by the owner: optionbaaz.ir, «کارمزد معاملات اختیار معامله» (published 1400/09/04,
last updated 1405/07/14), read on 2026-11-28. It is an educational article, not the exchange's
notice; it cites articles 13 and 38 of «دستورالعمل معاملات قرارداد اختیار معامله» and states that
its figures hold for option contracts on both the Tehran Stock Exchange and Iran Fara Bourse.

| Fee | Rate | Base | Who pays |
|---|---|---|---|
| Trade (buying or selling the option) | 0.001 | premium × contract size | each side, on every trade |
| Exercise | 0.0005 | strike × contract size | both sides of an exercised contract; nothing if the contract is not exercised |
| Transfer tax | 0.005 | strike × contract size | only on physical settlement, only the side that delivers the underlying, only when the underlying is a share; not for tradable fund units |

The page gives no cap, minimum or component breakdown. One sentence in its notes says "only the
seller pays the tax", while its detailed sections say "the deliverer"; this proposal follows the
detailed sections: the call writer delivers on a call exercise, the put holder delivers on a put
exercise.

The rates are configuration with these defaults, not constants. Whether an underlying is a share
or a fund unit must come from the instrument; how to tell is a planning item (OQ-177).

## Proposed design

### 1. Measures (domain)

All measures are per share, for a requested quantity in contracts, with fees.

**Fill price.** Volume-weighted price over order-book levels 1 to 5 for the requested quantity:
ask side for a buyer, bid side for a writer. If the five levels cannot fill it, the row is "not
fillable" and shows the available volume. The option chain reader must return all five levels
(today it reads level 1).

**Call bought for exercise**

| Measure | Definition |
|---|---|
| Effective purchase cost | strike × (1 + exercise fee) + fill ask × (1 + trade fee) |
| Break-even distance | (effective purchase cost − underlying price) ÷ underlying price |
| Intrinsic value | max(underlying price − strike, 0) |
| Time value | fill ask − intrinsic value |
| Implied annual rate | time value ÷ strike × 365 ÷ days to expiry; only for in-the-money calls with at least 3 days to expiry (OQ-174) |
| Leverage | underlying price ÷ fill ask |
| Cash now / cash at expiry | fill ask × size × quantity plus trade fee; strike × size × quantity plus exercise fee |
| Maximum loss | cash now |

**Put bought for exercise** (holder delivers the share)

| Measure | Definition |
|---|---|
| Effective sale price | strike × (1 − exercise fee − transfer tax if the underlying is a share) − fill ask × (1 + trade fee) |
| Distance from market | (effective sale price − underlying price) ÷ underlying price |
| Protection cost | fill ask × (1 + trade fee) ÷ underlying price, and annualized by days to expiry under the OQ-174 rule |

**Covered call** (holder of the share writes the call)

| Measure | Definition |
|---|---|
| Premium income | fill bid × (1 − trade fee) |
| Yield if exercised | (strike × (1 − exercise fee − transfer tax if share) + premium income − underlying price) ÷ underlying price, and annualized |
| Yield if not exercised | premium income ÷ underlying price, and annualized |
| Downside cushion | premium income ÷ underlying price: how far the share can fall before the position loses against today's price |
| Required margin | not computed in this feature; the note says the share must be held and pledged as the broker requires |

**Theoretical price and implied volatility.** Black–Scholes price of each call and put and the
volatility implied by the fill price, from underlying price, strike, days to expiry, a risk-free
rate and (for the price) a historical volatility of the underlying. Shown as two extra columns.
Needs the rate and the volatility window (OQ-178). No dividend adjustment in the first version.

**Highlights.** Two neutral labels per expiry and view, each only when at least two rows are
fillable: calls — lowest effective purchase cost and lowest implied annual rate; puts — highest
effective sale price; covered call — highest annualized yield if exercised. No overall ordering by
"quality".

The Feature 167 ranking endpoint keeps its contract.

### 2. Assistant capability (V2 path only)

New capability `option_comparison`, wired like `valuation_gauges` (Feature 159), with three views:
`call`, `put`, `covered_call`.

- **Intent rules**, deterministic, matched in the V2 dialogue gate. An option word plus one company
  token selects the capability; the view comes from the wording («اختیار خرید» / «آپشن خرید» →
  call, default; «اختیار فروش» → put; «کاورد کال» / «فروش اختیار خرید» → covered call;
  «زنجیره اختیار» → call view with puts available as a tab). Buy/sell advice wording keeps going
  to the existing refusal.
- **Quantity and expiry in the question.** A number of contracts («۵ قرارداد») and an expiry
  (month name, «نزدیک‌ترین سررسید», or a date) are parsed when present; otherwise quantity 1 and
  the nearest expiries.
- **Registration**: route constant, registry definition (gate-routed only), data requirements,
  executor in DI, capability source-map entry, flag `OptionAnswers:Enabled` (default off),
  availability exposed to the client like other capabilities.
- **Executor**: resolves the symbol with the canonical entity resolver, calls the option chain
  reader and the measures policy, returns a structured payload plus a text rendering.
- **Payload** `OptionComparison` on the assistant response and in persisted messages: view,
  underlying symbol and price with its time, quantity, expiries (at most the three nearest, or the
  requested one), per expiry the rows with the view's measures and a row state (fillable, not
  fillable, no order), the highlights, fee rates applied, order-book and open-interest observation
  times.
- **Outcomes without data** (none charged): unknown symbol → clarification; symbol without listed
  options → a plain sentence; no order-book sync in the underlying's latest session → the table
  with trade prices only and a visible "order book not current" note, without measures that need
  the book; no underlying price → a plain sentence.
- **Credits**: one credit per answered turn (OQ-173).

### 3. Presentation

- **Web card** (desktop chat and `/m`, one component): view tabs (calls, puts, covered call),
  expiry tabs, one row per strike, highlights as small labels, underlying price line, data time,
  fee rates line, and a fixed note block per view:
  - calls: exercising means paying the strike for every share at expiry (with the amount for one
    contract); if the share closes below the strike the premium is lost;
  - puts: exercising means delivering the shares; without them the put cannot be exercised
    physically;
  - covered call: the share must be held; above the strike the gain is capped at the strike;
  - all: thin orders — the price holds only for the shown volume; not investment advice (wording
    family of the gauge cards, OQ-161).
- **Telegram**: text rendering of the same table, the nearest expiry of the asked view, no image.
- **Entry points** (OQ-176): a starter card with the symbol picker (unrestricted; a symbol without
  options gets the plain sentence), an item in the symbol actions menu, and follow-up buttons under
  answered single-symbol turns for symbols that have options.
- **Wording** (OQ-175): titles «مقایسهٔ هزینهٔ خرید از مسیر اختیار خرید», «مقایسهٔ قیمت فروش از
  مسیر اختیار فروش», «مقایسهٔ بازده فروش اختیار خرید روی سهم»; highlights «کمترین هزینهٔ مؤثر»,
  «کمترین نرخ ضمنی», «بیشترین قیمت فروش مؤثر», «بیشترین بازده سالانه در صورت اعمال». The words
  «بهترین», «مناسب», «پیشنهاد» and «فرصت» are not used.

### 4. Discount alert

A user can ask to be told when a call of a symbol lets the share be bought below its market price:
effective purchase cost (with fees, for one contract unless stated) at or below the underlying's
last price by a margin the user gives (default 0).

- New rule type in the conditional tracker model (`AlertRule`), scoped to an underlying, optionally
  to one expiry. Evaluated by the existing tracker evaluation after each successful order-book
  sync, inside the session only.
- Fires once per contract per session; existing cooldown and hysteresis policies apply. The
  notification names the contract, the three prices and the data time, and repeats the notes on
  volume and on paying the strike.
- Created from the comparison card («خبرم کن») and by a question such as «وقتی اختیار خرید اخابر
  زیر قیمت سهم شد خبر بده». Listed and removed where other trackers are.
- Limits follow the existing tracker limits of the plan (OQ-179).

## Delivery order

1. Measures with fees and five-level fill; call view; assistant capability, card, Telegram text,
   entry points.
2. Put view and covered-call view.
3. Quantity and expiry from the question.
4. Theoretical price and implied volatility.
5. Discount alert.

Each step is usable on its own and stays behind `OptionAnswers:Enabled` until the owner turns it on.

## Alternatives considered

- **Expose the Feature 167 ranking as it is.** Rejected: one ordering by strike + ask hides the
  cash and leverage trade-off and reads as a recommendation.
- **A dedicated option-chain page instead of an assistant answer.** Deferred: more frontend work,
  and the assistant is where users ask symbol questions today. The card can later be reused.
- **Compute everything in the frontend from the chain endpoint.** Rejected: the measures and fees
  are business rules and must be one implementation for web, Telegram, API and alerts.
- **A separate capability per view.** Rejected: the three views share symbol resolution, data,
  freshness rules, card frame and billing.

## Risks

- **Stale or missing order book.** Outside the session, on a provider failure, or with polling
  off, the book is old. Answers show data time and fall back as described; no measure is computed
  from a book older than the underlying's latest session. Alerts are evaluated only on a fresh book.
- **Thin books.** 923 of 1,794 contracts had a level-1 ask on 2026-11-28. Rows without orders are
  collapsed; a card may be mostly empty for some symbols.
- **Fee source.** A secondary source with one internal inconsistency and no caps. Rates are
  configurable; the card states the rates it used.
- **Share or fund.** The transfer tax depends on it; a wrong classification shifts put and
  covered-call figures by 0.5% of the strike (OQ-177).
- **Advice perception.** Highlights and alerts can read as recommendations. Mitigated by neutral
  wording, labels instead of a rank, and the note blocks.
- **Annualized figures on short expiries** are large; hidden below 3 days (OQ-174).
- **Theoretical price** depends on a rate and a volatility estimate that are assumptions; shown as
  "theoretical", with the inputs stated, never as a target.
- **Alert noise.** A thin, briefly crossed book can fire an alert the user cannot trade on. The
  rule requires fillable volume for the stated quantity; cooldown limits repeats.
- **Size.** Five delivery steps across backend, frontend, Telegram and notifications; larger than
  the first draft. The order above lets the owner stop after any step.

## Open questions

Answered with Gate A (2026-11-28): OQ-168 and OQ-169 of Feature 167 (settled by this design),
OQ-172 (fees included, source above), OQ-173, OQ-174, OQ-175, OQ-176 (agent's proposals accepted).

Raised by the widened scope, for Gate B: OQ-177 (share or fund), OQ-178 (rate and volatility for
the theoretical price), OQ-179 (alert limits and default margin) in `specs/open-questions.md`.

## References

- `specs/167-option-chain-data-and-exercise-ranking/` (user story, tasks, verification record)
- `specs/159-capability-source-map-comprehensive-analysis/changes/standalone-valuation-gauge-cards/design-proposal.md` — model for a gate-routed card capability
- `specs/POLICY-V1-FREEZE.md`
- `docs/domain-glossary.md` — "Option Market (Feature 167)"
- `docs/tsetmc-webservice-datasource.md`
- Fee source: https://optionbaaz.ir/pedia/کارمزد-معاملات-اختیار-معامله-57
- Code: `ValuationGaugeIntentRules`, `ConversationalCapabilityContracts` (`CapabilityExecutionRoutes`), `ChatCapabilityAvailability`, `ValuationGaugeCapabilityExecutor`, `TelegramAssistantResponseRenderer`, `src/frontend/src/lib/chat/starter-prompts.ts`, `src/frontend/src/components/app/valuation-gauge-cards.tsx`, `RelatedCapabilityFollowUpProviders`, `AlertRule`, `AlertRuleGovernance`, `EfCoreOptionChainReader`, `CallExerciseRankingPolicy`
