# CyclicalWaves P/S provider contract

This contract deliberately records only evidence-backed behavior. The client sends an authenticated
server request through the existing CyclicalWaves pipeline; it does not send browser headers.

| Endpoint | Required fields | Explicit zero | No-data / limits |
| --- | --- | --- | --- |
| `ps/circle-chart-data/{symbolIsin}` | `a`–`f`, `close`, `start`, `min`, `avg`, `max`, `end` | Valid decimal/count value | 404/204 is `NotFoundOrNoData`; response bytes are bounded before JSON parsing. |
| `ps-data/{symbolIsin}` | `data.ticker`, `data.ps_ratio`, `data.close`, `data.date` | Valid for both ratios; it is never converted to missing | Returned ticker must ordinally match requested SymbolIsin after trim/case normalization. |
| `ps/{isin}` | `data[]` points with `_id`, `date`, `ps` | Valid P/S ratio | Full response only; no cursor, date range, ETag, or pagination parameter is sent or assumed. |

Decimals are parsed and persisted as fixed-precision decimal values (`numeric(28,14)`). Unknown
additive JSON fields are ignored. `date` is Gregorian `DateOnly`; `_id` is opaque, bounded to 128
characters, and is the identity key. Observation date is never a uniqueness key.

Response size defaults to 5 MiB and history length defaults to 10,000 points, both configuration
validated. An oversized, malformed, truncated, or point-limit-exceeding response is rejected before
it can activate a series. 401 is retried once by the existing authentication handler; persistent
authentication failure, 429, 5xx, network/timeout, no-data, invalid contract, and cancellation are
separate outcome codes. `Retry-After` is retained only as a bounded outcome hint.

The provider contract has not supplied pagination or conditional-request semantics. The integration
therefore always refreshes the complete history response and hashes normalized sorted points.

## Verified gauge semantics

Same-symbol شراز and غگلپا API/UI captures establish six equal-width histogram arcs. Visual order is
`a,b,c,d,e,f` from low/green to high/red. Counts determine exact percentages, while every arc remains
30 degrees. The rendered numeric axis is `min..max`, divided into six equal intervals. The needle
uses `ps-data.ps_ratio`, maps linearly over that axis, and clamps outside it. `start`, `end`, `avg`,
circle `close`, and Forward P/S remain separate provider facts.
