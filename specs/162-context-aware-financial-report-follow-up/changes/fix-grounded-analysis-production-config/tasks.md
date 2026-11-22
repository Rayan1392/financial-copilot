# Tasks: Grounded Analysis configuration in production (BUG-041)

Status: Gate B: Approved by the owner on 2026-11-22 (Gates A+B together, together with merge to prod and release tag)

## Feature Folder

`162-context-aware-financial-report-follow-up/changes/fix-grounded-analysis-production-config`

## Before Starting

- [x] Gates A+B approved by the owner on 2026-11-22.
- [x] Production behavior inspected: config sources in the image, api startup logs, production ledger (read-only).

## Implementation Tasks

- [x] Server `.env`: `GroundedAnalysis__Enabled=true` and `GroundedAnalysisPricing__Credits__*` = 8 / 12 / 16 / 25;
  api recreated; `/health` Healthy, 0 restarts (2026-11-22 13:22 UTC).
- [x] `.env.example`: Grounded Analysis flag, prices and OpenAI capabilities with `WebSearch`.
- [x] `docker/appsettings.Production.json` and the `docker-compose.yml` fallback: `WebSearch` on the OpenAI registration.
- [x] `docs/grounded-analysis-operations.md`: production configuration section.
- [x] `specs/bugs.md`: BUG-041; `specs/backlog.md`: production-config parity check.

## Tests

- [x] `GroundedAnalysisProductionConfigurationTests` (4 tests).

## Definition Of Done

- [x] Unit tests for Grounded Analysis green: `GroundedAnalysisProductionConfigurationTests` + `GroundedAnalysisPricingTests`,
  22 passed (2026-11-22).
- [ ] `CHANGELOG.md` entry and release tag; production deploy healthy.
