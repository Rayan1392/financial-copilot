# Feature 161 Change — Accounting Validation and Cash Flow Analytics

Status: Gate A: Approved by the owner on 2026-11-15

## Intent

Enhance the existing Feature 161 report in place. Preserve its Codal-first operating/investing/financing totals, independent-quarter reconstruction, existing routes, and recent card-free responsive UI. This change adds explicit detail-coverage validation, financially accurate opening/closing labels, traceable residual disclosure, better-scoped metrics and evidence-based findings, richer report metadata, and more useful table/chart/export presentation.

The owner's attached request explicitly directs documentation followed by implementation and verification; Gate A and Gate B are approved for the bounded change described here.

## Discovery and accounting decisions

- The approved Codal basis remains authoritative: OCF = item 122 − item 121; item 128 remains a cross-check. Existing R1/R2 and quarter-balance continuity checks remain.
- Existing quarter construction already differences cumulative **flow** values and carries closing cash as a point-in-time balance. For independent quarters, item 33 after Q1 is selected from the predecessor's closing cash. Correct labels to make this behavior explicit; do not alter values.
- The current displayed “other items … (difference)” rows are computed as subtotal minus the currently displayed detail rows. They are analytical residuals, not source Codal rows. Rename and explain them; keep their source/formula provenance. A nonzero residual must not count as a successful detail reconciliation.
- Source mappings marked proposed or industry-limited remain non-displayable until authoritative evidence closes their open questions. Do not infer activity classification from a Persian title alone.
- The existing metric calculator already contains CapEx, FCF, coverage, cash conversion, operating cash flow / revenue, and net-borrowing calculations. Review their input eligibility instead of duplicating them. Only verified, eligible components may contribute. Report PPE-only calculation explicitly as simplified CapEx/FCF; withhold comprehensive FCF if the approved mapping set is incomplete.
- The existing result includes statement period, audit, representation, publication, fiscal-year, provider, and unit metadata. Display only values actually present; keep standalone/consolidated basis truthful.
- Analysis remains deterministic and evidence-linked. Do not add LLM arithmetic or qualitative solvency/investment verdicts.
- No database migration, data write, route/capability expansion, V1 work, secret/config change, commit, push, or deploy.

## Proposed behavior

### Validation model

Expose three independent results per selected period:

1. Main Codal totals: (122 − 121) + 121 + 123 = 124.
2. Cash balance: 33 + 124 + resolved FX(35) = 125.
3. Detail coverage by section: compare the reported section subtotal with the sum of verified, explicitly classified detail rows. Report complete, documented_difference, insufficient_detail, or inconsistent_source_data with expected/actual/difference and traceability.

The overall report badge must not imply detail completeness when only R1/R2 pass. Keep the existing legacy status for consumers and add typed validation dimensions additively. Missing or unknown detail classifications remain “unallocated/needs review”; they never automatically make an adjustment pass.

### Period-specific cash labels

- Standalone independent quarter: «مانده نقد و معادل‌های نقد ابتدای فصل» / «مانده نقد و معادل‌های نقد پایان فصل».
- Cumulative interim period: beginning-of-fiscal-year cash / end-of-period cash.
- Annual period: beginning-of-fiscal-year cash / year-end cash.

The labels change presentation only. Q1–Q4 opening/closing selection and the cash-balance equation remain unchanged.

### Detail rows and traceability

- Rename computed subtotal differences to «اقلام تفصیلی تخصیص‌نیافته (مابه‌التفاوت)» (or the same wording scoped to the section).
- Explain that the amount equals reported Codal subtotal minus verified displayed details. It is not a source line and does not prove detailed completeness.
- Preserve source item IDs, line IDs, statement IDs, and formula in an analyst disclosure.
- If catalog items are unclassified or have proposed placement, list their source labels/IDs and keep their values out of a section until placement is verified. Bound all extra persisted reads.

### Metrics and analysis

- Reuse the calculator and validate formulas, scope, sign, missing-value handling, denominator rules, version alignment, and industry eligibility.
- Display verified PPE-only CapEx and simplified FCF when the required value is present. Do not label a limited asset set as comprehensive FCF.
- Display comprehensive CapEx/FCF only when all applicable, approved components are available and classification is verified. Do not count investment purchases as CapEx.
- Retain and clearly label appropriate general-company indicators; hide irrelevant bank metrics. Do not treat deposits as debt or apply industrial liquidity interpretations to banks.
- Extend deterministic analysis facts with direction-aware previous-quarter and same-quarter/year comparisons where comparable observations are available. Explain FX, financing, opening/closing cash, CapEx/FCF, and cash conversion only from aligned verified inputs.
- Preserve unknown/missing values rather than coercing them to zero. Avoid favorable/unfavorable judgment from a cash-flow sign alone.

