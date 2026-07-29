# Monthly-sales trend asks for clarification for شپاکسا

Date: 2026-07-29  
Status: Fixed in current working tree

## Title

Monthly-sales trend routing fails to extract the known symbol شپاکسا, while latest-price lookup resolves it.

## Observed behavior

The same company is handled differently:

```text
آخرین قیمت شپاکسا؟
=> latest price is returned for شپاکسا.

روند فروش ماهانه شپاکسا؟
=> لطفاً نام نماد یا شرکت موردنظر را در پرسش خود مشخص کنید.
```

## Expected behavior

`روند فروش ماهانه شپاکسا؟` must route to `MonthlyActivityTrend`, extract `شپاکسا`, resolve it through the canonical company-resolution path, and return the persisted monthly trend response. If the trend snapshot is unavailable, the response must say that trend data is unavailable for the resolved symbol; it must not ask for a symbol that is already present.

## Root cause

The active Microsoft Agent Framework V2 workflow has a deterministic monthly-trend branch in `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs:345-363`.

The branch calls `MonthlyActivityTrendIntentRules.ExtractCompanySymbol`. When that heuristic returns `null`, the workflow immediately emits the exact clarification shown above at lines 348-358. The use case is never called.

The extractor in `src/backend/FinancialCopilot.Application/AI/Orchestration/MonthlyActivityTrendIntentRules.cs:58-130`:

- removes a matched trend phrase;
- scans only one contiguous Arabic/Persian Unicode token;
- accepts only tokens of length 2–6;
- excludes a local stop-word list;
- does not call the canonical company/symbol resolver used by the latest-price path.

This creates two different entity-resolution paths. Latest-price lookup succeeds through the general symbol lookup/resolution flow, while monthly-trend routing can fail before resolution. The clarification proves the trend branch received no extracted symbol; a missing monthly snapshot would produce the separate “اطلاعات روند فروش ماهانه ... یافت نشد” message at `FinancialCopilotWorkflowDefinition.cs:377-379`.

The current workspace source appears capable of extracting the six-character literal `شپاکسا`, and existing unit tests cover other symbols. Therefore the deployed failure additionally requires verification of the deployed binary and the exact received Unicode input (hidden ZWNJ/variation characters, Arabic/Persian character variants, or an older build are possible). No production change should be inferred from the user-facing symptom alone until that runtime value is captured.

## Reproduction and diagnostic steps

1. Run the exact query through the active V2 endpoint.
2. Record the workflow intent tag and the normalized message immediately before `ExtractCompanySymbol`.
3. Record the extractor result without logging sensitive conversation data beyond this symbol/query diagnostic.
4. Confirm whether the running deployment contains the current `MonthlyActivityTrendIntentRules` implementation.
5. If extraction returns `شپاکسا`, trace canonical resolution and the monthly snapshot repository; the response must then be the no-data message, not clarification.

## Acceptance criteria for the fix

- `روند فروش ماهانه شپاکسا؟` detects `MonthlyActivityTrend` in both V1 and V2.
- The extracted entity is normalized and resolved through the same canonical resolver as latest-price lookup.
- Persian/Arabic `ی`/`ک`, ZWNJ, punctuation, whitespace, and symbol-before/after-phrase variants are covered.
- A known symbol that has no trend snapshot returns the resolved-symbol no-data message, never symbol clarification.
- Existing latest-price, point metric, scanner, and monthly-trend aliases remain unchanged.
- Tests cover at least `شپاکسا`, `شپدیس`, `کهمدا`, and a company-name variant, plus a negative query with no entity.

## Resolution

`MonthlyActivityTrendIntentRules` now:

- extracts Unicode letters rather than treating the entire Arabic-script range as a token (which previously included Arabic punctuation such as `؟` in the candidate);
- normalizes Arabic `ک`/`ی`, tatweel, and zero-width joiners inside candidate symbols;
- preserves a zero-width-joiner query for extraction before the phrase-normalized fallback;
- keeps the six-character symbol guard that prevents arbitrary long prose tokens from being treated as symbols.

Regression coverage was added for:

```text
روند فروش ماهانه شپاکسا
روند فروش ماهانه شپ‌اکسا
روند فروش ماهانه شپاکسا؟
```

The focused `MonthlyActivityTrend077Tests` suite passes 56/56 tests and the backend solution builds successfully. The deployed service must still be rebuilt/restarted so it runs this version of the rule.

## Affected files

- `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs:345-363`
- `src/backend/FinancialCopilot.Application/AI/Orchestration/MonthlyActivityTrendIntentRules.cs:50-130`
- `src/backend/FinancialCopilot.Application/AI/Orchestration/AiQueryOrchestrationService.cs:419-431`
- `tests/FinancialCopilot.UnitTests/MonthlyActivityTrend077Tests.cs`
- `tests/FinancialCopilot.IntegrationTests/AiFacadeV2EndpointTests.cs:744-841`
