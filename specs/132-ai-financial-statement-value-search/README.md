# Feature 132 — AI Financial Statement Value Search

Feature 132 exposes the completed Feature 131 value-search capability through the existing AI query facade.

It translates a natural-language question such as `نمادی را پیدا کن با درآمد 3300508` into the existing
`IFinancialStatementValueSearchService` contract, then returns the persisted company and line-item evidence.

Feature 132 owns only AI interpretation, routing, tool invocation, and response shaping. Feature 131 remains
the sole owner of exact decimal matching, latest-statement selection, company resolution, and evidence grouping.

## Dependency

```text
Feature 131 — Financial Statement Value Search
  -> Feature 132 — AI Financial Statement Value Search
```

## Explicit non-goals

- no new database tables, columns, or migrations;
- no second financial-statement query implementation;
- no public endpoint beyond the existing AI facade;
- no fuzzy numeric matching, rounding, tolerance, or currency conversion;
- no title-only or metric-only search;
- no historical or statement-ID lookup;
- no provider API call during query execution;
- no investment recommendation or analyst interpretation.
