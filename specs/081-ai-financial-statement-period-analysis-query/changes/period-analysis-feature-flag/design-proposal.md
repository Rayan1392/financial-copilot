# Design Proposal: Feature 081 on/off switch; its questions go to the Feature 163 offer when off

Status: Gate A: Approved by the owner on 2026-11-20 (option B)

## Problem

«سود و زیان شاراک را تفسیر و تحلیل کن» in a new conversation got Feature 081's fixed summary (numbers, percent
change and ✅, no interpretation, 2 credits). The owner wanted Feature 163 to handle it. Feature 163 runs only on
`Unsupported` turns (163 FR-1) and never replaces an existing answer (163 FR-13). «سود و زیان» is one of 081's
general phrases (`FinancialStatementAnalysisIntentRules`), and the governed interpreter picks
`financial_statement_period_analysis` for a statement phrase plus «تحلیل». So 081 answers and 163 never runs.

## Options presented

- **A**: switch 081 off and let its questions fall through. They would most likely land on the statement table
  (154/083) or the comprehensive analysis. 163 would not run.
- **B (chosen)**: switch 081 off. A turn that 081 would have answered ends `Unsupported` with its own reason code,
  so 163 offers its analysis (8/12/16/25 credits after «تحلیل کن»).
- **C**: like B, but only for analysis or interpretation wording. Other 081 questions go to the statement table.

## Design (B)

1. Options `FinancialStatementPeriodAnalysis:Enabled`. The code default is `true` (a missing key keeps today's
   behaviour), and `appsettings.json` sets `false`.
2. V2 workflow. 081 runs at two places: the semantic dispatch of capability `financial_statement_period_analysis`,
   and the deterministic check in the agent step. When the flag is off, each place ends the turn the same way:
   - The Feature 141 fallback is tried first, as for every other non-answer (163 FR-13 order).
   - Otherwise the outcome is `Unsupported` with reason code `financial_statement_period_analysis_disabled`, the
     generic Persian unsupported text, and completion status `ValidationFailed` (zero charge).
   - The reason code is not a provider-policy code, so 163 eligibility (FR-1) accepts it.
3. The result step no longer labels such a turn `FinancialStatementPeriodAnalysis` by keyword, so diagnostics and
   follow-ups see an ordinary unsupported turn.
4. Billing. With the flag off, the operation selector stops choosing `AiQuery.FinancialStatement` for 081-only
   requests. The reservation uses the default operation, so a user with a small balance still reaches the offer.
5. V1 is not changed (`specs/POLICY-V1-FREEZE.md`). Production runs V2.

### Deviation from the text first shown to the owner

The first proposal also listed «capability `Enabled=false` in the catalog so semantic routing cannot pick it».
That would send these questions to the statement table (option A behaviour), against the approved option B.
The capability therefore stays recognisable and is stopped where it would run. Its questions reach 163, and the
catalog and interpreter stay unchanged.

## Risks

- A question that today gets 081's free-of-model summary now costs 8–25 credits if the user confirms the offer,
  and 0 credits otherwise (it was 2 credits).
- Follow-up chips or help text that suggest «تحلیل صورت مالی X» now lead to the 163 offer. This is acceptable
  under option B.
- 163's own definition of done is not ticked yet (T01, T05, T12, T14 partial; T16 open). This change uses only
  163's implemented trigger (FR-1, T04 done) as it already runs in production.
