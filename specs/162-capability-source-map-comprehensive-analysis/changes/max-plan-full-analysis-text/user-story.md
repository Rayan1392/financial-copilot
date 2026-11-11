# User Story: whole analysis text for Max subscribers

## Feature Folder

`162-capability-source-map-comprehensive-analysis/changes/max-plan-full-analysis-text`

## Layers / Area

Layers: backend | database
Area: AI · Billing · Telegram

## Priority

P2

## Complexity

M

## Depends On

162-capability-source-map-comprehensive-analysis (change `latest-analysis-teaser`; change `bug-020-technical-analysis-topic` on branch `track/a-routing-answer-correctness`)

## References

- `design-proposal.md` in this folder (Gate A approved 2026-11-11)
- `specs/162-capability-source-map-comprehensive-analysis/changes/latest-analysis-teaser/design-proposal.md`
- `docs/providers/cyclicalwaves-v2/README.md` (provider `summary` is the whole paid text)
- Billing model: `BillingDbContext`, `billing_plan_capabilities` (`PlanCapabilityRow`, `BaselinePlanCapabilities`),
  `IPlanCapabilityService`, `IBillableAccountResolver`, `TahlilAppSubscriptionPolicy`
- Code: `ComprehensiveAnalysisCapabilityExecutor` (`SemanticCapabilityExecutors.cs`),
  `CyclicalWavesV2ComprehensiveAnalysisQuery`, `ComprehensiveAnalysisCardComposer`, `ComprehensiveAnalysisToolAdapter`,
  `TelegramAssistantResponseRenderer`, `QueryExecutionContext`

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As a TahlilApp Max subscriber,
I want the whole text of the latest analysis in the chat answer to «تحلیل جامع X»,
so that I can read the analysis I already pay for without leaving the chat.

## Business Context

Max is TahlilApp's top plan. Other users keep the partial preview that sends them to TahlilApp.

## Scope

- A plan capability `ComprehensiveAnalysis.FullText`, enabled for Max only.
- The comprehensive-analysis answer on the V2 semantic route (web and Telegram), including the BUG-020 fallback card.

## Out Of Scope

- Plus and Pro (a later data change to `billing_plan_capabilities`).
- Each article's own TahlilApp `required_plans` (backlog).
- The LLM tool path `ComprehensiveAnalysisToolAdapter.QueryAsync` (agent route): it keeps the preview.
- The legacy CyclicalWaves table path (`EfCoreComprehensiveAnalysisSearchRepository`): unchanged.
- A per-analysis link (change `per-analysis-link`, withdrawn).

## Functional Requirements

1. A user is entitled when the actor is a user (not an API client), the user's individual billing account resolves,
   and its effective plan (expiry applied, TahlilApp windows resolved) has an enabled `ComprehensiveAnalysis.FullText`
   row. No plan, a disabled row, no row, an API client, an error or more than 2 seconds → not entitled.
2. Only an entitled request makes the query return the whole text (repeated title removed); every other request gets
   the preview exactly as today.
3. Entitled card (web): title, «تاریخ انتشار: …», the whole text, then the link line
   `[تحلیل‌های جامع تحلیل‌اپ](<TahlilAppAnalysesUrl>)`, without «متن کامل این تحلیل را در بخش تحلیل جامع، تحلیل اپ
   بخوانید». Telegram: the same parts as plain text with the bare URL.
4. Telegram: when the entitled card would pass 4,096 characters, the text is cut at the last sentence end (else word
   boundary) that fits, followed by « …», and the link line is kept.
5. The no-result reply, routing, charges and the non-entitled card are unchanged.
6. A counter `comprehensive_analysis_card_total{text="full"|"preview"}` is recorded; no text or user id is logged.

## Data / Schema

`BillingDbContext`: one seed row in `BaselinePlanCapabilities` — `Enabled("Max", "ComprehensiveAnalysis.FullText")`
(policy `v1`, no limit) — and its migration. No other table changes.

## AI Query Path

V2 semantic route `comprehensive_analysis`. `QueryExecutionContext` gains `UserId` and `ApiClientId` (optional) so
the executor can ask the entitlement port. The response records whether the whole text was included, so the composer
and the Telegram renderer pick the card.

## UI

No web component change: the card is Markdown text. Admin plan matrix: Persian label «متن کامل تحلیل جامع» for the
new capability in `src/frontend/src/components/admin/admin-page.tsx` `permissionLabels`.

## Non-Functional Requirements

- Fail closed with a 2-second budget for the entitlement check; the answer never waits longer for it.
- The plan is read after the billing reservation (which refreshes TahlilApp status when due).
- The whole text never reaches a model prompt or the agent tool path.

## Acceptance Criteria

- [x] AC-1 A user with an active Max plan asks «تحلیل جامع فولاد»: the answer has the whole seeded text, the date and
  the link line, and not the read-more sentence (web and Telegram).
- [x] AC-2 Free, Plus, Pro, an expired Max, an API client and a user without an account get today's preview card.
- [x] AC-3 With the Max row disabled, a Max user gets the preview card.
- [x] AC-4 An entitlement error or timeout gives the preview card and the answer still succeeds.
- [x] AC-5 «تحلیل تکنیکال کگل» for a Max user: the not-found line, then the whole text of the latest کگل analysis.
- [x] AC-6 Telegram: an entitled card longer than 4,096 characters is cut with « …» and keeps the link.
- [x] AC-7 For a non-entitled request the whole text appears nowhere in the response (unchanged AC4 of
  `latest-analysis-teaser`).
- [x] AC-8 The billing migration adds exactly the one row; no pending model changes.

## Assumptions

- `QueryExecutionContext.ActorId` alone is not enough to find the billing account (API clients); the user id and the
  API client id are passed explicitly, as the billing hook does.
- The production chat answers «تحلیل جامع X» through the semantic route (Feature 162; Shadow and SemanticPrimary both
  execute the frame); the agent tool path is not used for it.
- The existing `IPlanCapabilityService.ValidateCanExecuteAsync` allows an account with no plan (organization
  integrations); it is not used here, because paid text must fail closed.
