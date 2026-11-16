# Fix Report Discussion analysis answer failures (BUG-031)

Status: Gate A: Approved by the project owner on 2026-11-15

**Layers / Area:** backend · AI

**References:** `specs/162-context-aware-financial-report-follow-up/user-story.md` (FR-4 budget, FR-7 grounding, FR-12 observability), `src/backend/FinancialCopilot.Application/AI/ReportDiscussion/`, `src/backend/FinancialCopilot.Infrastructure/AI/ModelProviders/OpenAiHostedAiModelTransport.cs`.

## Problem

On 2026-11-15 two analysis follow-ups on the فاذر balance sheet («جمع کل دارایی‌ها را تحلیل کن», «موجودی نقد را تحلیل کن») reached Report Discussion but ended in zero-credit fallbacks: `Unverified` and `ProviderUnavailable`. The API container had been recreated, so the `Report discussion evaluated` log lines were gone. The causes below come from reading the code, not from captured model output.

1. **Output cap.** FR-4 caps the reply at 650 output tokens. A Persian analysis answer in the JSON envelope often needs more (about 2.5 characters per token, schema allows 4,000 characters). On `incomplete_details.reason = max_output_tokens` the OpenAI transport throws `ModelOutputTruncated`, which the handler shows as "temporarily unavailable". The model was switched to `gpt-6-luna` the same day. If it spends output tokens on reasoning, the budget is smaller still.
2. **Evidence ids read as numbers.** The prompt asks the model to separate facts, calculations and interpretation. Models often cite `(F45)` or `(C14)` inline. The grounding regex matches the digits inside these ids. `45` matches no fact, so the whole answer is rejected. Ids up to 12 pass by accident, so short reports hide the defect.
3. **Diagnostics gap.** For a provider failure the log `Reason` holds only the status (`InvalidStructuredOutput`), not the provider code (`ModelOutputTruncated`), so truncation cannot be told apart from other structured-output failures.

## Proposed correction (no material design change)

- **Output budget:** raise the `ReportDiscussion:MaxOutputTokens` default from 650 to 1,600 (inside the existing 100–2,000 validation range). Raise `ModelTimeoutSeconds` from 20 to 30 to match the longer output. Tell the model to keep the answer within about 250 Persian words. FR-4's 650 figure becomes 1,600; the budget mechanism is unchanged.
- **Grounding:** before extracting numbers, remove evidence-id tokens (`F`/`C` followed by Latin or Persian digits) from the answer. Tell the model to put ids only in `usedEvidenceIds`, never in the answer text. All other FR-7 rules stay as they are.
- **Prompt:** when a useful figure is not among the facts or calculations (a share of total assets, an absolute change), describe it in words without a number instead of computing it. This restates the existing "never compute" rule for analysis questions.
- **Diagnostics:** carry the provider code into the failure reason (`InvalidStructuredOutput:ModelOutputTruncated`). Logs still hold no report values or answer text.

## Out of scope (owner decision, recorded in backlog)

- Adding more balance-sheet calculations (each row's share of total assets, absolute changes between periods). That would let analysis answers quote such numbers, but it enlarges the prompt and adds new computed data. It is a design change.
- A per-call reasoning-effort setting in the model transport. It affects every workload.

## Risks

- Higher cost per answered turn: at most 1,600 output tokens instead of 650. This is still one model call, and failures stay at zero credits.
- Stripping `F`/`C` + digits could hide a real number written straight after a Latin F or C. Persian answers do not write numbers that way.
- The cause is inferred. Verification therefore includes a live run on local Development with the same questions.

## Immediate mitigation (no code, owner's choice)

The output cap and timeout are configuration. On the server, `.env` can carry `ReportDiscussion__MaxOutputTokens=1600` and `ReportDiscussion__ModelTimeoutSeconds=30`, followed by `docker compose up -d --force-recreate api`. This addresses cause 1 only.
