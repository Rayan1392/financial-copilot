# Profile GET Without a TahlilApp Profile Row

Status: Gate A: Approved by owner on 2026-11-13

## Discovery

`GET /api/auth/v1/profile` is registered and requires authentication. The profile store returns `null` when the authenticated user's `UserProfiles` row is absent, and the controller currently maps that case to 404. TahlilApp launch provisions the row, but an otherwise valid `WebAppUser` can have no TahlilApp profile row. The existing response contract makes profile fields nullable, and Feature 139 requires signed-in users to retrieve their own profile.

## Approved design

Keep the route, authentication, self-only subject lookup, privacy behavior, and existing response contract. When the store has no profile row, return `200 OK` with the existing profile response fields set to `null` and `Cache-Control: no-store`. Leave `POST /api/auth/v1/profile/reveal` unchanged: a missing profile value remains 404.

No database, migration, frontend, or identity-provisioning changes are included.

## Risks and alternatives

- Returning an empty profile object may require API consumers to handle nullable fields; the fields are already nullable by contract.
- Returning 404 preserves the current behavior but keeps an authenticated profile read unusable for users without TahlilApp data, so it is not selected.

## Approval

Gate A design approval and Gate B implementation-plan approval were explicitly granted by the owner in the conversation on 2026-11-13.
