# Feature 150 — Shared Chat Routing: Natural Language Intent and Entity Resolution Fixes

**Status:** SPEC_CREATED, not implemented. Spec-only deliverable; no code change in this feature.

## Why this is its own feature, not part of 148

Feature 148 (CyclicalWaves v2) proved, through its own end-to-end acceptance run (28/28 tests passing,
`specs/148-cyclicalwaves-v2-independent-financial-data-provider/ImplementationReport.md` §12.3), that six of
its ten exact-wording Chat acceptance questions are stopped by defects in the **shared** Chat routing layer —
code that existed before Feature 148, is not owned by it, and behaves identically regardless of which
financial-data provider (NoavaranAmin or CyclicalWaves) answers the question. The Product Owner decided
(2026-10-31) that Feature 148 closes with this limitation documented rather than being expanded to repair
routing it does not own. This feature is the separate, scoped place to fix that routing.

## Dependency on Feature 148

None at the code level. This feature does not require Feature 148's CyclicalWaves v2 provider to exist; every
defect below is reproduced and will be fixed identically whether `FinancialData:ChatProvider` is
`NoavaranAmin` or `CyclicalWaves`. The dependency is historical: Feature 148's acceptance run is what surfaced
and characterized these defects, and its `Feature148ChatProviderE2ETests.cs` already pins several of them as
characterization tests (asserting the current, wrong behavior) that this feature's fix will need to update to
assert the corrected behavior.

## Current behavior vs. expected behavior

### R1 — LLM routing suggestion discarded whenever `periodCount` is `null`

**Current:** `LlmQueryInterpretationProposalProvider.Parse` in
`src/backend/FinancialCopilot.Application/AI/Orchestration/CapabilityInterpretationGovernance.cs:269` reads the
model's structured output with:

```csharp
root.TryGetProperty("periodCount", out var countProperty) && countProperty.TryGetInt32(out var count) ? count : null,
```

The JSON schema the model is instructed to follow (`SchemaJson`, same file, line ~185) declares
`"periodCount": { "type": ["integer", "null"], ... }` and the system prompt explicitly tells the model
"Use null for absent scalar fields" (same file, line ~215). But `JsonElement.TryGetInt32()` throws
`InvalidOperationException` when the element's `ValueKind` is `Null` — it does not return `false` for a
null element the way it does for, say, a non-numeric string. This was verified empirically (not assumed):
`JsonDocument.Parse("""{"periodCount": null}""").RootElement.GetProperty("periodCount").TryGetInt32(out _)`
throws `InvalidOperationException: The requested operation requires an element of type 'Number', but the
target element has type 'Null'.`

The `catch (Exception exception) when (exception is JsonException or InvalidOperationException)` block a few
lines below (line ~277) catches this and rethrows it as
`AiModelProviderException(AiExecutionStatus.InvalidStructuredOutput, "invalid_query_frame", ...)`. The caller
treats this as "the model's proposal is unusable" and falls back to the deterministic interpreter
(`DeterministicCapabilityInterpreter`) alone — silently, with no log distinguishing "model declined" from
"model's own correctly-schema-compliant answer crashed our parser."

**Expected:** A proposal with `"periodCount": null` is valid per the schema and must parse to `PeriodCount =
null`, not throw. Any request that omits a period count (which is most of them) must not lose the model's
entire proposal.

**Impact:** This is the amplifier behind R2–R6: real production traffic uses a real LLM, which follows its own
"use null for absent scalar fields" instruction routinely, so in practice almost every proposal is discarded
and Chat runs on the deterministic interpreter alone. The deterministic interpreter's gaps (R2–R6) are never
corrected by the model, because the model's answer is thrown away before anyone reads it.

### R2 — Incorrect company/entity extraction: «سود» and «خالص» treated as company names

**Reproduction:** «سود خالص آخرین فصل فولاد چقدر بوده؟»

