# Feature 141 ? Final Live Navigator Evaluation (2026-10-08)

## Root causes and fixes

- **`fa-02` truncation:** The old navigation contract repeated requirement declarations already returned by the independent extractor. At a 700-token output cap, the Responses API reported `max_output_tokens`; the adapter had discarded this metadata. Navigation now returns compact typed decisions, preserves completion metadata, classifies truncation as `ModelOutputTruncated`, and allows one bounded retry up to 1,200 output tokens.
- **`fa-value-01` outline overflow:** The old outline was 19,597 characters against an 18,000-character cap. It searched 296 definitions, broadly matching 92 source nodes. The new outline normalizes Persian text, ranks at most 12 compact candidates, and trims the lowest-ranked tail only when needed. Final `fa-value-01` outline size: 15,654 characters. Item 161 scored 1018, ahead of similar item 140 at 1011; the live prediction selected item 161.

## Effective limits

- Four total model calls per request (including extraction and retries), at most three navigation calls, and one retry across the request.
- 18,000 input tokens; 4,200 total output tokens; 700 initial and 1,200 retry output tokens.
- 18,000 outline characters; 10,000 estimated catalog-context tokens; 36,000 prompt characters; depth 2; fan-out 12; 20 seconds per call; 45 seconds total.
- Development User Secrets selected OpenAI `gpt-5.6-luna`; no fallback provider/model was used.

## Golden evaluation

- 21 rows: {"completed": 19, "failed": 2}; outcomes: {"unsupported": 16, "clarification": 3}. Two cases ended with typed request-wide input-budget failures and no predictions.
- Aggregate: node recall 0.484; metric recall 0.615; dimension recall 0.810; domain accuracy 0.524; source-item reference accuracy 0.750.
- Usage: 42 calls, 218,146 input tokens, 15,062 output tokens, 224.8s reported model latency. Provider cost metadata is unavailable.
- `fa-02`: 2 calls; 6,082 input and 765 output tokens; 9.9s; retrieval complete, outcome Unsupported because governance approvals remain pending.
- `fa-value-01`: selected `NoavaranCurrentApi / IncomeStatement / SourceItemId=161`; 2 calls; 11,479 input and 670 output tokens; 9.0s. Retrieval complete, while semantic review/query authorization remain false, so it is non-executable.

### Per-case results

| Case | Status | Outcome | Retrieval complete | Calls | Input tokens | Output tokens | Latency ms | Failure |
|---|---|---|---:|---:|---:|---:|---:|---|
| fa-01 | completed | unsupported | True | 2 | 6565 | 578 | 9054 | ? |
| fa-02 | completed | unsupported | True | 2 | 6082 | 765 | 9937 | ? |
| fa-03 | completed | unsupported | True | 2 | 6149 | 667 | 9757 | ? |
| fa-04 | completed | unsupported | False | 2 | 10156 | 427 | 6313 | ? |
| fa-05 | completed | clarification | False | 2 | 10262 | 634 | 8968 | ? |
| fa-06 | completed | clarification | False | 2 | 12598 | 919 | 12959 | ? |
| fa-07 | failed | ? | ? | 2 | 13888 | 985 | 14363 | request_input_token_budget_exceeded |
| fa-08 | completed | unsupported | True | 2 | 13902 | 954 | 14305 | ? |
| fa-09 | completed | unsupported | True | 2 | 6384 | 958 | 13082 | ? |
| fa-10 | completed | unsupported | True | 2 | 6579 | 649 | 10585 | ? |
| fa-11-heldout | failed | ? | ? | 2 | 14000 | 886 | 15006 | request_input_token_budget_exceeded |
| fa-12-heldout | completed | unsupported | True | 2 | 13974 | 985 | 13489 | ? |
| fa-13-heldout-unsupported | completed | clarification | False | 2 | 7059 | 640 | 9805 | ? |
| fa-value-01 | completed | unsupported | True | 2 | 11479 | 670 | 9036 | ? |
| fa-value-02 | completed | unsupported | True | 2 | 9292 | 618 | 9383 | ? |
| fa-value-03 | completed | unsupported | False | 2 | 11913 | 550 | 8623 | ? |
| fa-value-04 | completed | unsupported | True | 2 | 11416 | 607 | 9809 | ? |
| fa-value-05 | completed | unsupported | True | 2 | 11991 | 495 | 7931 | ? |
| fa-value-06 | completed | unsupported | True | 2 | 12618 | 865 | 13513 | ? |
| fa-value-07 | completed | unsupported | True | 2 | 11655 | 653 | 9396 | ? |
| fa-value-08 | completed | unsupported | True | 2 | 10184 | 557 | 9482 | ? |

## Retrieval, governance, and execution

Across the 19 cases with completed inference, retrieval was complete in 14. All 19 remain unapproved for semantic review and query authorization, so none is executable. Unsupported outcomes reflect catalog governance, not a failed inference. No statement observations were read and no SQL was executed.

## Independent held-out v3

- 3 fresh cases; statuses {"completed": 3}; outcomes {"unsupported": 2, "clarification": 1}.
- Node recall 0.421; metric recall 0.833; dimension recall 1.000; domain accuracy 0.667; statement-scope accuracy 1.000.
- Usage: 6 calls, 24,151 input and 2,178 output tokens, 33.7s; cost metadata unavailable.
- v1 overlapped the baseline. v2 was inspected before final tuning and is retired. v3 tests price plus cash flow, industry ranking with two efficiency measures, and current ratio plus gross margin.

## Verification and remaining work

- `dotnet build tools/SemanticCatalog/SemanticCatalog.csproj --no-restore`: passed.
- Focused .NET navigator/configuration tests: 20 passed; provider adapter integration tests: 16 passed.
- `python -m unittest discover -s tools/SemanticCatalog/tests -v`: 21 passed.
- Catalog and hierarchy hashes validate: `b7ce865504f0b2b78321cd15a17f64b3b11bebb2ea8182bd8709bdc9e71935f4`.
- Remaining blockers: two cases reached the request-wide input budget; semantic review and query authorization remain pending. No query route, compiler, or executor was enabled.

## Reproduction

```powershell
dotnet run --project tools/SemanticCatalog -- evaluate-navigator --tenant-id <tenant-guid> --output specs/141-schema-aware-semantic-query-engine/artifacts/navigator-optimization-golden-final-v2-live.jsonl
python specs/141-schema-aware-semantic-query-engine/evaluation/run_navigation_evaluation.py --dataset specs/141-schema-aware-semantic-query-engine/evaluation/navigation-golden-v1.yaml --predictions specs/141-schema-aware-semantic-query-engine/artifacts/navigator-optimization-golden-final-v2-live.jsonl --output specs/141-schema-aware-semantic-query-engine/evaluation/scored-navigator-optimization-golden-final-v2-live.json
dotnet run --project tools/SemanticCatalog -- evaluate-navigator --tenant-id <tenant-guid> --dataset specs/141-schema-aware-semantic-query-engine/evaluation/navigation-heldout-v3-unseen-2026-10-08.json --output specs/141-schema-aware-semantic-query-engine/artifacts/navigator-heldout-v3-final-live.jsonl
```

Machine-readable report: [`live-evaluation-report-2026-10-08-final.json`](live-evaluation-report-2026-10-08-final.json).
