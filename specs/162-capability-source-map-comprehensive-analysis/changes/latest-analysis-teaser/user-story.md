# User Story: Feature 162 change — latest analysis with a partial preview

## Feature Folder   `162-capability-source-map-comprehensive-analysis/changes/latest-analysis-teaser`
## Layers / Area    backend · AI, FinancialData
## Priority         P2
## Complexity       S
## Depends On       162
## References       `design-proposal.md` (this folder), `../../design-proposal.md` §6, `../../user-story.md` FR4

## User Story

As TahlilApp's partner,
I want Sapio to show only the start of the latest analysis for a ticker and send the reader to TahlilApp for the rest,
so that the paid analysis is promoted, not given away.

## Scope

Latest analysis only; partial preview cut at the source; publish date; read-more line; configurable preview numbers.

## Out Of Scope

Schema, API shape, billing, matching, per-article links.

## Acceptance Criteria

See `tasks.md` (AC1–AC5).

## Assumptions

`summary` from `GET /blogs?filter[type]=analysis` is the whole analysis text (owner, 2026-11-10).
