# Tasks

1. Audit the existing metric metadata response against assisted-control needs.
2. Add `GET /api/ai/v1/metadata/periods`.
3. Add bounded `GET /api/ai/v1/metadata/symbols?search=&limit=`.
4. Add bounded `GET /api/ai/v1/metadata/industries?search=&limit=`.
5. Add typed frontend metadata API functions through the authenticated bridge.
6. Implement assisted prompt composition without direct scanner invocation.
7. Add endpoint authorization/search-bound tests and frontend lint/build verification.

## Implementation Status

Completed on 2026-06-02. Assisted controls compose a visible prompt in the existing textarea;
the user still submits that prompt only through `POST /api/ai/v1/query`.
