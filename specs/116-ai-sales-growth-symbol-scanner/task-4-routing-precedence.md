# Task 4 — Routing and Precedence

Status: Implemented

`LlmAiIntentDetector` now applies the following deterministic precedence:

1. Existing monthly trend/chart, product-revenue-mix, disclosure, and
   financial-statement safeguards remain authoritative.
2. A single-symbol monthly-sales growth request routes to the existing
   `SymbolLookup` intent.
3. A plural/list/discovery request containing monthly-sales and growth terms
   routes to the existing `Scanner` intent.
4. Other messages continue through the existing model-backed intent detector.

The Feature 116 scanner predicate is provider-neutral and does not select a
provider, generate SQL, or execute calculations. Scanner composition remains on
the generic scanner path; semantic plan fields and execution behavior are
handled by later tasks.

Validation: `AiIntentDetectorTests` passes 24 tests, including trend/product
mix safeguards, single-symbol lookup, scanner discovery, and composed scanner
filters.
