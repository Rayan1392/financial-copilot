# Feature 148 — Live contract verification (T148-00.3)

Read-only calls against the production CyclicalWaves v2 provider on **2026-10-31** with the account supplied by the
Product Owner. Tokens and credentials were never printed or stored; samples below are trimmed. The same checks are
automated in `tests/FinancialCopilot.IntegrationTests/CyclicalWavesV2LiveContractTests.cs` (opt-in,
`CYCLICALWAVES_V2_RUN_LIVE=1`), which passed through the production client pipeline on 2026-10-31.

| # | Request | Status | Evidence |
|---|---|---|---|
| 1 | `POST https://auth.cycfx.com/api/auth/login` (`x-client-id: default`, `{identifier,password}`) | 201 | keys `access_token`, `refresh_token`, `used_secondary_password=false` |
| — | access-token JWT claims (decoded, not stored) | — | `exp − iat = 900 s` (**15 minutes**; the screener PDF says 5, the statement PDF says 15). The refresh token is also a JWT, lifetime 604 800 s (7 days). |
| 2 | `POST …/auth/refresh` (`{refreshToken}`) | 201 | keys `access_token`, `refresh_token` (rotated) |
| 3 | `GET /custom-filtering/tickers` | 200 | `string[]`, **683** tickers; some contain spaces (`"آ س پ"`) |
| 4 | `GET /custom-filtering/ticker/شلرد` | 200 | 29 documented fields + `id, createdAt, updatedAt, version`; `pe = 0`, `lastQuarterDate = "20260922"` |
| 4b | `GET /custom-filtering/ticker/بیمهxxx` | 404 | `{"statusCode":404,"message":"Ticker \"…\" not found"}` |
| 5 | `GET /income-statements/ticker/فولاد?limit=4` | 200 | `total = 52`; statements carry undocumented `isFiscalYearChanged`, `actualFiscalMonth`, `correctedByHand` (ignored); 16 lines for an industrial company incl. `6220` (adjusted EPS); key 86 `trailingYear = null` |
| 5b | `GET /income-statements/ticker/وبملت?limit=4` | 200 | bank lines with their own keys (`113, 593, 6271, …`); key `111` `quarter = null` |
| 5c | `GET /income-statements/ticker/فولاد?limit=0` | 400 | ``{"statusCode":400,"message":"`limit` must be a whole number of at least 1."}`` |
| 6 | `GET /income-statements/lines` | 200 | **resolves B2:** a bare JSON array of **82** `{key, name, nameFa}` objects |
| 7 | `GET /blogs?filter[type]=analysis&page=1&paginate=100` | 200 | `meta.total = 17 250`, `last_page = 173`; items carry extra fields (`slug, type, views_count, required_plans, …`, ignored) |
| 7b | `GET /blogs?…&paginate=5&filter[from_date]=2026-10-20` | 200 | `meta.total = 53`; **`YYYY-MM-DD` confirmed** |
| 7c | `GET /blogs?…&filter[from_date]=bad` | 400 | `{"statusCode":400,"message":"تاریخ باید به شکل YYYY-MM-DD باشد"}` |
| 8 | any data call with an invalid bearer token | 401 | `{"statusCode":401,"message":"Unauthorized"}` |

## B3 resolution (analysis linkage)

- **Categories are authors**, not topics: the 100-item sample had only `نیما آزادی` (86) and `اشکبوس شرفی` (14).
- **Topics are tags** with underscores: `تحلیل_تکنیکال`, `رصد_معاملات_عمده`, `گزارش_فصلی`, `P-E`, `P-S`, … as documented.
- **Most articles have no tags at all**; the ticker is the leading title segment, separated by `-`, `؛` or nothing:
  `«شتوکا- گزارش عملکرد مهر 1405»`, `«فاذر- تکنیکال»`, `«اخابر؛»`, `«وبملت»`. Some tags are tickers (`فولاد`, `چخزر`).
- 2 of 100 summaries were empty, matching the documented "older posts have no summary".

The retrieval rule (design §10.4) is therefore: an article matches a ticker when a tag's normalized name equals the
normalized ticker **or** the normalized leading title segment (before `-`, `–`, `—`, `؛`, `:`, `|`) equals it.

## Interpretation notes

- `publishDate` (`"2026-10-22 14:37:13"`) has no offset; it is read as Iran Standard Time (UTC+03:30).
- `fiscalMonth` for a March-ending fiscal year: `20260922 → 6`, `20260320 → 12`; the quarterly table labels the fiscal
  year by the Jalali year in which the fiscal year ends.
- Field set and units match tahlilapp-api-client.pdf §3.2; all money is IRR, margins are percentages.