### Presentation and export

- Header: company name/ticker, report view and basis, period end/fiscal year, audited/unaudited and represented status, publication date, provider, and unit. Unknown metadata is omitted or labeled unknown.
- Validation shows main-total status, cash-balance status, and detail coverage separately; ordinary users see a concise summary, analysts can expand check arithmetic, mappings, and source trace.
- Keep the card-free summary approved in the preceding UI change. Show a structured, readable analytical indicators section and evidence-based «جمع‌بندی تحلیلی ساپیو».
- Keep five independent quarters, chart/table agreement, RTL, Persian digits, negative signs, and responsive no-overflow behavior. The latest-period absolute and valid percent change is a distinct comparison field at wide sizes; compact/mobile views retain the change without horizontal scrolling.
- Do not put closing cash snapshots on the flow chart's axis. If shown, use a separately labeled secondary visualization.
- Keep the existing PNG pipeline and improve layout density, section ordering, readability, and export contents. Add print styles only if the existing report surface supports them without introducing a new PDF stack.

## Evidence limitations known at discovery

- Existing T01 Development survey is read-only and covers cash-flow catalog counts and fixtures 255/452 (فلوله/وبملت); it explicitly says this is not live Feature 161 validation and leaves P mappings open.
- The attached request provides reference values for خودرو Q2 1405 but says they must be verified against Development data. Do not turn screenshot approximations into golden live-data assertions.
- The local Development database will be queried in an explicit read-only transaction. If requested companies or statements are absent, record that limitation and rely only on labeled fixtures.
- No Iran Khodro screenshot was present as a separate image in the pasted text attachment. Use the attached numerical reference and existing repository visual evidence; do not treat synthetic browser screenshots as company validation.

## Acceptance criteria

- Independent-quarter values are unchanged; cash opening/closing labels match the selected period mode.
- R1 and R2 results are independently understandable; detail coverage is never inferred from them.
- Residual differences are labeled as calculated unallocated amounts with formula and source provenance, and cannot satisfy coverage.
- Detail coverage exposes complete, documented-difference, insufficient-detail, and inconsistent-source states.
- Only eligible verified CapEx components enter comprehensive metrics. Simplified PPE-only FCF is explicitly labeled; unavailable calculations state why in Persian.
- Existing bank template behavior and Codal mapping remain; unsupported bank metrics stay hidden.
- Analytical facts cite comparable report periods/metric IDs and remain deterministic, neutral, Persian, and supported by figures.
- Header, indicators, notes, chart, table, PNG, and any existing print view use the same unit, period and verification data.
- No horizontal overflow at the existing browser widths and no regression in Income Statement, Balance Sheet, V1-freeze, or existing cash-flow routes.
- Verification is fixture-based unless exact live Development statements are available and safely readable; no claims of live values without query evidence.

## Dependencies and risks

- Extending the typed report contract requires coordinated backend, text renderer, frontend, fixtures, and persistence/replay compatibility. Prefer additive nullable fields and increment the Feature 161 contract version; no schema migration.
- Full source-item coverage requires bounded reads scoped to already-selected statement IDs and provider/catalog. Enforce a hard item-count limit; return coverage as insufficient when the limit is exceeded.
- Accounting classification of unresolved items and bank treatment require evidence from the existing owner-approved mapping matrix or authoritative source, not text heuristics.
- A dedicated comparison column competes with five quarter columns. At narrow widths use the existing stacked mobile row presentation.
- Multi-page PDF generation is out of scope because this feature currently exports PNG and the repository has no shared PDF pipeline.

## References

- Existing accepted specification: [user-story.md](../../user-story.md), [mapping matrix](../../design-proposal-mapping-matrix.md), [UX design](../../design-proposal-ux.md).
- Existing mapping and T01 evidence: [mapping matrix](../../design-proposal-mapping-matrix.md), [T01 evidence](../../evidence/T01-evidence.md).
- Iranian Accounting Standard No. 2, revised 1397 (readable copy): https://dl.hesabdanan.com/library/standards/standard%202-%2097.9.12.pdf
