# Tasks: Profile GET Without a TahlilApp Profile Row

Status: Gate B: Approved by owner on 2026-11-13

## Feature Folder

`139-tahlilapp-session-exchange/changes/profile-get-without-tahlilapp-row`

## Before Starting

- [x] Gate B approval recorded above.
- [x] Feature 139 is implemented; this is a scoped correction to its existing profile read.
- [x] Read the change user story, proposal, and Feature 139 profile design.
- [x] No blocking open question applies to this behavior.

## Implementation Tasks

### 1. Profile response behavior

- [x] Map an absent profile row to the existing nullable profile response in the API controller; retain `no-store` and leave reveal unchanged.
- [x] Add integration coverage for the empty response and existing privacy/auth behavior.
- [x] Disable unrelated TahlilApp top-up startup validation in this integration fixture so the focused suite can start without partner signing keys.
- [x] Add a Persian user-facing line under `CHANGELOG.md` → `Unreleased`.

## Tests

- [x] Run the focused `OwnProfileIntegrationTests` suite.
- [x] Build the backend API project in Release configuration.

## Definition Of Done

- [x] Acceptance criteria in `user-story.md` are verified.
- [x] No database migration is required.
- [x] Focused integration tests and API Release build pass.
