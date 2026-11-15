# Implementation Plan: Feature 161 Accounting and Analytics Enhancement

Status: Gate B: Approved by the owner on 2026-11-15

## Scope and implementation boundary

This is an incremental change inside Feature 161. Its user request explicitly authorizes implementation after documenting the approved enhancement. Reuse existing feature services, contracts, reader, chart library, message rendering, and PNG canvas. No new feature folder, migration, source-data write, route, V1 change, PDF dependency, unrelated configuration change, commit, push, merge, or deploy.

Implementation must stop for renewed design/plan approval if repository evidence requires a material accounting reclassification, a schema migration, a different pipeline, or an unbounded query.

## Existing components to reuse

- Application: CashFlowLineRegistry, CashFlowStatementBuilder, CashFlowPeriodSelector, CashFlowReconciliation, CashFlowMetricsCalculator, CashFlowAnalysisFacts, CashFlowReportQueryUseCase.
- Infrastructure: EfCoreCashFlowStatementReader, CashFlowReportTextRenderer; bounded PostgreSQL read port.
- API/carrier: existing typed CashFlowReportResult nested in the financial statement table result; persistence/replay contract.
- Frontend: cash-flow-report.tsx, cash-flow-report-model.ts, existing Recharts primitives, cash-flow-report-image.ts, PNG branding helpers, current card-free responsive rendering.
- Regression references: Feature 144 quarterly income statement and Feature 154 balance-sheet table/header styles; do not edit those features' behavior.

## Tasks

### Group A — Accounting integrity

- [x] **A01 — Audit period semantics and source mapping.** Review period selection/building, catalog source titles/IDs, item-128 cross-check, and existing Codal mapping evidence. Confirm old/new subtotals and financial-industry treatment remain unchanged unless authoritative evidence closes a documented open mapping. Record findings.
- [x] **A02 — Correct opening/closing labels.** Make report row labels depend on selected period kind: independent quarter, cumulative interim, or annual. Keep Q1–Q4 balance selection unchanged; add regression cases for each.
- [x] **A03 — Separate validation dimensions.** Add additive typed fields for R1 main totals, R2 cash balance, and per-section detail coverage, preserving the legacy status for existing consumers. Surface expected, actual, difference, and source period. Bump the report contract version; do not change persistence schema.
- [x] **A04 — Make detail coverage fail closed.** Compare each Codal subtotal only with verified, explicitly classified detail rows. Identify unclassified/proposed items through a bounded read. Mark residual differences as documented/unallocated; missing/ambiguous items as insufficient; conflicting rows as inconsistent. Never let a calculated residual pass coverage.
- [x] **A05 — Disclose residual provenance.** Rename investing/financing residual rows as unallocated differences, include the computation and original item/statement/line references, and exclude them from the detail-completeness numerator.
- [x] **A06 — Validate database bounds and replay compatibility.** Add reader bounds and old-payload deserialization coverage. Any cap overflow returns a data-quality state rather than a truncated “complete” result.

### Group B — Financial analytics

- [x] **B01 — Audit metric inputs.** Reconcile each current formula against its definitions, registry verification, sign convention, scope, period alignment, denominator behavior, missing/zero rules, version alignment, and general/banking policy. Do not duplicate existing metrics.
- [x] **B02 — Correct CapEx and FCF eligibility.** Sum only verified applicable asset-purchase components. Provide clearly labeled PPE-only simplified CapEx/FCF when sufficient; suppress comprehensive FCF unless the full applicable mapping set is verified. Exclude investment purchases.
- [x] **B03 — Extend deterministic comparisons/findings.** Add current versus previous independent quarter and same-quarter prior-year facts only when company, provider, fiscal year, period, scope, version and comparability match. Explain sign changes neutrally; carry evidence metric IDs and period IDs.
- [x] **B04 — Verify financial interpretation.** Keep net principal borrowing separate from total financing flow; align income-statement conversion inputs; suppress industrial interpretations/ratios for banks and preserve bank-specific FX handling.

### Group C — Report UI/UX

