# Architecture Decision Records

One file per architecture decision made from 2026-11-09 onward (decision OQ-005 in `specs/open-questions.md`). Earlier decisions stay where they are (feature specs, `docs/architecture.md`, `specs/POLICY-V1-FREEZE.md`); link to them from a new ADR instead of moving them.

- File name: `NNNN-<kebab-decision-name>.md`, numbered from `0001`. Numbers are not reused.
- Write an ADR when a decision changes layer dependencies, a bounded-context boundary, a data source of truth, a persistence model, or a cross-feature policy.
- An ADR proposed inside a feature is reviewed with that feature's `design-proposal.md` (Gate A in `specs/FEATURE_DEVELOPMENT_WORKFLOW.md`). Its status changes only after the owner's explicit decision.
- A superseded ADR is not edited or deleted; set its status to `Superseded by NNNN` and write a new one.

## Template

```markdown
# NNNN — <Decision title>

Status: Proposed | Accepted by <owner> on <YYYY-MM-DD> | Superseded by NNNN
Related: specs/NNN-<feature-name>/, other ADRs

## Context

The forces, constraints and facts that make a decision necessary.

## Decision

What is decided, stated in full sentences.

## Consequences

What becomes easier or harder, what must change, and what is ruled out.
```
