# User Story: Read an Empty Profile Before TahlilApp Data Exists

## Feature Folder

`139-tahlilapp-session-exchange/changes/profile-get-without-tahlilapp-row`

## Layers / Area

Layers: backend
Area: Authentication

## Priority

P2

## Complexity

S

## Depends On

139-tahlilapp-session-exchange

## References

- `design-proposal.md` (Gate A approved)
- `specs/139-tahlilapp-session-exchange/Design.md` §10.2 and §22.4
- `src/backend/FinancialCopilot.API/Controllers/ProfileController.cs`
- `src/backend/FinancialCopilot.API/Contracts/OwnedIdentityContracts.cs`

## User Story

As an authenticated web app user without synced TahlilApp profile data, I want to retrieve my profile response so account clients can render the empty state.

## Scope

- Return the existing profile response with nullable fields when no `UserProfiles` row exists.
- Preserve the masked response for users with a profile row and keep `Cache-Control: no-store`.
- Preserve 404 for reveal requests when the requested value is absent.

## Out Of Scope

- Database or migration changes.
- Changes to profile provisioning, reveal behavior, or frontend rendering.

## API

| Method | Route | Response | Notes |
|---|---|---|---|
| GET | `/api/auth/v1/profile` | Existing `OwnProfileResponse`, all nullable fields may be `null` | Authenticated caller only; `no-store` |

## Acceptance Criteria

- [x] An authenticated user with no profile row receives 200 and all six profile fields are JSON `null`.
- [x] The empty response includes `Cache-Control: no-store`.
- [x] An authenticated user with a profile row still receives the existing full names and masked identifiers.
- [x] Unauthenticated profile requests remain 401; reveal behavior for an absent value remains 404.
