# CyclicalWaves v2 Gauge and Equilibrium API Contract

**Authority:** [`gauge-equilibrium-api-client.pdf`](./gauge-equilibrium-api-client.pdf), supplied by the Product Owner. The PDF is the contract; this file transcribes it for implementation planning. No live provider request has been made for this extension.

**Document date:** 1405 Aban 13 (2026-11-04).

## Hosts and authentication

| Purpose | Base URL |
|---|---|
| Authentication | `https://auth.cycfx.com/api` |
| Gauge/equilibrium data | `https://api.tahlilapp.com/api` |

An active account is sufficient; the document says no subscription is required. Each data request sends:

```http
Authorization: Bearer ACCESS_TOKEN
```

Login:

```http
POST /auth/login
x-client-id: default
Content-Type: application/json
```

```json
{"identifier":"USERNAME","password":"PASSWORD"}
```

The response contains `access_token` and `refresh_token`.

Refresh:

```http
POST /auth/refresh
x-client-id: default
Content-Type: application/json
```

```json
{"refreshToken":"REFRESH_TOKEN"}
```

Access tokens last 15 minutes. Every refresh returns a new `refresh_token`; always replace the prior
one. The prior refresh token is accepted for two additional minutes only. Reusing it after that
window invalidates the login. Existing Feature 148 authentication must own refresh and credentials;
no second credential set is defined here.

## 1. Gauge ticker catalog

```http
GET /pe-ps-gauges/tickers
```

Returns symbols with at least one available gauge, alphabetically:

```json
[
  {"ticker":"آباد","enticker":"IRO1ABAD0001","pe":true,"ps":true}
]
```

| Field | Meaning |
|---|---|
| `ticker` | Persian symbol. |
| `enticker` | ISIN. |
| `pe` | Whether a P/E gauge exists. |
| `ps` | Whether a P/S gauge exists. |

Persist both flags. The catalog is a provider listing, not permission to create a new Feature 148
ticker master row.

## 2. Gauge details

```http
GET /pe-ps-gauges/ticker/{ticker}
```

The `ticker` path segment may be an URL-encoded Persian symbol or an ISIN. ISIN matching is
case-insensitive. Arabic Yeh and Kaf are accepted as well as Persian Yeh and Kaf. A symbol absent
from the relevant ticker list returns 404. Encode the canonical request identifier once.

Example:

```json
{
  "ticker":"فولاد",
  "enticker":"IRO1FOLD0001",
  "pe":{
    "a":414,"b":848,"c":1490,"d":851,"e":534,"f":506,
    "close":5.9751,"start":2.2886,"end":38.8073,
    "min":3.4315,"avg":5.8271,"max":9.4010,"status":3
  },
  "ps":{
    "a":468,"b":933,"c":1021,"d":1034,"e":659,"f":529,
    "close":1.7531,"start":0.5432,"end":12.2804,
    "min":0.9774,"avg":1.7614,"max":2.7264,"status":2
  }
}
```

`pe` or `ps` can be `null` when that gauge does not exist. In a present gauge, `close` can be
`null`, for example when the company is loss-making or has not traded for over 30 days. Do not
convert nulls to zero.

| Field | Meaning |
|---|---|
| `a` through `f` | Number of historical days in each of six gauge segments from lowest to highest. These are counts, not ratios. |
| `close` | Most recent P/E or P/S ratio used to construct the gauge; it is not the live share price. |
| `start`, `end` | Historical minimum and maximum defining the gauge axis. |
| `min` | Mean of the lowest fifth of historical values. |
| `avg` | Historical mean. |
| `max` | Mean of the highest fifth of historical values. |
| `status` | Relative location of `close`; see below. |

| `status` | Interpretation |
|---:|---|
| `1` | `close` below `min` (lowest/cheapest state). |
| `2` | `close` between `min` and `avg`. |
| `3` | `close` between `avg` and `max`. |
| `4` | `close` above `max` (highest/most expensive state). |
| `null` | `close` is unavailable or lies exactly on a boundary. |

The six day-count segments use the `min`, `avg`, and `max` boundaries and the `start`/`end` axis.
If only an inexpensive/expensive label is required, `status` is sufficient; do not infer or
reconstruct a different status formula.

## 3. Equilibrium ticker catalog

```http
GET /equilibrium-ranges/tickers
```

Returns symbols with an available equilibrium range, alphabetically:

```json
[
  {"ticker":"آباد","enticker":"IRO1ABAD0001"}
]
```

Fields are `ticker` (Persian symbol) and `enticker` (ISIN).

## 4. Equilibrium range details

```http
GET /equilibrium-ranges/ticker/{ticker}
```

Ticker identity and encoding rules are the same as for gauge details.

```json
{"min":3236.70,"avg":7232.17,"max":11227.65}
```

| Field | Meaning |
|---|---|
| `min` | Lower equilibrium bound in IRR. |
| `avg` | Equilibrium price in IRR; the midpoint of `min` and `max`. |
| `max` | Upper equilibrium bound in IRR. |

The provider says all three values can sometimes be negative for a loss-making company; negative
values are valid and are not an error. The sample demonstrates two fractional decimal places, but
the PDF does not set a precision, scale, rounding rule, or maximum magnitude.

## Freshness and errors

Gauge values are rebuilt when the provider rebuilds a gauge; `close` is a ratio observation, not an
intraday quote. Equilibrium ranges are calculated from financial statements and generally change
when new statements are published. One read per day is sufficient according to the PDF.

| HTTP status | Documented meaning |
|---:|---|
| `401` | Token is missing, invalid, or expired; renew it. |
| `404` | Symbol is absent, or it has no gauge/equilibrium range. |

The PDF does not define response contracts for other HTTP statuses, retry limits, rate limits,
catalog pagination, or maximum numeric precision. Do not invent those as provider guarantees. The
integration proposal reuses the existing Feature 148 bounded retry and pacing behavior; any
non-401/404 interpretation remains an implementation policy, not part of this API contract.

## Feature 148 extension disposition

Implementation status: authorized by the Product Owner on 2026-11-08 and in progress. The routes,
latest-state schema and sync integration are implemented in source; they have not been live-verified
or applied to a database.
The data host is configured separately as `CyclicalWavesV2:GaugeBaseUrl`, defaulting to this PDF's
data host; the existing `DataBaseAddress` is unchanged. Both clients share Feature 148 credentials,
account and token lifecycle. Storage uses unconstrained PostgreSQL `numeric` and latest state only;
exact .NET parsing/serialization must be verified because this PDF does not specify a maximum scale or
magnitude and .NET `decimal` is bounded.

P/E gauge, P/S gauge and equilibrium range are approved for Chat only in CyclicalWaves mode. Chat
preserves nulls, distinguishes current close from historical bounds and axis, treats a-f as day counts,
reports equilibrium values in IRR, and does not present a guaranteed future price or recommendation.
NoavaranAmin never reads the new tables. Only Feature 115 standalone P/S gauge is proposed for scoped
UX restoration; its history, monthly-sales inset, LastPS/TTM and Forward P/S remain unsupported.
Features 114, 125, 126 and 127 stay disabled.
