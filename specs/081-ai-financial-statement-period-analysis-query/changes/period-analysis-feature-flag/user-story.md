# User Story: Feature 081 on/off switch

## Feature Folder

`081-ai-financial-statement-period-analysis-query/changes/period-analysis-feature-flag`

## Layers / Area

Layers: backend
Area: AI (touches Billing)

## Priority

P1

## Complexity

S

## Depends On

081-ai-financial-statement-period-analysis-query (done). Runtime collaborator: 163-symbol-grounded-fallback-answer
(its FR-1 trigger is implemented and enabled; its definition of done is not yet ticked, see Assumptions).

## References

- `specs/081-ai-financial-statement-period-analysis-query/changes/period-analysis-feature-flag/design-proposal.md` (approved at Gate A, option B)
- `specs/163-symbol-grounded-fallback-answer/user-story.md` FR-1, FR-13
- `specs/162-context-aware-financial-report-follow-up/changes/sol-deep-analysis-web-search/` (the offer and its tiers)
- `specs/POLICY-V1-FREEZE.md`
- Code: `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs`,
  `src/backend/FinancialCopilot.Application/AI/Orchestration/AiQueryBillingOperationSelector.cs`,
  `src/backend/FinancialCopilot.Application/AI/Orchestration/FinancialStatementAnalysisIntentRules*.cs`,
  `src/backend/FinancialCopilot.Application/AI/SymbolGroundedFallback/SymbolGroundedFallbackHandler.cs`

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As the product owner,
I want to switch Feature 081's fixed statement summary off in configuration,
so that analysis questions such as «سود و زیان شاراک را تفسیر و تحلیل کن» get the Feature 163 analysis offer
instead of a table of numbers.

## Scope

- Options section `FinancialStatementPeriodAnalysis` with `Enabled` (code default `true`; `appsettings.json` `false`).
- V2: with the flag off, a turn that 081 would answer (semantic capability `financial_statement_period_analysis`
  or the deterministic 081 check) ends `Unsupported` with reason code `financial_statement_period_analysis_disabled`.
- Billing operation selection ignores 081 when the flag is off.

## Out Of Scope

- V1 orchestration (frozen).
- Removing 081 code, its capability definition or its interpreter rules.
- Changing 163, its offer, its prices or its eligibility.
- Rewording help text or follow-up chips that mention statement analysis (→ `specs/backlog.md` if needed).

## Functional Requirements

1. **FR-1 Flag.** `FinancialStatementPeriodAnalysis:Enabled` (bool). A missing section means enabled.
2. **FR-2 Semantic path.** With the flag off and a semantic frame for `financial_statement_period_analysis`, the
   capability is not executed.
3. **FR-3 Deterministic path.** With the flag off, the V2 deterministic 081 check does not run the use case.
4. **FR-4 Outcome.** In FR-2 and FR-3 the Feature 141 fallback is tried first. Without its answer the turn is
   `Unsupported`, reason code `financial_statement_period_analysis_disabled`, generic Persian unsupported text,
   completion status `ValidationFailed` (0 credits). Feature 163 then decides whether to add its offer.
5. **FR-5 Labels.** With the flag off, the result step does not label a turn `FinancialStatementPeriodAnalysis`
   from the message text alone.
6. **FR-6 Billing.** With the flag off, `AiQueryBillingOperationSelector` returns the default operation for the
   capability `financial_statement_period_analysis` and does not use the 081 message rule. Table rules are unchanged.
7. **FR-7 Flag on.** With the flag on, behaviour is unchanged.

## AI Query Path

V2 only. Capability `financial_statement_period_analysis` stays in the catalog; it is stopped where it would run.

## Non-Functional Requirements

- No model call, no new outbound call, no new persistence.
- One log line per stopped turn: route, reason code and correlation id, no message text.

## Acceptance Criteria

- [x] AC-1 Flag off: «سود و زیان وبملت را تفسیر و تحلیل کن» returns outcome `Unsupported`, 0 credits, and the
      Feature 163 analysis offer for وبملت. The intent is not `FinancialStatementPeriodAnalysis`.
- [x] AC-2 Flag off: «صورت مالی وبملت را تحلیل کن» returns the same kind of offer.
- [x] AC-3 Flag off: a quarterly report request («عملکرد فصلی وبملت») still returns the statement table.
- [x] AC-4 Flag on (default): the billing selector and options default are unchanged (unit tests).
- [x] AC-5 Flag off: the billing selector returns the default operation for 081-only input and still returns
      `AiQuery.FinancialStatement` for table input.
- [x] AC-6 `appsettings.json` has `FinancialStatementPeriodAnalysis:Enabled` = `false`.

## Assumptions

- «فیچر 16» in the owner's request means Feature 163 (stated in the proposal; not corrected).
- Feature 163's definition of done is not ticked. This change relies only on its implemented and enabled
  trigger (FR-1), which the endpoint tests here exercise.
