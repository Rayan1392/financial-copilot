# Feature 116 — AI Sales-Growth Symbol Scanner

This folder specifies the missing end-to-end capability for discovering and listing symbols whose latest monthly sales have grown relative to:

- the previous month;
- the same month of the previous year;
- the average of the previous 12 months.

It supports:

- positive growth without a numeric threshold;
- minimum growth percentage;
- current-to-baseline multiples such as `2 برابر`;
- natural-language intent detection without dependence on exact commands;
- deterministic web and Telegram result tables.

Implementation and operating guidance: [Feature 116 documentation](../../docs/feature-116-sales-growth-scanner.md).

## Why a New Feature Is Needed

Existing specs already provide monthly-sales ingestion, growth calculations, aliases, scanner parsing/execution, and single-symbol/trend responses. They do not fully specify the list-oriented discovery use case, comparison matrix, threshold semantics, common cross-symbol period policy, and requested result-table contract as one implementable feature.

## Files

- `user-story.md` — business behavior, semantic rules, result contract, and acceptance criteria.
- `tasks.md` — implementation tasks, tests, integration boundaries, and completion gate.
