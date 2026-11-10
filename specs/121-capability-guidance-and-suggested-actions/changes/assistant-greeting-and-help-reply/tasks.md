# Tasks: Assistant greeting and "what can I ask?" reply

Status: Gate A: Approved by the owner on 2026-11-10 — plan proposed, Gate B not approved

## Feature Folder

`121-capability-guidance-and-suggested-actions/changes/assistant-greeting-and-help-reply`

## Before Starting

- [ ] This file's `Status:` line records Gate B approval by the owner (see `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`, "Recording Approval").
- [ ] The features in `Depends On` are done (their `tasks.md` definition of done is ticked). Feature 121's `tasks.md` has no checkboxes; its user story records it as implemented.
- [ ] Read `user-story.md`, the references it lists, and the relevant `docs/` files.
- [ ] No open question in `specs/open-questions.md` blocks this spec. OQ-030 and OQ-011 do not block. OQ-PROPOSED-A10, if added, changes task 3.3 only.

## Plan Summary

### Files

All paths under `src/backend/` unless stated.

| Action | File | Change |
|---|---|---|
| New | `FinancialCopilot.Application/AI/Orchestration/AssistantHelp/AssistantHelpCapability.cs` | Code `assistant_help`, reason code, relevance reason. |
| New | `.../AssistantHelp/AssistantHelpIntentRules.cs` | Compact key, three phrase sets, leading words, `BuildInterpretation`. Pure. |
| New | `.../AssistantHelp/AssistantHelpReplyComposer.cs` | The one owner of the reply text, samples, and actions; applies the availability filter. |
| New | `.../AssistantHelp/AssistantHelpCapabilityExecutor.cs` | Returns `Executed`, reason code, text payload. |
| Change | `.../AI/Orchestration/ConversationalCapabilityContracts.cs` | Route constant, `All`, catalog entry (`GateRoutedOnly`, no slots), data-requirement arm. |
| Change | `.../AI/Orchestration/ConversationTaskStateContracts.cs` | Gate rule and early return; `RecordOutcomeAsync` guard. |
| Change | `.../AI/Orchestration/ChatCapabilityAvailability.cs` | Add the route to `CyclicalWavesSupportedRoutes`. |
| Change | `.../AI/Orchestration/AiDialogueOutcomePolicy.cs` | `DialogueOutcomeReasonCodes.AssistantHelp`. |
| Change | `.../AI/Orchestration/SemanticCapabilityExecutionContracts.cs` | `SemanticBillingCompletionStatus.InformationalReply`, `For(CapabilityExecutionResult)`, coordinator settles through it. |
| Change | `FinancialCopilot.Infrastructure/Billing/DefaultPricingPolicy.cs` | Add `InformationalReply` to `ZeroChargeStatuses`. |
| Change | `FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs` | Step 3 deterministic-actions branch for `assistant_help`. |
| Change | `FinancialCopilot.Infrastructure/ServiceCollectionExtensions.cs` | Register executor and composer. |
| Change | `tests/FinancialCopilot.UnitTests/ConversationalCapabilityRegistryTests.cs` | Catalog count 21 → 22 (deliberate). |

No change: frontend, Telegram renderer, `FinancialCopilotAgentWorkflowRunner` (reached only outside V2 mode; `MicrosoftAgentFrameworkAiQueryOrchestrationService`), database, `appsettings`, `queries.sql`.

### V2-only enforcement

The gate also serves V1 (`AiQueryOrchestrationService.cs:86`). The rule runs only when all of these hold:

1. `Feature154RoutingScope.IsActive` is true. It is `V1Frozen` when `AiOrchestration:Mode` is not V2 (`ServiceCollectionExtensions.cs:966`), and the frozen V1 service marks its flow with `EnterFrozenV1` (`AiQueryOrchestrationService.cs:57`).
2. `capabilityRegistry` was injected. Existing gate tests that do not pass it keep their meaning.
3. `rolloutCoordinator.Decide("assistant_help", …).ExecuteSemanticRoute` is true, so a rollback mode stands the rule down.

The catalog entry is shared, but `GateRoutedOnly` keeps it out of the model prompt and guidance, so V1 cannot select it.

### Billing path

The executor returns `Executed`, which is not deferred for Feature 141, so `SemanticExecutionCoordinator` settles the turn (`SemanticCapabilityExecutionContracts.cs:234`). `For(result)` returns `InformationalReply` when the reason code is `assistant_help`, otherwise `For(result.Status)`. `OperationUsageChargeCalculator` returns zero for a listed status, and `AiFacadeBillingHook.ApplyUsageBasedCredits` keeps the zero (`operationCharge.CreditsCharged > 0` is false).

### Classifier

This item does not change `queries.sql`. Under classifier v1 the reply still counts as `misrouted` for every phrase `misroute_hint` matches, until OQ-030 is answered (`user-story.md`, Assumptions). No claim about the T15 gap rate is made.

## Implementation Tasks

Each behaviour test is written first and seen failing for the reason named.

### 1. Regression tests first (must fail today)

Questions are invented. Tests use literal strings where possible so they compile and fail by assertion before any production code exists. The endpoint test uses the existing `AiFacadeApiFactory`, which runs on in-memory databases, so port 5432 is not used.

