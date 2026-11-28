# Design Proposal: Switch For The Option Starter Card

Status: Gate A: Approved by the owner on 2026-11-28 («گیت A و B تأیید، منوی نماد همان‌طور بماند»)

## Feature Folder

`168-option-comparison-answers/changes/option-starter-card-switch`

## Layers / Area

backend, frontend, ops · AI

## Request (owner, 2026-11-28)

Remove the «اختیار خرید نماد» card from the new-chat starter cards for now. An `.env` key adds the card back
or removes it. Option questions keep working when typed and sent; only the home-page card follows the key.

## Today

- The card `option-calls` in `src/frontend/src/lib/chat/starter-prompts.ts` has `requires: "optionComparison"`.
  `homeStarterPrompts` shows it when `GET /api/ai/v1/capabilities/availability` returns
  `optionComparison: true`. Desktop `/chat` and phone `/m` use the same list.
- `optionComparison` is true when the V2 path is active and `OptionAnswers:Enabled` is on. The same flag also
  gates the chat route itself and the symbol-menu item.
- So today the card cannot be hidden without turning the capability off.

## Proposal

1. New key `OptionAnswers:StarterCardEnabled`, default `false`. Compose variable
   `OPTION_ANSWERS_STARTER_CARD_ENABLED` (API and Worker share the environment block), `.env.example` line,
   `appsettings.json` value `false`.
2. The availability endpoint gains `optionComparisonStarterCard`: true only when `optionComparison` is true
   **and** `StarterCardEnabled` is on. The card can never appear while the capability itself is off.
3. The `option-calls` card requires `optionComparisonStarterCard` instead of `optionComparison`.
4. Unchanged: intent matching, the answer, billing, the card in answers, Telegram, the read API, and the
   symbol-menu item (still shown for symbols with listed options while `OptionAnswers:Enabled` is on).
5. A change takes effect after the API container restarts with the new `.env`; no frontend rebuild.

## Alternatives considered

- **Frontend build variable (`VITE_…`)**: needs a rebuild and redeploy to change; rejected.
- **Remove the card from the code**: adding it back needs a release; rejected.

## Decision (owner, 2026-11-28)

The symbol-menu item «اختیار خرید» (in the ⋯ menu of a symbol, not on the home page) stays tied to
`OptionAnswers:Enabled`; it is not behind the new key.

## Acceptance criteria (draft)

- AC-01: With `OptionAnswers:Enabled=true` and `StarterCardEnabled=false`, the new-chat page on desktop and
  phone shows no «اختیار خرید» card, and typing «اختیار خرید اخابر» returns the option comparison.
- AC-02: With both on, the card is shown, as today.
- AC-03: With `OptionAnswers:Enabled=false`, the card is hidden whatever `StarterCardEnabled` says.
- AC-04: The availability response carries `optionComparisonStarterCard`; older clients ignore it.

## Plan (for Gate B)

1. Backend: option property and default; availability response field; unit test of the three combinations;
   integration test of the endpoint.
2. Frontend: capability type, availability hook, card `requires`; update starter-prompt tests.
3. Ops and docs: `docker-compose.yml`, `.env.example`, `appsettings.json` (API and Worker),
   `docs/option-comparison-answers.md`.
4. Verify: build, the touched unit/integration groups, Vitest for starter prompts and the phone list, lint.
