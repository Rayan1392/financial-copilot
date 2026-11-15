# User Story: Feature 161 Accounting and Analytics Enhancement

Status: Gate A: Approved by the owner on 2026-11-15

Layers: Application, Infrastructure read port, typed report contract, web UI, text and PNG renderers

## Story

As an investor or financial analyst, I need the cash-flow report to distinguish arithmetic reconciliation from detail completeness, show period-correct cash labels, disclose unallocated differences, and calculate only supported metrics, so that a balanced subtotal is not mistaken for fully verified detail and the analysis remains traceable to Codal records.

## Acceptance criteria

- [ ] Separate main-total reconciliation, opening/closing cash reconciliation, and detailed-item coverage for each selected period.
- [ ] Display the correct opening and closing balance labels for independent-quarter, cumulative interim, and annual reports without changing source values.
- [ ] Label calculated subtotal differences as unallocated analytical differences, explain the formula, expose provenance, and do not count them as verified detail.
- [ ] Identify unclassified source items when available; do not infer their section from titles or use them as a balancing plug.
- [ ] Calculate CapEx and FCF only from verified applicable source components; distinguish comprehensive from PPE-only simplified calculations.
- [ ] Preserve positive, negative, zero, unavailable, unverified, bank, and scope-specific states.
- [ ] Show only meaningful indicators, with Persian reasons for unavailable values, and provide deterministic evidence-backed cash-flow facts.
- [ ] Display available company/period/audit/scope/provider metadata without inventing missing values.
- [ ] Keep all five independent quarters, appropriate trend chart semantics, responsive RTL table, and card-free summary.
- [ ] Align on-screen report, text response, PNG export, and supported print presentation.
- [ ] Existing routing and adjacent statement reports remain behaviorally unchanged.
- [ ] Run targeted accounting, integration, frontend, routing, and visual checks. Distinguish actual Development verification from synthetic fixtures.
- [ ] No migration, source-data write, config/secret edit, commit, push, merge, or deployment.
