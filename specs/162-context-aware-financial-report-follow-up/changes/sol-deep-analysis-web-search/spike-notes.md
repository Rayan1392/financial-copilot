# T00 — Provider spike notes

Status: partial — blocked on provider credit (2026-11-20)

Environment: Development key (`OPENAI_API_KEY`), `https://api.openai.com/v1/`, Responses API. No production
configuration was used or changed. No user data was sent; the probe prompt was a generic question about the cement
industry.

## Results

| Call | Result |
|---|---|
| `GET models/gpt-6.2-sol` | 200; `owned_by: system`, `shutdown_date: null`. The model is reachable with the development key. |
| `reasoning.effort = none` | 400 `unsupported_value`: "Supported values are: 'low', 'medium', 'high', 'xhigh', and 'max'." Confirms design-proposal §9: the lowest effort is `low`. |
| json_schema without tools | 429 `insufficient_quota` / `credit_balance_exhausted` |
| json_schema + `web_search` (`search_context_size: medium`, `user_location.country: IR`, `include: web_search_call.action.sources`) | 429 `credit_balance_exhausted` |
| `max_tool_calls` 1 | 429 `credit_balance_exhausted` |
| `max_tool_calls` 2 | 429 `credit_balance_exhausted` |
| `reasoning.effort = medium` + web | 429 `credit_balance_exhausted` |

Latency of the rejected calls: 7–9 s (request validation before the credit check; no model work).

The probe script is reproducible (kept outside the repository; it reads the key from the environment and prints no
secret). Re-run it after the development organisation has credit and complete the open rows below.

## Still open (needs credit on the development organisation)

- Compatibility of `web_search` with `text.format = json_schema` (strict) in one response.
- Count of `web_search_call` items under `max_tool_calls` 1 and 2, including `open_page` / `find_in_page` actions.
- `usage.output_tokens_details.reasoning_tokens` at `low` and `medium`.
- Shape of `web_search_call.action.sources`.
- Latency and actual cost of each tier.

## Decision for T02 while the row is open

The implementation follows the design: in web mode the request carries both `tools: [web_search]` and the strict
`json_schema` format, and the reply schema carries `externalSources`. The transport keeps the existing
"first `output_text`" rule, so `web_search_call` items before the message do not change parsing.

If the completed spike shows the combination is rejected or unreliable, the fallback is local to the transport and
the two Sol models (no contract or billing change):

- Web mode sends no `text.format`; the instructions ask for exactly this tagged plain text:

  ```text
  <answer>
  …Persian answer with [Wn] markers…
  </answer>
  <sources>
  W1 | https://… | title | YYYY-MM-DD
  </sources>
  <score>NN|null</score>
  ```

- The model adapter parses the tags into the same `ReportDiscussionAnalysis_v1` / `SymbolGroundedFallbackAnalysis_v1`
  records; a missing `<answer>` tag is treated as `InvalidStructuredOutput` (no charge), like a schema failure today.

Prices are out of scope of the spike.
