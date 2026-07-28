# Frontend Assisted Query Metadata

## User Story

As a web user, I want optional assisted filter controls backed by governed metadata so I can
discover supported symbols, industries, metrics, and periods without bypassing the natural
language AI facade.

## Current Gap

The prototype displays a filter-writing button without behavior. The backend already exposes
`GET /api/ai/v1/metadata/metrics`, but period, symbol, and industry metadata endpoints do not
exist.

## Scope

- Connect governed metric metadata.
- Add lightweight period, symbol, and industry discovery endpoints.
- Implement assisted controls that compose a user-visible prompt submitted through the AI
  facade.

## Acceptance Criteria

1. The frontend reads metrics from `GET /api/ai/v1/metadata/metrics`.
2. The backend exposes authenticated period, symbol, and industry metadata endpoints with
   bounded search where applicable.
3. Assisted controls never execute scanner parse or scanner execution APIs directly.
4. The final action submits a normal user-visible message to `POST /api/ai/v1/query`.
5. Persian and English labels come from governed backend metadata where available.
6. Empty, loading, and search-limit states are handled.
7. Backend integration tests and frontend lint/build checks pass.

## Out Of Scope

- A public scanner DSL endpoint.
- Frontend-defined metric formulas.
- Replacing free-text chat.

