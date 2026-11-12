# Feature 159 — Capability Source Map, Step 1: Comprehensive Analysis from CyclicalWaves v2

Status: Gate A: Approved by the owner on 2026-11-10

Approval record: on 2026-11-10 the owner wrote «با پیشنهاد: نقشهٔ «منبع هر قابلیت» موافقم / با ترتیب پیشنهادی هم
موافقم». This approves the per-capability source map and the order: Step 1 comprehensive analysis (this feature),
Step 2 gauge and equilibrium, Step 3 P/E and P/S (both later, under the parked Feature 160 proposal). It does not
approve the implementation plan (Gate B).

| Field | Value |
|---|---|
| Layers / Area | backend, database, ops · AI, FinancialData |
| Evidence | Feature 155 theme T01 (comprehensive analysis is the C2 refusal today) |
| ADR | `docs/adr/0001-per-capability-chat-data-source.md` |

## 1. Problem

Production chat runs in provider mode `NoavaranAmin`. Decision C2 of Feature 148 disables comprehensive analysis in
that mode, so «تحلیل فولاد», «تحلیل بنیادی فولاد» and «تحلیل جامع فولاد» all get «تحلیل جامع در منبع دادهٔ فعال در
دسترس نیست.» The TahlilApp analyses are already fetched by the v2 ingestion into `cyclicalwaves."Analyses"`.

The provider is chosen once for the whole application (`FinancialData:ChatProvider`). There is no way to take one
capability from another provider.

## 2. Design (approved)

### 2.1 Capability source map

```json
"FinancialData": {
  "ChatProvider": "NoavaranAmin",
  "CapabilitySources": { "comprehensive_analysis": "CyclicalWaves" }
}
```

- The detection engine keeps deciding the capability as today. Only then is the source looked up.
- Default for every capability is `ChatProvider`. An empty map means today's behaviour exactly.
- Closed list: only capabilities in a code-owned allowlist may be overridden. Step 1 allowlist =
  `comprehensive_analysis`. Any other key, an unknown provider, or a map used while `ChatProvider=CyclicalWaves`
  fails start-up validation (fail fast, visible).
- The map entry is the switch. Removing it restores the C2 refusal without a code change.
- No fallback between providers: if v2 has no analysis for the symbol, the reply says so.

### 2.2 The four C2 enforcement points switch together

| Point | Today | After |
|---|---|---|
| `ChatCapabilityAvailability.Evaluate` | C2 when NoavaranAmin | C2 only when the route's source is NoavaranAmin |
| Semantic dispatcher (`SemanticCapabilityExecutionContracts.cs:96-97`) | reads the evaluation | unchanged code, follows the evaluation |
| DI (`CyclicalWavesV2ServiceCollectionExtensions.cs:66-69`) | `UnavailableComprehensiveAnalysisQueryUseCase` | `CyclicalWavesV2ComprehensiveAnalysisQuery` when the map routes it to v2 |
| Agent tool choice (`FinancialCopilotWorkflowDefinition.cs:1201-1202`) | `IsCyclicalWavesMode` | the route's source |

`IChatCapabilityAvailability` gains `SourceFor(route)` as the single owner of the decision. Everything else in
NoavaranAmin mode is untouched: entity resolver, symbol lookup (the analysis executor's context metrics still come
from Noavaran), statements, monthly, scanner, Feature 141, trackers, V1.

### 2.3 The answer

Deterministic, no LLM writing (the semantic path already builds the text in code):

```text
### شگل، بخش دوم- تعادلی
تاریخ: 1405/08/18 | تحلیلگر: نیما آزادی
<teaser: first ~300 characters, cut at a sentence or word boundary> …
[ادامهٔ تحلیل در تحلیل‌اپ](<article link or https://tahlilapp.com/app/analysis>)

این متن نظر تحلیلگر تحلیل‌اپ است و توصیهٔ خرید یا فروش ساپیو نیست.
```

- Up to 3 newest analyses for the symbol (existing default), newest first.
- The author label comes from the provider category, which in live data is the analyst's name.
- Teaser length is configurable; the full text stays on TahlilApp (`required_plans: ["analysis"]` content).
- Link: a configurable per-article template using the provider `slug`; until the owner confirms the pattern, the
  link is the all-analyses page `https://tahlilapp.com/app/analysis`. Probe on 2026-11-10:
  `https://tahlilapp.com/blog/<slug>` returns 307 to `/app/analysis` for a real slug and 404 for a fake one, so a
  slug route exists; whether a signed-in user sees the article there is not verified.