**Current (observed in Feature 148's E2E run, Q2):** the deterministic interpreter resolves «سود» and/or
«خالص» as unresolved company-name mentions, and the turn ends in an `entity_not_found` clarification asking
which company the user means — even though «فولاد» is present and unambiguous.

**Expected:**
- Company: فولاد
- Metric: سود خالص (net profit)
- Period: آخرین فصل (latest quarter)
- No clarification; the quarterly net profit figure for فولاد is returned.

### R3 — Missing financial-threshold interpretation («کمتر از» / «بیشتر از»)

**Reproduction:**
- «سهم‌هایی با P/E کمتر از ۵ را پیدا کن»
- «شرکت‌هایی با P/S کمتر از ۱»

**Current (observed in Feature 148's E2E run, Q5/Q6):** `CapabilityRoutingPrecedence`
(`src/backend/FinancialCopilot.Application/AI/Orchestration/CapabilityInterpretationGovernance.cs:29`) does not
recognize «کمتر از» (less than) / «بیشتر از» (more than) as comparison operators tied to a numeric threshold,
so the query is misrouted to single-symbol metric lookup instead of stock screening. Both wordings are
provably reachable through the screener when phrased differently (Feature 148's tests show P/E < 5 →
شستا, فملی and P/S < 1 → فملی, وبملت, کگل via equivalent wordings the router does accept) — this is a router
gap, not a missing data path.

**Expected:**
- Financial-screening intent (`stock_screening` capability), not single-symbol lookup.
- Correct comparison operator (less-than / greater-than) and the stated numeric threshold passed into the
  scanner plan.
- No misrouting to `symbol_metric_lookup`.

### R4 — Monthly sales growth classified as a product-level trend query

**Reproduction:** «نمادهایی با رشد فروش ماهانه بیشتر از ۲۰ درصد»

**Current (observed in Feature 148's E2E run, Q7):** classified as `product_sales_trend`, which looks for a
named product's unit-level sales trend, not a company-level financial screen — the query has no product name,
so this route returns "unavailable" instead of running the screen. Equivalent wordings proved the screener
path works for this exact metric (Feature 148's tests show MoM growth > 20% → فملی 33.33%, کگل 25%).

**Expected:**
- Financial-screening intent, with a monthly-sales-growth filter at the stated threshold.
- No routing into `product_sales_trend` for a query that names no product.

### R5 — Quarterly trend query missing statement type

**Reproduction:** «روند سود خالص چهار فصل اخیر فولاد چطور بوده؟»

**Current (observed in Feature 148's E2E run, Q4):** the router requires an explicit financial-statement-type
signal (e.g. "صورت سود و زیان") before it will resolve a quarterly trend query, even though the metric named
(«سود خالص», net profit) already determines which statement it belongs to (the income statement). Without
that explicit signal the turn returns "no data" despite four quarters of net profit being present and
retrievable (Feature 148's tests retrieve all four quarters directly: 471,272,793; 120,356,493; 560,156,183;
188,930,866).

**Expected:**
- Resolve the net-profit metric from the wording alone.
- Resolve the company (فولاد) and the four-quarter period.
- Retrieve and present the quarterly series.
- Do not require a separate, explicit statement-type phrase when the named metric already implies one.

### R6 — Unsupported product question asks for a ticker that was already given

**Reproduction:** «تولید گندله کگل چقدر بوده؟» (pellet production volume for کگل)

**Current (observed in Feature 148's E2E run, Q10):** when CyclicalWaves is the active provider and
product-level production data is not available from it, the agent's symbol-lookup tool ends the turn by
asking the user which symbol they mean — even though «کگل» was already supplied and correctly resolved
upstream. No value is invented and no legacy call is made (verified: zero outbound HTTP, no cross-provider
read), but the response is a disambiguation request instead of an explicit "this data is not available"
answer.

**Expected:**
- When the requested data category (product-level production) is unavailable from the active provider, return
  the existing unavailable-capability response used elsewhere in the system (see
  `ChatCapabilityAvailability` in `src/backend/FinancialCopilot.Application/AI/Orchestration/`), not a
  clarification request for information already provided.
- Never ask the user to re-supply a ticker that was already resolved.

## Affected Chat routes

- `src/backend/FinancialCopilot.Application/AI/Orchestration/CapabilityInterpretationGovernance.cs` —
  `LlmQueryInterpretationProposalProvider` (R1), `CapabilityRoutingPrecedence` (R3, R4).
- `src/backend/FinancialCopilot.Application/AI/Orchestration/DeterministicCapabilityInterpreter.cs` — entity
  and intent resolution (R2, R4, R5).
- The financial-statement / quarterly-trend intent rules referenced from
  `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Workflow/FinancialCopilotWorkflowDefinition.cs`
  (`FinancialStatementTableIntentRules`, `FinancialStatementAnalysisIntentRules`) — R5.
- The MAF V2 agent's `lookup_symbol_metrics` tool path (symbol already resolved, capability unsupported) — R6.
- Both the semantic (MAF V2) and legacy/shadow routing paths, since Feature 148's tests show the same six
  defects in `ForceV1Orchestration` mode and in Shadow mode as in the primary semantic route.

## Relevant tests

- `tests/FinancialCopilot.IntegrationTests/Feature148ChatProviderE2ETests.cs` — `Feature148CyclicalWavesChatE2ETests`
  and `Feature148NoavaranAminChatE2ETests` currently assert the R2, R4, R5, R6 behaviors as *known, pinned*
  wrong outcomes (characterization tests) for Q2, Q4, Q5, Q6, Q7 and Q10. This feature's implementation must
  update those specific assertions to the corrected behavior — the files are otherwise unrelated to this
  feature's scope and must not be rewritten wholesale.
- New unit tests are needed for `LlmQueryInterpretationProposalProvider.Parse` covering every nullable field in
  the schema (`periodCount`, `presentation`, `intent`, `period`, `comparison`, `entityType`, `scope`) with an
  explicit JSON `null`, to close the whole class of bug R1 represents, not just the one field that was caught.
- New unit/integration tests for `CapabilityRoutingPrecedence` and `DeterministicCapabilityInterpreter` for
  each of R2–R6's exact Persian reproduction phrases.

## Acceptance criteria

Each of R1–R6 must have passing automated test coverage, verified end-to-end (real pipeline, only the LLM
mocked, per the pattern Feature 148 already established) for:

1. Both `FinancialData:ChatProvider` modes (`NoavaranAmin` and `CyclicalWaves`) — all six defects are
   provider-independent, so both must show identical corrected behavior.
2. The existing V1 orchestration path (`ForceV1Orchestration`) and the Shadow routing mode, in addition to the
   primary MAF V2 semantic route — Feature 148 confirmed all three share this routing code.
3. The MAF V2 agent route specifically for R6 (the `lookup_symbol_metrics` tool path).
4. Conversation persistence and replay: a corrected answer must persist and replay identically to how Feature
   148's existing Q1/Q3/Q8 tests already verify for working cases.
5. Financial metric lookup (R2), screening (R3, R4), quarterly statements (R5), and unsupported-capability
   handling (R6) each get their own exact-Persian-phrase acceptance test using the reproduction string given
   above — not a paraphrase.
6. No cross-provider fallback is introduced by any fix: a NoavaranAmin-mode fix must not read CyclicalWaves
   data or vice versa, consistent with Feature 148's C1/C1-M/C2 boundaries.
7. R1's fix is verified by a unit test that feeds the parser a `null` value for every nullable schema field,
   not only `periodCount`, since the same bug class (`TryGetInt32`/`TryGetDouble`/`GetString` on a JSON `null`
   token) could recur for any of them if a similar pattern was copy-pasted.

## Explicit out-of-scope items

- No new Chat architecture, orchestration mode, or routing framework. This is a fix to existing deterministic
  and LLM-proposal interpretation code, not a redesign.
- No change to financial formulas, metric definitions, or provider ingestion (NoavaranAmin or CyclicalWaves).
- No change to Feature 148's C1/C1-M/C2/N1 exclusion rules or to `ChatCapabilityAvailability`'s policy logic,
  beyond reusing its existing unavailable-capability response for R6.
- No change to which LLM provider or model is used, or to prompt content beyond what is strictly necessary to
  fix R1–R6 (for example, no speculative prompt-engineering pass unrelated to these six defects).
- No retroactive change to Feature 148's own acceptance criteria or its `ImplementationReport.md` beyond what
  Feature 148 already recorded (that report stays as the historical record of what was known at closure).
- No live-provider or live-Telegram verification is required for this spec-only deliverable; that belongs to
  this feature's own implementation and acceptance phase, once started.
