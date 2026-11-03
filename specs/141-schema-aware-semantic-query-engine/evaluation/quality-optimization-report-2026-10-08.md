# Feature 141 Navigator Quality Optimization Report (2026-10-08)

## Golden before and after

| Metric | Baseline v2 | Final retried live | Change |
|---|---:|---:|---:|
| nodeRecall | 0.4840 | 0.9160 | +0.4321 |
| metricRecall | 0.6154 | 0.8462 | +0.2308 |
| dimensionRecall | 0.8095 | 0.9048 | +0.0952 |
| domainAccuracy | 0.5238 | 0.0000 | -0.5238 |
| financialReferenceAccuracy | 0.6250 | 0.6250 | +0.0000 |
| sourceItemReferenceAccuracy | 0.7500 | 0.7500 | +0.0000 |
| requirementsFinancialReferenceRecall | 0.7143 | 0.7143 | +0.0000 |
| financialOperationAccuracy | 0.8750 | 0.8750 | +0.0000 |
| statementScopeAccuracy | 1.0000 | 1.0000 | +0.0000 |
| entityResolutionAccuracy | 1.0000 | 1.0000 | +0.0000 |
| periodInterpretationAccuracy | 1.0000 | 1.0000 | +0.0000 |

All 21 Golden cases scored with no unscored rows. Two baseline request-budget cases now produce predictions. Three initial failed rows were retried through resume mode without changing code, budgets, labels, or provider.

| Recorded prediction usage | Baseline v2 | Final latest rows |
|---|---:|---:|
| Calls | 42 | 42 |
| Input tokens | 218146 | 217771 |
| Output tokens | 15062 | 15480 |
| Model latency ms | 224796 | 212814 |

Retry traffic: 6 calls, 32841 input tokens, 2576 output tokens, 40206 ms. Including the initial final-code run, observed traffic totals 47 calls, 243795 input tokens, 18589 output tokens, and 253287 ms. Provider cost metadata was unavailable.

## Per-case regressions and improvements

Metric misses are fixture-required catalog metric IDs absent from the prediction. Governance remains separate.

| Case | Status baseline to final | Metric misses baseline to final | Node recall baseline to final |
|---|---|---|---:|
| `fa-01` | completed to completed | none to none | 0.375 to 0.875 |
| `fa-02` | completed to completed | none to none | 0.600 to 1.000 |
| `fa-03` | completed to completed | none to none | 0.667 to 1.000 |
| `fa-04` | completed to completed | metric_net_profit to none | 0.200 to 1.000 |
| `fa-05` | completed to completed | none to none | 0.600 to 1.000 |
| `fa-06` | completed to completed | none to none | 0.600 to 1.000 |
| `fa-07` | failed to completed | metric_monthly_sales_growth_yoy to none | 0.000 to 0.700 |
| `fa-08` | completed to completed | none to none | 0.750 to 1.000 |
| `fa-09` | completed to completed | none to none | 0.500 to 1.000 |
| `fa-10` | completed to completed | none to none | 0.667 to 1.000 |
| `fa-11-heldout` | failed to completed | metric_monthly_sales_growth_yoy to none | 0.000 to 1.000 |
| `fa-12-heldout` | completed to completed | none to none | 1.000 to 1.000 |
| `fa-13-heldout-unsupported` | completed to completed | none to none | 0.333 to 0.333 |
| `fa-value-01` | completed to completed | none to none | none to none |
| `fa-value-02` | completed to completed | none to none | none to none |
| `fa-value-03` | completed to completed | none to none | none to none |
| `fa-value-04` | completed to completed | none to none | none to none |
| `fa-value-05` | completed to completed | none to none | none to none |
| `fa-value-06` | completed to completed | none to none | none to none |
| `fa-value-07` | completed to completed | none to none | none to none |
| `fa-value-08` | completed to completed | none to none | none to none |

## Independent held-out v5

This fresh fixture was run once after implementation freeze and was not used for tuning.

| Metric | Recall |
|---|---:|
| nodeRecall | 0.6667 |
| metricRecall | 0.6667 |
| dimensionRecall | 0.6667 |
| domainAccuracy | 0.0000 |
| statementScopeAccuracy | 0.0000 |

Usage: 5 calls, 18527 input tokens, 2280 output tokens, 30294 ms. One case failed independent extraction validation with `requirements_contain_unsupported_entityKinds`.

## Root causes and remaining limits

- Deterministic required-node seeding raised Golden node recall from 0.484 to 0.916 and metric recall from 0.615 to 0.846. Dimension recall reached 0.905. Both original input-budget failures now have predictions under unchanged caps.
- Metric recall (0.846) and source-item reference accuracy (0.750) remain below 0.90 targets. Source item 161 remains correctly selected as NoavaranCurrentApi / IncomeStatement / SourceItemId 161.
- Exact domain accuracy fell from 0.524 to 0.000. One final row contains 60 selected node IDs despite the intended provider cap of 48, exposing an end-to-end context bound issue.
- Held-out v5 node, metric, and dimension recall are each 0.667; one case failed entity-kind validation. This does not support planner readiness.
- Strict validation rejected non-visible expansion IDs and unsupported financial operations in the initial Golden run; the failed rows were retried and nothing invalid was accepted.
- Semantic review and query authorization remain pending. No query executed, no catalog item was approved, and the 296-item dictionary was preserved.

## Verification and recommendation

- Catalog validation passed; source/hierarchy hash `b7ce865504f0b2b78321cd15a17f64b3b11bebb2ea8182bd8709bdc9e71935f4`.
- .NET Release solution build passed with 0 warnings/errors. Focused navigator/completeness tests: 17 passed. Python catalog/scorer tests: 21 passed.
- Development OpenAI `gpt-5.6-luna` via User Secrets; no fallback and no credential values logged.
- Provider cost metadata was absent; no price estimate was inferred for this model alias.
- **Query Planner recommendation: not ready for activation.** Improve metric/source-item accuracy, exact domain quality, held-out recall, end-to-end node bounding, and governance before planner review.
