# Feature 140 — IME Daily Trades Raw Ingestion

This folder is a specification package for adding daily Iran Mercantile Exchange (IME) raw trade acquisition to FinancialCopilot.

## Files

- `UserStory.md` — product scope, functional requirements, acceptance criteria, boundaries, and future product direction.
- `Design.md` — architecture, persistence, idempotency, scheduling, API, resilience, and test design.
- `Tasks.md` — implementation slices/tasks and required verification.
- `SpecReview.md` — self-review of product/architecture risks and decisions.
- `source-fields.json` — machine-readable list of the 34 observed provider fields for implementation/test completeness.

## Key boundary

Feature 140 is **raw ingestion only**. It does not implement company mapping, analytics, monthly forecasting, alerts, AI tools, or user-facing IME questions.

## Repository limitation

The production repository was not available while authoring these specs. The implementing agent must first map conceptual components to existing repository conventions and reuse existing infrastructure wherever possible.