- None found: «برای نماد X تحلیلی در تحلیل‌اپ پیدا نشد.» plus the all-analyses link; NoData, free.

### 2.4 Ingestion changes (small)

- Store `slug` in `cyclicalwaves."Analyses"` (nullable column, migration on `FinancialIngestionDbContext`).
- The store writes the slug even when the content hash is unchanged, so one existing `Analysis` sync in `Full` mode
  backfills it.
- Title matching: add the Persian comma «،» to the title separators, so «شگل، بخش سوم - نسبت P/s» matches «شگل».

### 2.5 Detection fixes

- «جامع» becomes a non-entity word, so «تحلیل جامع فولاد» does not carry a stray entity.
- Existing precedence stays: statement analysis («تحلیل صورت سود و زیان X») keeps its route; a point metric
  (P/E, EPS) keeps symbol lookup.

### 2.6 Telegram

The renderer prints analyses without links and, per code reading, may drop or duplicate them
(`TelegramAssistantResponseRenderer.cs:57-58`, `:113`, `:123`). Step 1 verifies both with tests, fixes what
fails, and adds the link.

## 3. Out of scope

Steps 2 and 3 (gauge, equilibrium, P/E, P/S); full article text; images; screening by analysis; any LLM
summarisation; V1 work; enabling v2 ingestion on the production server (ops step with its own approval).

## 4. Risks

- Production may not ingest v2 analyses yet (compose default `false`). Release gate, read-only check first.
- Paid content: teaser length and display need TahlilApp's agreement (OQ-064).
- Advice boundary: analyst text includes buy calls; shown verbatim with attribution and a fixed disclaimer (OQ-065,
  linked to OQ-012).
- The V1 orchestrator shares `IComprehensiveAnalysisQueryUseCase`. V1 is not active (`AiOrchestration:Mode` is V2);
  no V1 code changes.

## 5. Open questions (registered in `specs/open-questions.md`)

- OQ-063 per-article link pattern.
- OQ-064 teaser length and TahlilApp agreement on paid content.
- OQ-065 verbatim analyst opinions with attribution and disclaimer.

## 6. Owner answers at Gate B (2026-11-10) — these supersede §2.3, §2.4 and §5

On 2026-11-10 the owner answered OQ-063..065 and approved Gate B in the same message:
«Q-63 لینک هر تحلیل داینامیک نیست. کلا لینک ثابتی در تحلیل اپ برای بخش تحلیل های جامع وجود دارد که به نظرم در تنظیمات ست
کن / Q-064 کلا این بخش تحلیل اپ پولی است برای همین هم پیشنهاد دادم که فقط عنوان و خلاصه (کل خلاصه) نمایش داده بشه و لینک
ثابت تحلیل اپ در انتها آورده بشه. نحوه پیدا کردن تحلیل هم جستجوی نماد مورد پرسش در عنوان تحلیل باید باشد. / Q65 کل خلاصه
عینان نمایش داده شود دیگر این بخش موضوعیت ندارد. / Gate B مورد تایید است»

Resulting design:

- **Link:** one fixed link from configuration, `ComprehensiveAnalysis:TahlilAppAnalysesUrl` (default
  `https://tahlilapp.com/app/analysis`, validated absolute https URL at start-up), shown once at the end of the reply.
  No per-article link, no slug column, no migration, no API field.
- **Card:** for each analysis only the title and the whole published summary (HTML already stripped at ingestion),
  verbatim. No date, analyst or disclaimer line. A hard safety bound of 6,000 characters per summary stays
  (largest live summary seen: 3,829 of 50 sampled); a cut is marked with «…».
- **Matching:** an analysis belongs to the asked symbol when the symbol appears as a whole word in the title
  (normalized Persian, any position; «حق تقدم شگل», «شگل، بخش سوم - نسبت P/s»). Tags no longer select analyses
  for a symbol (daily «رصد معاملات عمده» reports tag many tickers). Topic tags still filter when given.
- **Example:**

```text
### شگل، بخش دوم- تعادلی
<whole summary>

### شگل
<whole summary>

[ادامهٔ تحلیل‌ها در تحلیل‌اپ](https://tahlilapp.com/app/analysis)
```

- **None found:** «برای نماد X تحلیلی در تحلیل‌اپ پیدا نشد.» plus the link; NoData, free. The registered
  financial source (Feature 141, NoavaranAmin) does not answer instead, because a mapped capability has no
  cross-provider fallback.
- **Telegram:** the same card as plain text (title line, summary, link line with the bare URL).
