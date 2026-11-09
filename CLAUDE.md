# CLAUDE

read [README](README.md)

## Rules to read before coding

- `.claude/rules/clean-architecture.mini.md`
- `.claude/rules/implementing-domain-driven-design.mini.md`
- `.claude/rules/release-it.mini.md`
- `.claude/rules/the-pragmatic-programmer.mini.md`
- `.claude/rules/spec-driven-workflow.md`

## Feature Development Lifecycle

Follow `specs/FEATURE_DEVELOPMENT_WORKFLOW.md` for new features, changes to existing features,
bug fixes, and hotfixes.

1. Begin new feature requests with discovery and a proposed design. Inspect relevant code, data
   sources, existing specs, dependencies, and external APIs; record assumptions, questions, risks,
   and alternatives.
2. Present the design and wait for explicit **Gate A — Design Approval**. Do not create finalized
   `user-story.md` or `tasks.md` before this approval. A clearly marked `design-proposal.md`
   ("Proposed — not approved") may be created when a persistent draft is useful.
3. After Gate A, prepare detailed specifications and an implementation plan, present the plan and
   wait for explicit **Gate B — Implementation Plan Approval**.
4. Only after Gate B, implement within the approved scope, update tasks, verify acceptance criteria,
   run relevant builds/tests, and report deviations and remaining issues. Material design changes
   require renewed approval.
5. Approving one gate does not approve the next. A user may authorize both gates in one request only
   when that authorization clearly covers both.

Existing specs do not establish approval. Do not pick a feature to work on yourself; work only on
what the owner asks for. Open decisions live in `specs/open-questions.md` — do not decide them silently.
