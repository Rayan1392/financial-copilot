# User Story - AI Evaluation and Regression Framework

## Story

As a platform quality owner,
I want repeatable evaluation of prompts and AI workflows,
so that changes to interpretation, routing, answer generation, and financial reasoning do not silently reduce product quality.

## Acceptance Criteria

- The internal quality capability models `GoldenQuestion`, `GoldenAnswer`, `EvaluationDataset`, `PromptVersion`, `EvaluationRun`, `EvaluationScore`, and `RegressionResult`.
- Evaluation datasets can contain deterministic expected scanner plans, clarification outcomes, metric resolutions, evidence requirements, and allowed prose criteria.
- Prompt, workflow, model-provider configuration, semantic-definition version, and calculation-policy version used by each evaluation run are recorded.
- Scanner query interpretation accuracy, clarification correctness, ranking consistency, hallucination checks, and financial metric extraction quality can be scored.
- Deterministic financial correctness is evaluated against backend-calculated evidence rather than accepting LLM assertions.
- Evaluation runs compare current results with approved baselines and expose regressions over time.
- Tests can use deterministic fake AI providers; controlled provider comparison evaluation can be run separately when needed.
- This capability is internal quality infrastructure and does not alter the public AI facade contract.

## Technical Notes

- Begin with curated high-value questions in Persian and English, including ambiguous queries and unsupported requests.
- Automated scoring may be exact for structured plans/calculated facts and rubric-assisted for prose quality; identify which score type was used.
- Do not couple production query execution to running evaluation jobs.
