# User Story: <Title>

## Feature Folder

`NNN-<feature-name>`  (or `NNN-<feature-name>/changes/<kebab-change-name>` for a change to an existing feature)

## Layers / Area

Layers: backend | database | frontend | telegram-gateway | ops
Area: AI | Scanner | FinancialData | Billing | Authentication | Conversations | Memory | Notifications | Telegram | Administration | cross-cutting

## Priority

P0 | P1 | P2 | P3 | Reference | Dev

## Complexity

S | M | L | XL

## Depends On

None | NNN-other, NNN-other

## References

- `specs/NNN-<feature-name>/design-proposal.md` (approved at Gate A)
- `docs/domain-glossary.md` terms used: <terms>
- `docs/architecture.md` / other `docs/*.md` sections: <sections>
- Data model: <DbContext, entities, EF Core configurations>
- Related specs: `specs/NNN-other/...`

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As a <role>,
I want <capability>,
so that <business value>.

## Business Context

Why this matters for TahlilApp-AI users (web app, Telegram, API clients, admins); which user journey or operation it supports.

## Scope

- In scope item

## Out Of Scope

- Not in this spec (→ `specs/backlog.md` / later spec)

## Functional Requirements

1. ...

## Data / Schema

DbContext, tables, columns, keys, indexes, seed values touched, and the migration this needs. Use names from the EF Core model; never invent them.

## API (if any)

| Method | Route | Request | Response | Notes |
|---|---|---|---|---|

## AI Query Path (if any)

Capability, route, and tools on the V2 path (`specs/POLICY-V1-FREEZE.md`: no new V1 work). Outcome when data is missing or the question is unsupported.

## UI (if any)

Web screens (`src/frontend`), routes, components, Persian labels, RTL notes; Telegram messages and commands if the Telegram channel is affected.

## Non-Functional Requirements

Validation, authorization, performance, logging, caching, cancellation, timeouts and retries for outbound calls.

## Acceptance Criteria

- [ ] Testable criterion

## Assumptions

- Assumption (also logged in `specs/open-questions.md` if the owner must confirm)
