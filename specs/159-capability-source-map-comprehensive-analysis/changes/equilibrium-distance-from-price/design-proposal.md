# Design Proposal: equilibrium card distances measured from today's price

Status: Gate A: Approved by the owner on 2026-11-28 («موافقم نباید معنای وعده بده با گیت A و B موافقم»; OQ-164 to OQ-167 answered as proposed, with the condition that the wording must not read as a promise)

## Feature Folder

`159-capability-source-map-comprehensive-analysis/changes/equilibrium-distance-from-price`

## Layers / Area

backend · frontend · telegram-gateway (text only) · AI / FinancialData. No database change.

## 1. Request and goal

Owner, 2026-11-28, with the card of گدنا (price 12,800; lower 5,537; midpoint 17,055; upper 28,573): the tile «تا میانهٔ
بازه −۲۴٫۹٪» divides by the midpoint. A shareholder thinks the other way: how much must the price rise to reach the
midpoint, or the ceiling. That is (17,055 − 12,800) ÷ 12,800 = 33.2%. The denominator must be today's price, also
when the price is above the level (how much must it fall). A plus or minus sign does not carry this meaning; a very
short phrase must. The owner left the wording to the agent.

## 2. Discovery

- `ValuationGaugeProjector.ProjectEquilibrium` computes two insights, both divided by the level:
  `PriceVsMidpointPercent = (price − midpoint) ÷ midpoint` and `PriceVsLowerPercent = (price − lower) ÷ lower`.
- Web card (`valuation-gauge-cards.tsx`): three tiles, «جایگاه», «تا میانهٔ بازه» (signed, note «نسبت به ‹میانه›») and
  the lower-bound tile («بالای حد پایین» signed, or «زیر حد پایین» with the absolute value, OQ-137).
- Text and Telegram (`ValuationGaugeTextComposer`): «تا میانهٔ بازه −۲۴٫۹٪، بالای حد پایین +۱۳۱٫۲٪».
- Nothing is shown for the upper bound today.
- The insights are stored in the message payload, so answers in history carry the old two figures.

## 3. Proposed behaviour

### 3.1 The figure

For each level L of the range (lower bound, midpoint, upper bound):

```text
distance to L = |L − price| ÷ price          (one decimal, half away from zero)
direction     = رشد when L > price, افت when L < price
```

It answers one question: by what percent must today's price move to reach L. No figure when the price is zero or
missing. A distance that rounds to 0.0 reads «هم‌سطح».

گدنا: حد پایین 56.7% افت · میانه 33.2% رشد · حد بالا 123.2% رشد.

### 3.2 Wording (OQ-164)

Recommended: the level is named with «تا», the direction is a word after the number, no sign.

| Tile label | Value | Note under it |
|---|---|---|
| «تا حد پایین» | «۵۶٫۷٪ افت» | «۵٬۵۳۷ ریال» |
| «تا میانه» | «۳۳٫۲٪ رشد» | «۱۷٬۰۵۵ ریال» |
| «تا حد بالا» | «۱۲۳٫۲٪ رشد» | «۲۸٬۵۷۳ ریال» |

Text and Telegram: «تا میانه ۳۳٫۲٪ رشد، تا حد بالا ۱۲۳٫۲٪ رشد، تا حد پایین ۵۶٫۷٪ افت».

Why this pair: «رشد» and «افت» are the two words the market itself uses for a price move, each is one word, and with
«تا» the phrase reads as a distance to a level («تا میانه ۳۳٫۲٪ رشد»), which is the shareholder's question.

Alternative: «۳۳٫۲٪ بالاتر» / «۵۶٫۷٪ پایین‌تر» with the note «از قیمت روز» («میانه ۳۳٫۲٪ بالاتر از قیمت روز»). It
describes where the level is and never sounds like a forecast, but it is longer and less direct.

In «جزئیات»: «درصدها نشان می‌دهند قیمت روز چند درصد باید رشد یا افت کند تا به هر سطح بازه برسد؛ پیش‌بینی و توصیهٔ
سرمایه‌گذاری نیست.»

### 3.3 Which levels (OQ-165)

Recommended: all three, in axis order, after «جایگاه» (four tiles; two rows of two on a narrow card). One rule for
every position of the price, and it covers the ceiling the owner asked about.

Alternative: three tiles as today, the midpoint plus one bound chosen by position. Fewer tiles, but a rule the user
has to guess.

### 3.4 What it replaces

- The tile «تا میانهٔ بازه» (signed, divided by the midpoint) and the lower-bound tile, including «زیر حد پایین» of
  OQ-137: a price below the lower bound now reads «تا حد پایین ۱۲٫۳٪ رشد».
- «جایگاه», the bar, the needle, the quote line, the source line, credits and routing do not change.
- Colour: «رشد» keeps the cool tone and «افت» the warm tone that the signed midpoint figure has today (OQ-166 if the
  owner prefers no colour).

### 3.5 Stored answers (OQ-167)

Recommended: the payload gains three optional figures (additive, version 1); the old two stay in new payloads so an
older client keeps working. A card stored before this change has only the old figures and keeps its old tiles.
Alternative: the web card recomputes the new figures for old cards from the stored range and price, so history looks
the same as new answers, at the cost of the same formula in two places.

## 4. Architecture

- `ValuationGaugeInsights` gains `LowerDistancePercent`, `MidpointDistancePercent`, `UpperDistancePercent`: signed
  invariant strings, positive when the level is above the price. The sign is the direction; the words are chosen by
  the presenters.
- `ValuationGaugeProjector.ProjectEquilibrium` computes them (pure; guards price ≤ 0).
- `ValuationGaugeTextComposer` and the web card render the wording; a shared helper per side turns a signed figure
  into «‹عدد›٪ رشد/افت» or «هم‌سطح».
- No reader, configuration, migration, billing or telemetry change.

## 5. Testing

- Projector: price below the range, in each half, at the midpoint, above the range; price zero; rounding; گدنا's
  numbers as a fixture (56.7 افت, 33.2 رشد, 123.2 رشد).
- Composer and web card: wording for each direction and for «هم‌سطح»; a stored card without the new figures.
- Existing equilibrium tests updated only where they assert the replaced tiles. No live provider or LLM call.

## 6. Risks

- **«رشد» read as a forecast.** Mitigation: «تا», the sentence in «جزئیات», the existing disclaimer; the alternative
  wording avoids the word.
- **Two looks in one conversation** (old answers keep the old tiles) unless OQ-167 chooses recomputation.
- **Reverses OQ-137** («زیر حد پایین»), an owner decision of 2026-11-26. Approving this proposal approves that.

## 7. Open questions (owner decisions)

- **OQ-164** wording: «تا میانه ۳۳٫۲٪ رشد» / «… افت» (recommended), or «۳۳٫۲٪ بالاتر / پایین‌تر از قیمت روز».
- **OQ-165** levels: all three bounds as tiles (recommended), or the midpoint plus one bound.
- **OQ-166** colour of the figure: keep cool for «رشد» and warm for «افت» (recommended), or neutral.
- **OQ-167** stored answers: keep their old tiles (recommended), or recompute on the web card.
