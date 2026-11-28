# User Story: Switch For The Option Starter Card

## Feature Folder   `168-option-comparison-answers/changes/option-starter-card-switch`
## Layers / Area    backend, frontend, ops · AI
## Priority         P2
## Complexity       S
## Depends On       168-option-comparison-answers
## References       `specs/168-option-comparison-answers/user-story.md` (AC-12), `docs/option-comparison-answers.md`, [design](design-proposal.md)

## Story

As the operator, I want the «اختیار خرید نماد» starter card on the new-chat page behind its own `.env` key, so
that I can hide or show it without turning off option answers that users type themselves.

## Scope

- New setting `OptionAnswers:StarterCardEnabled` (default `false`), compose variable `OPTION_ANSWERS_STARTER_CARD_ENABLED`.
- Availability response field `optionComparisonStarterCard`; the starter card follows it on desktop and phone.

## Out Of Scope

- The option answer, intent matching, billing, Telegram, read API: unchanged.
- The symbol-menu item: stays tied to `OptionAnswers:Enabled` (owner, 2026-11-28).

## Acceptance Criteria

- [x] AC-01: With `OptionAnswers:Enabled=true` and `StarterCardEnabled=false`, the new-chat page on desktop and phone shows no «اختیار خرید» card, and typing «اختیار خرید اخابر» returns the option comparison.
- [x] AC-02: With both on, the card is shown, as before.
- [x] AC-03: With `OptionAnswers:Enabled=false`, the card is hidden whatever `StarterCardEnabled` says.
- [x] AC-04: The availability response carries `optionComparisonStarterCard`; clients that do not know it ignore it.

## Assumptions

- A key change takes effect after the API restarts with the new `.env`; no frontend rebuild.

Feature 168 AC-12 ("the starter card … produce a question that routes to the capability") now holds when the key is on.
