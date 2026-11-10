# User Story: Comprehensive Analysis from TahlilApp in Chat (Capability Source Map, Step 1)

## Feature Folder

`162-capability-source-map-comprehensive-analysis`

## Layers / Area

Layers: backend, ops
Area: AI, FinancialData

## Priority

P2

## Complexity

M

## Depends On

148 (v2 ingestion of analyses, done)

## References

- `specs/162-capability-source-map-comprehensive-analysis/design-proposal.md` (Gate A; §6 records the owner's
  Gate B answers and supersedes §2.3, §2.4 and §5)
- `docs/adr/0001-per-capability-chat-data-source.md`
- `specs/148-cyclicalwaves-v2-independent-financial-data-provider/design.md` (C2, §10.4)
- `docs/providers/cyclicalwaves-v2/README.md`, `tahlilapp-api-client.pdf` (`GET /blogs?filter[type]=analysis`)
- `specs/POLICY-V1-FREEZE.md`
- Code: `ChatCapabilityAvailability.cs`, `CyclicalWavesV2ServiceCollectionExtensions.cs`,
  `SemanticCapabilityExecutors.cs` (`ComprehensiveAnalysisCapabilityExecutor`),
  `ComprehensiveAnalysisToolAdapter.cs`, `CyclicalWavesV2ComprehensiveAnalysisQuery.cs`,
  `FinancialCopilotWorkflowDefinition.cs`, `TelegramAssistantResponseRenderer.cs`
- Data model: `FinancialIngestionDbContext`, `CyclicalWavesV2AnalysisRow` (`cyclicalwaves."Analyses"`, read only)

## Standards Source Of Truth

Obey `.claude/rules/*`. Do not copy rule text into this spec.

## User Story

As a Sapio customer,
I want «تحلیل جامع X», «تحلیل X» or «تحلیل بنیادی X» to show TahlilApp's latest analyses of X (title and
summary) with TahlilApp's analysis link at the end,
so that I get the analyst's view instead of «در دسترس نیست».

## Business Context

Comprehensive analysis is refused in production today (decision C2), which is theme T01 of the Feature 155 report.
The analyses are already in our database from the v2 ingestion. This is also the first use of the per-capability
source map, which Steps 2 and 3 (gauge, equilibrium, P/E, P/S) will reuse.

## Scope

- `FinancialData:CapabilitySources` with a code-owned allowlist (`comprehensive_analysis` only).
- Comprehensive analysis answered from `cyclicalwaves."Analyses"` while `ChatProvider=NoavaranAmin`.
- Deterministic answer card: title and whole summary per analysis, fixed TahlilApp link at the end.
- Symbol-in-title matching; the «جامع» non-entity fix.
- Telegram rendering of the card.
- Compose/env plumbing for the map (default empty).

## Out Of Scope

- Gauge, equilibrium, P/E, P/S (Steps 2 and 3).
- Per-article links, slug storage, images, screening by analysis, LLM summarisation.
- Database schema changes; API contract changes.
- V1 changes.
- Turning on v2 ingestion or the map on the production server (separate ops approval).
- `is_published`, `required_plans`, thumbnails (→ `specs/backlog.md`).

## Functional Requirements

1. With an empty map, every chat answer is the same as today, including the C2 refusal.
2. With `comprehensive_analysis → CyclicalWaves` and `ChatProvider=NoavaranAmin`, a comprehensive-analysis turn
   reads only `cyclicalwaves."Analyses"`, `AnalysisCategories`, `AnalysisTags` from the v2 schema; the context
   metrics in the same turn still come from Noavaran.
3. Start-up fails with a clear message for: a key outside the allowlist, an undefined provider value, a non-empty
   map while `ChatProvider=CyclicalWaves`, or a `TahlilAppAnalysesUrl` that is not an absolute https URL.
4. The reply lists up to 3 newest analyses for the symbol, newest first; each shows `### <title>` and the whole
   published summary verbatim (safety bound 6,000 characters, a cut ends with «…»).
5. The reply ends with one link line `[ادامهٔ تحلیل‌ها در تحلیل‌اپ](<TahlilAppAnalysesUrl>)`; default URL
   `https://tahlilapp.com/app/analysis`.
6. An analysis belongs to symbol X when X (normalized) is a whole word in its title, at any position. Tags do not
   select analyses for a symbol; topic tags still filter when given.
7. No analysis: «برای نماد X تحلیلی در تحلیل‌اپ پیدا نشد.» plus the link line; billed as NoData (free); the
   registered financial source (Feature 141) does not answer instead.
8. Billing otherwise unchanged (Executed or Partial → 1 credit).
9. «تحلیل جامع X», «تحلیل X», «تحلیل بنیادی X», «آخرین تحلیل X», «X را بررسی کن» route to comprehensive
   analysis; «تحلیل صورت سود و زیان X» and «P/E X» keep their current routes.
10. Titles «شگل، بخش سوم - نسبت P/s» and «حق تقدم شگل» match «شگل»; «فرابورس- …» does not match «بورس».
11. Telegram shows the same card once, as plain text with the bare URL, also when the turn has one lookup row.

## Data / Schema

No schema change. Chat reads `cyclicalwaves."Analyses"` (`Title`, `PlainTextSummary`, `PublishedAt`) and its tag
and category tables through `CyclicalWavesV2ComprehensiveAnalysisQuery`.

## API (if any)

No change.

## AI Query Path (if any)

V2 semantic route `comprehensive_analysis` → `ComprehensiveAnalysisCapabilityExecutor` →
`IComprehensiveAnalysisQueryUseCase` (v2 implementation when mapped). C2 refusal stays when unmapped.
Agent tool selection follows the same source.

## UI (if any)

Web: markdown text (links already rendered by `markdown-message.tsx`). Telegram: plain text with the URL.

## Non-Functional Requirements

- Read-only chat access; no provider call from chat; existing query limits (max 10 items).
- Configuration validated at start-up.
- Logging: the semantic step records `chat.capability_source`.

## Acceptance Criteria

- [x] AC1 Empty map: NoavaranAmin regression suites and `Feature148ChatProviderE2ETests` C2 test unchanged and green.
- [x] AC2 Mapped: «تحلیل جامع فولاد» returns the card from v2 rows; the read recorder shows only the three analysis tables from the v2 schema; context metrics from Noavaran.
- [x] AC3 Invalid map values and an invalid link fail start-up with a clear message.
- [x] AC4 Card format: title and whole summary, safety bound with «…», one link line at the end, max 3 items, newest first.
- [x] AC5 None found: NoData sentence, link, 0 credits, no Feature 141 answer.
- [x] AC6 Routing corpus of FR9 passes in NoavaranAmin mode with the map.
- [x] AC7 Title-word matching (FR10) and «جامع» non-entity tests pass.
- [x] AC8 Telegram renders the card once, with the URL, including the one-lookup-row case.
- [x] AC9 Live Development check on disposable PostgreSQL with real v2 analyses for two symbols (one with a «،» title).

## Assumptions

- Owner answers of 2026-11-10 (OQ-063, OQ-064, OQ-065) are recorded in `specs/open-questions.md`.
- Production ingestion of v2 analyses is not verified; release needs the read-only check.
