# User Story — Explainable Scanner Results

## Story

As an investor or SaaS API consumer,  
I want each AI answer produced by the Scanner Tool to explain why symbols matched,
so that I can trust and audit the output in my Conversation.

## Acceptance Criteria

- Each result includes matched condition details.
- Each condition includes actual value, threshold, period, and comparison basis.
- Each result includes Data Citations with source provider, report date, and last sync timestamp.
- Each result includes a Confidence Score.
- Each result includes warnings if data is stale or incomplete.
- Explanation text is concise and not a buy/sell recommendation.
- The Explainable Answer is returned through `POST /api/ai/v1/query` for both our own UI and conversational external clients.

## Technical Notes

- Numeric explanations should be generated from deterministic result data.
- LLM may polish text but must not change numbers.
- The assistant Message persists answer evidence needed for later Conversation retrieval.
