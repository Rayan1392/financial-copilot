# Feature 141 — Phase 1.5 Unit-Dimension Repair and Evaluation

## Resolution

The failed smoke question was:

> فروش ریالی شپدیس در ماه قبل با ماه مشابه سال قبل چند درصد تفاوت داشت؟

The original failed model JSON was not retained. The prior artifact records only `semantic_validation_failure` / `requested_unit_dimension_mismatch`, one model call, 1,851 input tokens, 838 output tokens, and 9,246 ms. Therefore the exact original `requestedUnit` cannot be recovered. Given the canonicalizer's reviewed mapping of `فروش ریالی` to `salesAmount` / `CurrencyAmount`, the validator's reported mismatch means the requested unit was incompatible with that base dimension; `Percent` is the likely value because the user explicitly asks for a percentage difference, but that value is an inference, not the original raw evidence.

A raw-output capture of the same question after the repair produced:

```json
{
  "measure": {
    "userPhrase": "فروش ریالی",
    "conceptId": null,
    "kind": "salesAmount",
    "dimension": "currencyAmount",
    "qualifiers": {
      "valueForm": "percentageChange",
      "reportingBasis": "monthlyDiscrete",
      "valueGrain": "companyTotal"
    },
    "requestedUnit": "percent",
    "effectiveSourceUnit": null,
    "effectiveSourceUnitEvidence": "unresolved"
  },
  "operation": "growthRate",
  "comparison": {
    "mode": "yearOverYear",
    "baselineDescription": "ماه مشابه سال قبل",
    "targetDescription": "ماه قبل"
  },
  "period": {
    "kind": "relativePeriods",
    "granularity": "month",
    "numberOfPeriods": 2
  },
  "entityScope": { "kind": "namedCompanies", "companyLabels": ["شپدیس"] },
  "ambiguity": { "requiresClarification": false }
}
```

The original failing response itself is unavailable, so the report does not claim that this reproduced JSON is its exact payload. An earlier reproduction before the semantic fix did return `valueForm=percentageChange`, `output.requestedFields=["percentageDifference"]`, and `requestedUnit=irr`; it passed the old validator but was semantically wrong for the requested result display. The final prompt/canonicalization now yields `percent` consistently.

After deterministic canonicalization the intent is `salesAmount` / `CurrencyAmount`, requested display unit `Percent`, source unit `null` with `Unresolved` evidence, `GrowthRate`, and `YearOverYear`. The validator had treated every requested unit as a unit of the underlying measure; that incorrectly rejected the valid percent result for a percentage-change qualifier. It was a validator/contract-semantics mismatch, not a wrong measure dimension or a source-unit claim.

## Minimal changes

- Canonicalization now treats `percentageChange` as a percent result display while preserving the underlying measure dimension and effective source unit/evidence. No conversion is performed.
- Validation checks display-unit compatibility with the value form. Thus `SalesAmount + Percent` is valid only for a percentage-change result; `SalesAmount + IRR/MillionIRR` remains valid for reported/absolute monetary values; assigning a monetary display/source unit to a physical measure remains invalid.
- Trusted source-unit evidence is kept separate and validated against the canonical dimension. The LLM extractor still rejects model-asserted effective source units. No conversion factor or conversion execution was added.
- When a model includes a product label in its measure phrase, canonicalization removes the separately captured product label only for exact reviewed measure-term resolution. It preserves the original phrase and product constraint and does not infer a physical unit.
- The extraction prompt now distinguishes result display unit, base measure dimension, and source-storage unit and keeps product labels out of the measure phrase.
- The scorer was corrected so dimension/requested-display accuracy do not also depend on effective-source evidence; only the effective-source metric scores its evidence field. Benchmark and holdout questions/labels were unchanged.

No production routing, catalog approval, Navigator, Planner, compiler, executor, or SQL authorization behavior changed.

## Regression coverage and smoke gate

`CanonicalSemanticIntentTests` passes 27/27, including tests for IRR/MillionIRR sales amounts, percent-change display, ton production with approved evidence, unspecified sales-quantity source, incompatible physical/monetary units, per-share profit, ordinary financial-statement amounts, requested display versus approved source scale, missing units, typed ambiguity, and incompatible approved source units. It also retains the strict-schema and malformed-output tests. The generic product-label resolution case verifies no unit inference.

The ordered smoke gate passed:

