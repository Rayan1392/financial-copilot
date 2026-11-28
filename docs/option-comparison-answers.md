# Option Comparison Answers (Feature 168)

Assistant answers that compare the listed options of one share: calls bought for exercise, puts
bought for exercise, and calls written against a held share (covered call). The answer compares;
it never names a best position. Spec: `specs/168-option-comparison-answers/`.

Data comes from Feature 167 (`docs/tsetmc-webservice-datasource.md`): stored option contracts,
open interest and the five-level order book. The answer path reads stored data only; it never
calls the provider and makes no model call.

## Questions

Matched by `OptionComparisonIntentRules` in the V2 dialogue gate, before generic symbol routing.

| Wording | View |
|---|---|
| «اختیار خرید اخابر», «اختیار معامله اخابر», «آپشن‌های اخابر», «زنجیره اختیار اخابر» | Call |
| «اختیار فروش اخابر» | Put |
| «کاورد کال اخابر», «فروش اختیار خرید اخابر» | Covered call |
| «اختیار خرید ضمخا9021» (a contract symbol) | The contract's side and expiry, its row marked |

Optional in the question: «۵ قرارداد» (quantity, 1 to 100,000), «سررسید دی» (Jalali month),
«سررسید 1405/09/18», «نزدیک‌ترین سررسید». Defaults: one contract, the three nearest expiries.

Not matched, so they keep their own route: advice wording («بخرم», «توصیه», «سیگنال»,
«پیشنهاد»), analysis requests, alert requests («خبر بده», «هشدار»), several symbols.

## Measures

Per share, with fees, for the requested quantity. Definitions are in
`OptionComparisonPolicy`.

- **Fill price**: volume-weighted price over order-book levels 1 to 5; ask side for a buyer, bid
  side for a writer. A row is `Fillable`, `NotFillable` (orders exist but fewer contracts than
  asked) or `NoOrder`.
- **Call**: effective purchase cost = strike × (1 + exercise fee) + fill ask × (1 + trade fee);
  break-even distance from the underlying price; intrinsic value; time value; implied annual rate
  = time value ÷ strike × 365 ÷ days, only for in-the-money calls; leverage = underlying price ÷
  fill ask; cash now and cash at expiry.
- **Put**: effective sale price = strike × (1 − exercise fee − transfer tax) − fill ask × (1 +
  trade fee); distance from the underlying price; protection cost.
- **Covered call**: premium income = fill bid × (1 − trade fee); yield if exercised and if not
  exercised, also annualized; downside cushion.
- **Annualized figures** appear only with at least `AnnualizationFloorDays` (3) days to expiry.
- **Highlights**, only when two or more rows are fillable: lowest effective purchase cost and
  lowest implied annual rate (calls), highest effective sale price (puts), highest annualized yield
  if exercised (covered call). Ties go to the larger fillable volume, then the symbol.
- **Theoretical price and implied volatility**: Black–Scholes (European, no dividends) with the
  configured risk-free rate and the underlying's historical volatility (daily log returns over
  `VolatilityWindowDays`, annualized with `TradingDaysPerYear`). Absent when the rate is not
  configured, fewer than 20 returns exist, or expiry is today.

## Fees

Source named by the owner (OQ-172): optionbaaz.ir, «کارمزد معاملات اختیار معامله», updated
1405/07/14. A secondary source; it gives no caps or minimums.

| Fee | Default | Base | Paid by |
|---|---|---|---|
| Trade | 0.001 | premium | each side of every option trade |
| Exercise | 0.0005 | strike | both sides of an exercised contract |
| Transfer tax | 0.005 | strike | the side delivering a share at exercise; none for fund units |

The underlying is a fund unit when its ISIN starts with `IRT`, a share when it starts with `IRO`
(OQ-177); anything else is treated as a share and the answer says so.

## Freshness

Measures that need the order book are computed only when the last successful order-book sync is
not older than the trading session of the underlying's latest quote. Otherwise rows carry the last
trade price and open interest only and the answer is marked "order book not current".

