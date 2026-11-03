# Feature 141 Correctness, Budget, and Evaluation Audit (2026-10-08)

## Scope and evidence

This addendum preserves the prior reports and their predictions. It verifies the prior live Golden output offline, then records a fresh live Golden run and a fresh post-fix holdout. The v5 held-out case inspected during diagnosis is treated as consumed for debugging and is not used for final quality claims.

Primary artifacts:

- Scorer-audited prior Golden: `scored-navigator-quality-golden-offline-audited-v1.json`
- Fresh Golden predictions and score: `../artifacts/navigator-quality-corrected-v3-live.jsonl`, `scored-navigator-quality-corrected-v3-live.json`
- Fresh holdout fixture, predictions, and score: `navigation-heldout-v6-unseen-2026-10-08.json`, `../artifacts/navigator-heldout-v6-final-live.jsonl`, `scored-navigator-heldout-v6-final-live.json`
- v5 failure evidence: `../artifacts/navigator-heldout-v5-final-live.jsonl`, `scored-navigator-heldout-v5-offline-audited-v1.json`

## Scoring contract

Domain labels map to canonical navigation domain node IDs (`domain-{label}`). For each case, the scorer compares the exact set of selected domain node IDs with the fixture's expected domain set. `domainAccuracy` remains a strict exact-set hit rate; it is not recall. Domain precision, recall, and F1 are reported separately as macro averages over applicable cases. A selected extra domain lowers precision and fails exact accuracy even when every expected domain is present.

Node scoring is set based. The report includes expected, selected/final-context, true-positive, false-positive, and missing-required counts, plus precision, recall, and F1. `llmSelectedNodeCount` is populated for new live rows from navigator candidates; `finalExpandedContextNodeCount` measures the serialized final context. Historical artifacts did not persist the LLM candidate IDs, so that field remains unknown for the offline baseline.

## Golden results

| Metric | Prior recorded final (offline rescored) | Fresh live after fixes | Change |
|---|---:|---:|---:|
| Node recall | 0.916 | 0.728 | -0.188 |
| Macro node precision | 0.315 | 0.535 | +0.220 |
| Macro node F1 | 0.407 | 0.572 | +0.165 |
| Average final context nodes | 23.05 | 7.71 | -15.34 |
| Maximum final context nodes | 60 | 11 | -49 |
| Exact domain-set accuracy | 0.000 | 0.619 | +0.619 |
| Domain precision | 0.244 | 0.762 | +0.518 |
| Domain recall | 0.952 | 0.833 | -0.119 |
| Domain F1 | 0.382 | 0.778 | +0.396 |
| Metric recall | 0.846 | 0.846 | 0.000 |
| Dimension recall | 0.905 | 0.905 | 0.000 |
| Financial reference accuracy | 0.625 | 0.750 | +0.125 |
| Direct source-item accuracy | 0.750 | 0.875 | +0.125 |
| Financial operation accuracy | 0.875 | 0.750 | -0.125 |

All 21 fresh Golden cases produced recorded predictions. The 48-node breach is fixed: the largest final context had 11 nodes. Unit tests exercise initial selections of exactly 48, 49, and 60 nodes. Selection and expansion are bounded; shared dimensions no longer make every domain branch appear relevant. When a final expanded context exceeds the limit, it is rehydrated at the bound and classified incomplete if required context cannot be retained. The independent requirements extractor outline cap is 16,000 characters so the request-wide token reserve fits the existing hard limit.

The node recall decrease is a real tradeoff from reducing over-selection, not a scorer change. Exact domain accuracy is still below a readiness threshold, and the operation score regressed. The planner remains unsuitable for activation.

A later experiment counted specific dimensions as branch-routing signals in addition to measures and references. Its Golden exact domain accuracy fell to 0.571 and metric recall to 0.769, so that variant was reverted. The final score above uses the better-performing shared-company-dimension exclusion rule; the v4 artifacts retain the rejected experiment for comparison.

## Offline scorer verification

The previous final predictions were rescored without calling an LLM. Expected labels, predictions, and strict exact domain semantics were unchanged. The recorded domain score of 0.000 was reproduced; the defect was excessive unrelated domain selection, not scorer normalization or hierarchy granularity. Added F1, precision, false-positive, and context-count reporting does not convert exact accuracy to recall.

## Financial source-item findings

- `fa-value-06` asks for consolidated net profit. The outline retrieves the correct `NoavaranCurrentApi / IncomeStatement / SourceItemId=143` candidate (`سود خالص`) at rank 1 with match score 1008. The independent requirement now selects `source_item_66fdf1090cbf96d3`; final navigation preserves that verified identity. The catalog has a distinct consolidated per-share candidate (SourceItemId 168), which is not substituted. The catalog identity is verified, but semantic review and query authorization remain pending.
- `fa-value-07` asks for year-over-year operating-profit growth. The candidate list contains SourceItemId 140 (`سود عملیاتی`), the underlying operating-profit amount, while independent extraction selects `metric_operating_profit_growth_yoy`. That distinction is semantically appropriate for a growth value; the frozen fixture expects the base operating-profit source item and therefore records a reference mismatch. The fixture was not rewritten. The candidate list is present, so this is not a candidate-retrieval omission.
- The successful `NoavaranCurrentApi / IncomeStatement / SourceItemId=161` resolution for operating profit per share remains exact, has no MetricCode, and preserves an unknown unit.
- An additional frozen fixture expects no financial reference for a net-profit-margin question while the extractor selects the governed net-profit-margin metric. That affects financial-reference exactness, not direct source-item accuracy. No labels were changed.

The reusable extraction instruction now separates statement scope from financial concept: standalone/consolidated scope is carried independently, plain net profit remains distinct from per-share profit/EPS, and a growth measure is not replaced by its underlying amount.

## Held-out evaluation

The v5 failure is `holdout-v5-cashflow-net-margin`, which failed during requirements extraction with `requirements_contain_unsupported_entityKinds`. The fixture question is “For each issuer, show the latest standalone operating cash flow alongside net profit margin.” The CLI failure row did not persist the rejected requirements payload, so the exact invalid entity-kind token cannot be recovered from recorded evidence. The case is marked consumed for debugging; no validation rule was weakened.

Fresh v6 held-out results (3 cases, one run): node recall 0.811, metric recall 1.000, dimension recall 1.000, exact domain-set accuracy 0.667, domain precision 0.833, domain recall 1.000. All three rows completed and the largest context contained 10 nodes. This small evaluation is evidence of generalization, not a readiness claim.

## Usage, governance, and verification

Fresh Golden usage: 42 calls, 212,255 input tokens, 14,238 output tokens, 203,279 ms model latency. Fresh v6 usage: 6 calls, 22,680 input tokens, 2,360 output tokens, 31,309 ms. Provider cost metadata was absent. No SQL was produced or executed, and no semantic approval or query authorization was granted.

Validation: Release solution build succeeded with zero warnings/errors; 20 focused .NET navigator/completeness tests passed; 22 SemanticCatalog Python tests passed; canonical source/hierarchy hashes validated as `b7ce865504f0b2b78321cd15a17f64b3b11bebb2ea8182bd8709bdc9e71935f4`.
