# Bug: Confidence Score Returns 0% for Structured Financial Answers

## Summary

The chat UI showed `0%` confidence for a valid symbol metric lookup:

- Question: `pe شپنا چقدر است؟`
- Narrative answer: `نسبت P/E نماد شپنا برابر است با 5.17`
- Supporting table: `PE_TTM = 5.17`
- Displayed confidence: `0%`

This is incorrect because the answer was grounded in structured financial data and the narrative value matched the returned table value.

## Root Cause

Scanner responses computed confidence under `ExplainableAnswer.Confidence`, but symbol lookup responses returned only deterministic prose and `SymbolLookupTable`.

The frontend mapped confidence from `explainableAnswer.confidence.score` and fell back to `0` when no explainable answer was present. Valid symbol lookup answers therefore displayed `0%` even when the structured table contained the exact value used in the answer text.

## Fix

- Added `IConfidenceScoringService` for deterministic confidence scoring outside the scanner-only explainable answer path.
- Added top-level `ConfidenceScore` to AI query responses and persisted assistant payloads.
- Computed confidence for symbol lookups from:
  - source type: pre-calculated metric, derived metric, LLM inference, or missing-data fallback
  - data completeness
  - number of supporting financial cells
  - source freshness
  - consistency between narrative numbers and structured table values
- Added logging for scoring inputs and final score.
- Updated frontend chat mapping to prefer backend `confidenceScore` before falling back to explainable-answer confidence.

## Regression Coverage

- `PE_TTM` exists and narrative matches the table: confidence >= 95%.
- Derived metric calculated successfully: confidence >= 85%.
- Partial data used: confidence between 50% and 80%.
- No supporting data: confidence <= 30%.

## Acceptance Criteria

- [x] Structured PE lookup with matching narrative/table values does not display `0%`.
- [x] `PE_TTM = 5.17` with narrative `5.17` scores at least 95%.
- [x] Missing or fallback responses remain low-confidence.
- [x] Regression tests cover the reported failure mode.