| Task | Test | File | Fails today because |
|---|---|---|---|
| - [ ] 1.1 | `Gate_WholeMessageHelpPhrase_RoutesToAssistantHelpWithoutCallingTheModel` (theory, all 80 phrases) | new `tests/FinancialCopilot.UnitTests/AssistantHelpReply121Tests.cs` | The stub proposal provider is called and the frame is not `assistant_help`. |
| - [ ] 1.2 | `Gate_HelpPhraseVariants_Match` (theory: punctuation, ZWNJ, Arabic letters, `ؤ`, leading words) | same | Same. |
| - [ ] 1.3 | `Gate_FollowUpAfterGenericRefusal_RoutesToAssistantHelp` | same | Same; the state has no pending action, but no rule exists. |
| - [ ] 1.4 | `Gate_FollowUpAfterNormalAnswer_RoutesToAssistantHelpAndLeavesTaskStateUnchanged` | same | Frame differs, and the state version changes. |
| - [ ] 1.5 | `Catalog_AssistantHelp_IsGateRoutedOnlyWithNoSlots` | same | `registry.Find("assistant_help")` is null. |
| - [ ] 1.6 | `Availability_AssistantHelpRoute_IsAvailableInCyclicalWavesMode` | same | The unknown route gets `UnavailableForCyclicalWaves`. |
| - [ ] 1.7 | `RegisteredPricingPolicy_InformationalReply_IsFree` | `tests/FinancialCopilot.UnitTests/BillingDomainTests.cs` | The status is not zero-charge, so 1 credit is charged. |
| - [ ] 1.8 | `SemanticCoordinator_AssistantHelpResult_SettlesAsInformationalReply` | `tests/FinancialCopilot.UnitTests/SemanticCapabilityDispatcherTests.cs` | The capability is unknown, so the frame settles as `ValidationFailed`. |
| - [ ] 1.9 | `AiQuery_Greeting_ReturnsIntroductionAnsweredFreeWithFourSamples` (V2 path, stub provider returns no capability) | new `tests/FinancialCopilot.IntegrationTests/AssistantHelpReply121EndpointTests.cs` | The response is `Unsupported` with the generic refusal. |

Guard tests, which pass today and must keep passing:

- [ ] 1.10 `Gate_GreetingWithQuestion_KeepsCurrentRoute` and `Gate_ShortRealQuestion_KeepsCurrentRoute`
- [ ] 1.11 `Gate_HelpPhraseDuringPendingClarification_KeepsExistingFlow`
- [ ] 1.12 `Gate_HelpPhraseUnderFrozenV1_KeepsCurrentRoute` and `Gate_HelpPhraseInRollbackMode_KeepsCurrentRoute`
- [ ] 1.13 `Gate_EligibleTickerEqualToListedPhrase_SnapshotWins` (AC-16), `Catalog_AssistantHelp_IsNotOfferedToTheModelOrStarterPrompts` (AC-17), `TelegramRenderer_AssistantHelpResponse_ShowsTextAndSuggestions` (AC-18)

### 2. Capability and reply

- [ ] 2.1 Add `AssistantHelpCapability`, the reason code, the route, and the catalog entry. Update the catalog count test.
- [ ] 2.2 Add `AssistantHelpIntentRules` with the phrase lists from `user-story.md`.
- [ ] 2.3 Add `AssistantHelpReplyComposer` with the exact text, samples, and availability filter. Tests: exact text both languages, action order, `CyclicalWaves` filter, sample round-trip through `DeterministicCapabilityInterpreter` (these need the new type, so they are written with it).
- [ ] 2.4 Add `AssistantHelpCapabilityExecutor`. Add the route to `CyclicalWavesSupportedRoutes`.

### 3. Gate

- [ ] 3.1 Add the rule after the Symbol Snapshot check, with the three conditions above and the pending-action skip.
- [ ] 3.2 On a match, return early with a frame that has no slots, without calling the hybrid interpreter, entity resolution, or `ResolveFollowUpAsync`. Record the interpretation event.
- [ ] 3.3 Skip `RecordOutcomeAsync` for an `assistant_help` frame, so the task state stays unchanged.

### 4. Billing

- [ ] 4.1 Add `InformationalReply`, `For(CapabilityExecutionResult)`, and use it in the coordinator.
- [ ] 4.2 Add the status to `DefaultPricingPolicy.ZeroChargeStatuses`.

### 5. Workflow and wiring

- [ ] 5.1 In step 3, apply the composer's actions as deterministic suggestions when the frame is `assistant_help` and the outcome is Answered.
- [ ] 5.2 Register the executor and composer.

### 6. Verification

- [ ] 6.1 Confirm the four samples return data for `کگل` on Development, sent through the running application or a read-only check of the tables involved. No production database. If a sample cannot be confirmed, stop and report it for the owner; do not swap the symbol silently. The read-only role used by Feature 156 could not read the monthly tables, so this may need another way to run the check.
- [ ] 6.2 Run the web browser check and one Telegram render check with an invented greeting.

## Tests

- [ ] Unit / integration / architecture tests for the above (`tests/FinancialCopilot.UnitTests`, `tests/FinancialCopilot.IntegrationTests`, `tests/FinancialCopilot.ArchitectureTests`). Named in task group 1 and task 2.3.
- [ ] Frontend tests for UI work: not applicable, no frontend change.

## Definition Of Done

- [ ] `dotnet test src/backend/FinancialCopilot.sln --configuration Release` green (builds the solution and runs unit, integration and architecture tests): no failure outside `specs/test-baseline.md`.
- [ ] For database work: not applicable, no migration.
- [ ] For frontend work: not applicable.
- [ ] Acceptance criteria in `user-story.md` all checked.
- [ ] Reference docs updated: `specs/README.md` index entry (lead-owned; proposed text handed over), affected `docs/*.md` and `README.md` sections.
- [ ] User-visible change: a Persian, user-facing line under `## [Unreleased]` in `CHANGELOG.md` (lead-owned; proposed text handed over; see `AGENTS.md`, "Versions and Releases").
- [ ] New questions → `specs/open-questions.md`; deferred work → `specs/backlog.md`; defects found → `specs/bugs.md` (all lead-owned; proposed text handed over).