## Outcomes without a comparison (not charged)

Unknown symbol → the existing unknown-symbol answer. Symbol without unexpired listed options, an
expiry that is not listed, or no stored underlying price → one sentence each. An answered
comparison with rows costs one credit (`OptionComparisonAnswered`, OQ-173).

## Channels

- **Web**: `OptionComparisonCard` (desktop chat and `/m`), from the `optionComparison` payload on
  the assistant response and on persisted messages (`OptionComparison`, versioned).
- **Telegram and text fallback**: `OptionComparisonTextComposer` — the asked view, nearest expiry,
  at most eight fillable rows nearest to the money.
- **API**: `GET /api/v1/market/options/comparison?underlying=&view=&expiry=&quantity=` returns the
  same payload without a chat turn; `GET /api/v1/market/options/underlyings` lists symbols with
  listed options.
- **Entry points**: starter card «اختیار خرید نماد», and a symbol-menu item for symbols that have
  listed options. Both are hidden while the capability is unavailable. The starter card also needs
  `OptionAnswers:StarterCardEnabled` (change `option-starter-card-switch`, default off); typed
  questions and the symbol-menu item do not depend on it. The availability endpoint
  (`GET /api/ai/v1/capabilities/availability`) reports it as `optionComparisonStarterCard`.

All Persian labels live in `OptionComparisonWording` (backend) and
`src/frontend/src/lib/chat/option-comparison.ts` (web). Neither may contain «بهترین», «مناسب»,
«پیشنهاد» or «فرصت»; tests check both.

## Discount alert

Conditional tracker rule type `OptionDiscount`, governed code `OPTION_CALL_DISCOUNT_PCT`, unit
percent. The observed value is the largest discount at which one contract of any call of the
company can be bought and exercised: (underlying price − effective purchase cost) ÷ underlying
price × 100, with fees and at the fillable ask. A rule such as "≥ 0" fires when a call prices the
share at or below the market.

There is no observation, and so no trigger, while `OptionAnswers:Enabled` is off, the company has
no fillable call, or the order book is not current; an observation older than 20 minutes is stale.
Evaluation runs on the tracker worker's timer; cooldown, re-arm and plan limits are those of every
tracker rule. The trigger's evidence names the contract, expiry, strike, ask, effective purchase
cost, underlying price and fillable volume.

Today the rule can only be created through `POST /api/v1/trackers/me`; there is no web or chat
path and no dedicated notification text; the owner decided on 2026-11-28 to keep it API-only
until trackers get a web surface (OQ-186).

## Configuration (`OptionAnswers`)

| Key | Default | Meaning |
|---|---|---|
| `Enabled` | `false` | Master switch of the capability |
| `StarterCardEnabled` | `false` | Shows the «اختیار خرید نماد» new-chat card; only with `Enabled` on |
| `TradeFeeRate`, `ExerciseFeeRate`, `ShareTransferTaxRate` | 0.001, 0.0005, 0.005 | Fee rates, each 0 to 0.05 |
| `AnnualizationFloorDays` | 3 | Minimum days to expiry for annualized figures |
| `MaxExpiries` | 3 | Expiries per answer when none is asked |
| `RiskFreeAnnualRate` | none in code; 0.415 in `appsettings.json` and `docker-compose.yml` | Effective annual rate (OQ-178: compounded yearly yield of fixed-income funds) |
| `VolatilityWindowDays`, `TradingDaysPerYear` | 90, 240 | Historical volatility inputs |

Compose variables: `OPTION_ANSWERS_ENABLED`, `OPTION_ANSWERS_STARTER_CARD_ENABLED`,
`OPTION_ANSWERS_RISK_FREE_ANNUAL_RATE`. A change takes effect when the API restarts.

Rollout: Feature 167 polling switches on and one session checked; then `OptionAnswers:Enabled`.
Rollback: turn `OptionAnswers:Enabled` off; stored answers stay readable.
