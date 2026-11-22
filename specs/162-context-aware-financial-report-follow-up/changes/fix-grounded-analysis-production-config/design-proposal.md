# Design: Grounded Analysis configuration in production (BUG-041)

Status: Approved — Gates A+B approved by the owner on 2026-11-22 («باگ فیکس ثبت کن و فیکس کن، مرج کن روی prod و تگ بزن»).
No material design change: the correction restores the approved behavior of change `sol-deep-analysis-web-search`.

## Layers / Area

ops · AI, Billing

## Defect

Version 1.19.0 shipped Grounded Analysis (162/163 change `sol-deep-analysis-web-search`). Its configuration lives only in
`src/backend/FinancialCopilot.API/appsettings.json`: `GroundedAnalysis:Enabled`, the profiles, and the approved prices in
`GroundedAnalysisPricing:Credits` (8 / 12 / 16 / 25). `docker/api.Dockerfile` has removed `appsettings.json` from the
image since 2026-07-28. Production therefore reads only `docker/appsettings.Production.json` and the server `.env`, and
neither had these settings. Effects:

- `GroundedAnalysis:Enabled` fell back to its code default `false`, so Report Discussion and Feature 163 ran their
  earlier paths. The production ledger holds no `AiQuery.GroundedAnalysis` entry and no `gpt-6.2-sol` call from
  1.19.0 to 2026-11-22.
- The prices have no code default (0). On 2026-11-22, setting `GroundedAnalysis__Enabled=true` on the server alone made
  price validation fail at startup. The api restarted in a loop and nobody could sign in until the flag was rolled back.
- The production defaults for the OpenAI registration (`docker/appsettings.Production.json` and the
  `docker-compose.yml` fallback for `AiModelProviders__Providers__0__Capabilities`) lack `WebSearch`. Web tiers worked
  only because the server `.env` overrides that key.

## Correction

1. Server `.env` (done 2026-11-22 13:21 UTC, backup `.env.backup-before-bug-041-*`): `GroundedAnalysis__Enabled=true`
   and the four `GroundedAnalysisPricing__Credits__*` amounts. The technical options keep their code defaults, which equal
   `appsettings.json`.
2. `.env.example` documents the same keys and the OpenAI capabilities key.
3. `docker/appsettings.Production.json` and the compose fallback add `WebSearch` to the OpenAI capabilities, matching
   `appsettings.json`.
4. Regression test `GroundedAnalysisProductionConfigurationTests` builds the configuration the api container sees
   (`docker/appsettings.Production.json` + `.env.example` keys). It checks that the flag is on, the options and prices
   validate, the prices equal the approved amounts in `appsettings.json`, and the provider has `WebSearch`.
5. `docs/grounded-analysis-operations.md` states where production configuration comes from.

## Acceptance criteria

- AC-1: Production api starts with `GroundedAnalysis:Enabled=true` and the approved prices; `/health` is Healthy.
- AC-2: `.env.example` and the production image defaults produce a configuration that passes the Grounded Analysis
  option and price validation, with `WebSearch` on the OpenAI registration (automated test).
- AC-3: The prices in `.env.example` equal `GroundedAnalysisPricing:Credits` in `appsettings.json` (automated test).

## Risks

- Grounded Analysis now really runs in production: costs follow `docs/grounded-analysis-operations.md` (monitor
  `grounded_analysis.cost_usd`). The reverse-proxy limit of at least 120 s is outside the repository and is not verified here.
- The prices are duplicated in `appsettings.json` and the server `.env`. The test keeps `.env.example` in line, but it cannot see the
  server `.env`.

## Out of scope

- The stale `REPORT_DISCUSSION_ENABLED` key in `.env.example` (compose does not read it).
- A general check that every new `appsettings.json` section has a production counterpart (backlog).
