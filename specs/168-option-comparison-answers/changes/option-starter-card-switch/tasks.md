# Tasks: Switch For The Option Starter Card

Status: Gate B: Approved by the owner on 2026-11-28 (Gate A and Gate B approved together: «گیت A و B تأیید، منوی نماد همان‌طور بماند»)

## Feature Folder

`168-option-comparison-answers/changes/option-starter-card-switch`

## Before Starting

- [x] This file's `Status:` line records Gate B approval by the owner.
- [x] The features in `Depends On` are done: Feature 168 is implemented and released dark in v1.41.0 (AC-23 open; not needed here).
- [x] Read `user-story.md`, the references it lists, and the relevant `docs/` files.
- [x] No open question in `specs/open-questions.md` blocks this spec.

## Implementation Tasks

### 1. Backend

- [x] 1.1 `OptionAnswersOptions.StarterCardEnabled` (default `false`) and the one rule that combines it with capability availability.
- [x] 1.2 `ChatCapabilityAvailabilityHttpResponse.OptionComparisonStarterCard` filled by `GET /api/ai/v1/capabilities/availability`.

### 2. Frontend

- [x] 2.1 Capability `optionComparisonStarterCard` in the availability hook and server function; the `option-calls` card requires it; desktop `/chat` and phone `/m` pass it.

### 3. Ops and docs

- [x] 3.1 `docker-compose.yml`, `.env.example`, `appsettings.json` (API and Worker).
- [x] 3.2 `docs/option-comparison-answers.md`, `specs/README.md`.

## Tests

- [x] Unit: the three combinations of the rule (AC-01 to AC-03).
- [x] Integration: the availability endpoint with the key off and on (AC-01, AC-02, AC-04); a typed option question is answered with the key off (AC-01).
- [x] Frontend (Vitest): starter list with and without the new capability.

## Verification Record (2026-11-28)

- Build of `FinancialCopilot.sln` (Release): 0 errors, 0 warnings.
- Unit `OptionComparison168Tests` and `ConversationalCapabilityRegistryTests`: 72 passed (the new rule test covers four combinations).
- Integration `OptionComparison168E2ETests`, `OptionComparison168FlagOffE2ETests`, `OptionStarterCardOnE2ETests`: 17 passed (AC-01 to AC-04). The `ValuationGaugeCards*` groups fail at host start-up with BUG-053, as before this change.
- Vitest over `src/lib/chat`, `src/components/mobile`, `src/components/app`: 491 passed, 2 failed in `markdown-message.test.tsx` (Persian digits in a quarterly table) and `monthly-product-trend-chart.test.tsx` (a product-trend error message). Neither touches the changed code; not checked against a clean `HEAD`.
- ESLint (with `endOfLine: auto`) clean and no type error in the touched frontend files.
- Not done: a browser check of the new-chat page; the full backend and frontend suites.

## Definition Of Done

- [~] Build green; touched unit and integration groups green. Outside them: BUG-053 (known) and two Vitest failures named above.
- [x] In `src/frontend`: lint clean on touched files, related Vitest green, `npm run typecheck` adds no new error.
- [ ] Acceptance criteria in `user-story.md` checked.
- [x] Reference docs updated.
- [x] No `CHANGELOG.md` line: option answers are not announced to users yet (flag off on production).