| Smoke | Result | Tokens (in/out) | Latency |
|---|---|---:|---:|
| Basic structured output | Completed | 55 / 17 | 1.52 s |
| Canonical production quantity (`کگل`) | Completed; `productionQuantity`, physical quantity, unit unspecified; product retained | 1,966 / 609 | 6.38 s |
| Prior failed percent-difference question (`شپدیس`) | Completed; `salesAmount`, currency amount, percent display, YoY | 1,967 / 735 | 7.60 s |
| Population production threshold | Correct typed clarification: month and monthly discrete/cumulative basis are unspecified; `productionQuantity`, `Ton`, source unresolved | 1,967 / 885 | 9.26 s |
| Per-share profit (`فولاد`) | Completed; `earningsPerShare`, currency per share, `IRRPerShare` | 1,965 / 571 | 6.10 s |

No product name was used to infer a source unit. The population clarification was retained as a valid typed ambiguity, not counted as a resolved answer.

## Evaluation results

The original v3 60-case benchmark ran once after the smoke gate. It contains 30 adjudicated cases, one disputed case, and 29 awaiting adjudication. The corrected scorecard reports:

| Metric (30 adjudicated cases unless noted) | Option B | Legacy on same cases |
|---|---:|---:|
| Valid intents | 51/60 (85.0%) | — |
| Measure concepts | 20/30 (66.7%) | 15/30 (50.0%) |
| Measure qualifiers | 13/30 (43.3%) | Not represented |
| Requested display units | 17/30 (56.7%) | Not represented |
| Unit dimensions | 20/30 (66.7%) | Not represented |
| Effective source unit + evidence | 20/30 (66.7%) | Not represented |
| Operation | 27/30 (90.0%) | 9/30 (30.0%) |
| Comparison | 27/30 (90.0%) | 17/30 (56.7%) |
| Period | 22/30 (73.3%) | 15/30 (50.0%) |
| Entity scope | 27/30 (90.0%) | 22/30 (73.3%) |
| Clarification decision | 2/6 (33.3%) | 2/6 (33.3%) |

The 60-case pipeline had 51 completed intents, 7 semantic-validation failures, 1 structured-schema validation failure, and 1 provider transport failure. These failure rows are not counted as semantic-correct outputs. Option B reported 123,832 input and 41,087 output tokens over 59 token-reporting cases; mean latency was 7.35 s, maximum 19.89 s.

Among the 27 completed Option B outputs in the 30 adjudicated rows, conditional measure concepts were 20/27 (74.1%), qualifiers 13/27 (48.1%), requested units 17/27 (63.0%), dimensions 20/27 (74.1%), effective source units/evidence 20/27 (74.1%), operation 27/27, comparison 27/27, period 22/27 (81.5%), and entity scope 27/27. These conditional rates supplement—not replace—the unconditional rates above.

The frozen independent 12-case holdout was run once against the same implementation and corrected scorer. Its source and materialized dataset hashes were verified against the frozen values before execution. Results: 10/12 valid intents (83.3%), both expected unsupported cases classified correctly (2/2), no provider or semantic-validation failures. Among the 10 supported/adjudicated rows: measure concepts 6/10 (60%), qualifiers 3/10 (30%), requested units 4/10 (40%), dimensions 5/10 (50%), effective source units/evidence 5/10 (50%), operation 10/10 (100%), comparison 10/10 (100%), period 7/10 (70%), entity scope 10/10 (100%), and clarification 1/1 (100%). On those same 10 rows, legacy achieved measure concepts 5/10, operation 4/10, comparison 7/10, period 4/10, entity scope 9/10; legacy does not represent qualifiers or units. Option B used 23,615 input and 6,877 output tokens over 12 cases; mean latency was 5.89 s, maximum 7.48 s.

These are actual post-repair outputs, not evidence that every semantic field is ready. Qualifier and unit accuracy remain weak, and holdout scores are reported once without further tuning.

## Before/after and verification

The earlier Phase 1 run completed 54/60 intents (90%) but used a different contract and only 11 adjudicated cases; its measure-label score was explicitly not reliable due to contract/gold misalignment. Phase 1.5 now has valid outputs on both benchmark and holdout, and its same-case legacy comparison shows gains in concepts, operation, comparison, period, and entity scope. This does not establish an apples-to-apples semantic improvement over the older Phase 1 result; 51/60 completion is below the prior 54/60, and unit/qualifier fields are newly measurable.

Verification after the repair: Release solution build succeeded with 0 warnings/errors; UnitTests passed 2,054/2,054; ArchitectureTests passed 12/12; scorer tests passed 7/7. The earlier full IntegrationTests run remains 421 passed, 71 failed, 43 skipped, with PostgreSQL/Testcontainers blocked by Docker availability; that suite was not rerun for this shadow-only change.

Recommendation: Phase 2 is **not ready**. The provider/schema path and unit-dimension smoke gate now pass, but 60-case qualifier accuracy is 43.3%, requested-unit accuracy 56.7%, unit-dimension accuracy 66.7%, holdout qualifier accuracy 30%, and the 60-case completion rate is 51/60. Do not route this intent into production query execution based on these results.
