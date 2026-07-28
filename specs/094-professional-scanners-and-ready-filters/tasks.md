# Tasks — Professional Scanners and Ready Filters

## 1. Catalog Ownership and Definitions

- [x] Reuse Features 007/008/009 scanner execution/explainability and Feature 015 governed metrics; this feature owns only the ready-filter catalog, saved-filter references, and professional result contracts.
- [x] Define stable filter `Code`, semantic `Version`, Persian title/aliases, category, parameters, conditions, required datasets, market-session policy, ranking, entitlement code, and active/deprecated state.
- [x] Cover governed technical, flow, volume, queue, large-trade, fundamental, industry, and composite filters only where canonical data/metrics exist; list unsupported requested filters explicitly.
- [x] Require deterministic condition/ranking definitions and exact explain-why fields; LLM may resolve aliases/parameters but cannot create SQL or executable formulas.

## 2. Domain and Persistence

- [x] Define typed parameter schemas with units, bounds, defaults, allowed operators, and Persian aliases; validate parameter combinations before execution.
- [x] Define `SavedFilter` as actor-owned reference to catalog code/version plus validated parameters/name; do not copy or mutate catalog logic.
- [x] Persist catalog governance/version history if definitions are database-managed; otherwise use the repository's governed catalog pattern and record effective version in every execution response.
- [x] Enforce unique active code/version, unique saved-filter name per actor if required, actor isolation, soft-delete, and indexes for catalog category/active and actor saved filters.
- [x] Define execution limits: maximum symbols/results/date range/complexity, timeout, pagination bounds, rate limit, and plan capability.

## 3. Execution and Result Contract

- [x] Implement list/get catalog, execute ready filter, save/update/delete/run saved filter, and natural-language alias resolution through the existing scanner boundary.
- [x] Resolve market universe/industry/instrument class explicitly and apply market-session dependencies, latest complete observation, and freshness policy.
- [x] Rank deterministically with declared tie-breakers; response includes canonical company/symbol, rank, matched values/units/periods, thresholds, source freshness, filter version, and exact match reasons.
- [x] Return explicit unavailable/stale/partial status per required dataset; never silently omit a failed condition or substitute an AI estimate.
- [x] Make repeated execution with the same evidence snapshot/version/parameters reproducible and expose evidence/correlation hash.

## 4. API and Telegram UX

- [x] Specify paginated `GET /api/v1/scanners/catalog` with category/search/entitlement metadata and execute via the existing AI facade or internal scanner contract.
- [x] Specify saved-filter actor endpoints and Telegram menus for category, filter, parameters, run, save, rerun, and pagination with versioned replay-safe callbacks.
- [x] Render compact ranked results with explain-why, units, period, freshness, and deep link for full tables; split long output without altering order.
- [x] Localize Persian aliases, validation, unavailable data, market-closed status, rate/plan-limit errors, and deterministic empty results.

## 5. Security, Operations, and Tests

- [x] Enforce entitlement/rate/execution limits server-side, actor isolation for saved filters, input bounds, and prohibition of arbitrary expressions/SQL.
- [x] Emit execution latency, dataset freshness, result count, timeout/limit, catalog/version usage, alias ambiguity, Billing outcome, and failures.
- [x] Unit-test each catalog definition, parameter bounds/aliases, ranking/tie breaks, industry scope, session policy, and explain-why fields.
- [x] Integration-test deterministic fixtures across all filter families, saved-filter isolation/version upgrades, entitlement, pagination, timeout, and stale/provider failure.
- [x] Given valid parameters and fresh evidence, when a ready filter runs twice on the same snapshot, then ordered results and explanations are identical.
- [x] Given missing/stale required data or unauthorized access, when execution is requested, then no fabricated matches appear and an explicit error/partial state is returned.

## Completion Gate

- [x] Keep tasks unchecked until catalog governance, deterministic contracts, all filter-family fixtures, limits, and existing scanner regressions pass.
