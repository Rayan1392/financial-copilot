# User Story — Explainable Scanner Results

## Story

As an investor or SaaS API consumer,  
I want every scanner result to explain why it matched,  
so that I can trust and audit the output.

## Acceptance Criteria

- Each result includes matched condition details.
- Each condition includes actual value, threshold, period, and comparison basis.
- Each result includes source provider, report date, and last sync timestamp.
- Each result includes confidence score.
- Each result includes warnings if data is stale or incomplete.
- Explanation text is concise and not a buy/sell recommendation.
- Response can be consumed by both our own UI and external clients.

## Technical Notes

- Numeric explanations should be generated from deterministic result data.
- LLM may polish text but must not change numbers.