- [x] **C01 — Improve report header.** Display only available company, ticker, report mode/basis, period end, fiscal year, audit/restatement, publication, provider and unit metadata.
- [x] **C02 — Present validation dimensions.** Replace the ambiguous single verification claim with compact accessible status lines and an expandable analyst view of checks and coverage.
- [x] **C03 — Redesign indicators and summary.** Keep the card-free five-value strip, show supported metrics in a readable structured section, remove repetitive em-dash metrics, disclose definitions and applicability, and present simplified labels when needed.
- [x] **C04 — Improve analytical summary.** Render deterministic «جمع‌بندی تحلیلی ساپیو» using only aligned metric values and calculation facts; no generated arithmetic, verdicts, or unsupported risk claims.
- [x] **C05 — Refine chart and comparison table.** Three activity-flow series with zero baseline and Persian quarter labels; do not mix closing-cash balance with flows on one axis. Provide a clear latest-period absolute/valid-percent comparison column on wide layouts and retain no-overflow mobile stacking.
- [x] **C06 — Add accounting/data-quality notes.** Expose reconstruction method, missing periods, source IDs, mapping caveats, unallocated items and reconciliation arithmetic in an accessible disclosure.
- [x] **C07 — Improve print/PNG export.** Add full metadata, status, metrics, chart/table and source notes in a compact readable hierarchy; preserve amount readability, avoid large empty blocks, and do not add a PDF stack.

### Group D — Validation and review

- [x] **D01 — Accounting unit tests.** Cover labels/modes, R1/R2, detail coverage states, residual behavior, FX, quarter continuity, verified CapEx, simplified/comprehensive FCF, metrics, signs, zeros, missing inputs, and conflicts.
- [x] **D02 — Historical and industry tests.** Cover quarter reconstruction, year-over-year alignment, periods with gaps/restatements, and general/bank templates including FX.
- [ ] **D03 — Integration and contract tests.** Contract replay and bounded-reader tests pass, but the PostgreSQL integration test was skipped because no isolated test connection is configured. Endpoint, billing and provider integration remain unverified.
- [x] **D04 — UI/regression tests.** Cover metadata, statuses, ratio units, text reasons, RTL, Persian digits, chart/table agreement, compact mobile, PNG export, V1 freeze, Income Statement, and Balance Sheet.
- [x] **D05 — Development-data review.** In a read-only transaction, locate and inspect خودرو، فولاد، وبملت source versions and values if present. Verify exact account calculations and source IDs. If absent, record absence and do not treat fixture estimates as live checks.
- [ ] **D06 — Visual and independent review.** Desktop/mobile/dark/PNG output was inspected and recorded in `evidence/browser/visual-signoff.md`; independent peer review remains open.
- [x] **D07 — Implementation report.** Record modified files, builds/tests, Development access outcome, screenshots, open mappings/coverage, and Git state. Do not claim full Feature 161 completion while existing baseline tasks remain open.

## Dependencies and verification order

1. A01 must close before deciding whether any mapping/metric component is eligible.
2. A02–A06 update the internal result model and typed contract before B/C work.
3. B01–B04 depend on verified model inputs.
4. C01–C07 follow the final data contract; D01–D04 land alongside implementation.
5. D05 is read-only and may run after the contract/accounting work; D06/D07 are final.

## Acceptance evidence

- Targeted dotnet test suites for CashFlowReport161, PostgreSQL reader and routing/persistence tests.
- Focused frontend Vitest, ESLint/typecheck, and production build.
- Browser checks and visual evidence at 320, 360, 390, 768, 1024, 1280, and 1440 CSS pixels; inspect desktop/mobile export.
- Read-only SQL evidence or an explicit note that the target symbol data is not available.
- All scope changes and skipped tests documented; no new migrations or deployments.

## Risks

- Existing mapping matrix has unresolved P items (including biological assets and bank-limited intangibles); do not silently promote them.
- A subtotal difference can result from incomplete taxonomy, hidden but valid items, or inconsistent source. Exact residual arithmetic alone does not establish its cause.
- New coverage fields cross persisted message payload and API boundaries; preserve old stored messages and test replay.
- Cash-flow signs do not map directly to “good”/“bad”; analysis language must remain neutral and evidence-based.
