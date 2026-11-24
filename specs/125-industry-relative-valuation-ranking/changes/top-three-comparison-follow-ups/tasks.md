# Implement the first three industry comparison follow-ups

Status: Approved by the owner on 2026-11-24 (Gate B)

- [x] Pass the typed result from the primary and fallback V2 execution paths to the existing follow-up context.
- [x] Register an industry comparison follow-up provider ahead of the generic single-symbol provider. Match the own-industry capability and a successful result with visible symbol rows. Build up to three localized actions from the first three symbol rows in display order. Skip an unusable or duplicate symbol within those rows. Route each action to the supported analysis capability for that symbol through the established action and validation conventions.
- [x] Verify the response and persisted actions for an own-industry comparison with three or more symbols, fewer than three symbols, an appended requested symbol, and no-data outcomes. Check that other comparison capabilities retain their current suggestions. Cover both V2 execution paths and confirm that the existing web and Telegram surfaces can display the actions.
- [x] Run the relevant backend and frontend tests. Verify that the implementation requires no data migration.

Verified on 2026-11-24 with 37 focused unit tests, two integration tests covering V2 execution, response persistence and the Telegram table image, all 24 architecture tests, and the web test for displaying and clicking a persisted suggested action.
