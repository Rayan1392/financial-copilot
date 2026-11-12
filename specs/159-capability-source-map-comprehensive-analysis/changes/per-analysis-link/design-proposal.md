# Change to Feature 159 — a link to the analysis itself

Status: Withdrawn — not implemented

Owner decision (2026-11-11), after the probe below showed the URL is the TahlilApp API and returns JSON: «این لینک api
سرویس خود اون تحلیل هست نه لینک به صفحه وب در تحلیل اپ، اینجا این لینک کاربردی برای این سناریو ما نداره». The card
keeps the fixed link `ComprehensiveAnalysis:TahlilAppAnalysesUrl`. The design below can be reused if TahlilApp gets a
web page for one analysis; then only the template value changes.

Would have superseded: the owner answer to OQ-063 (2026-11-10, «there is no per-analysis link») for the link of a
found analysis, and the link part of change `latest-analysis-teaser` §4.

## Request (owner, 2026-11-11)

The card that answers «تحلیل جامع X» ends with a fixed link read from configuration
(`ComprehensiveAnalysis:TahlilAppAnalysesUrl`, `.env` `COMPREHENSIVE_ANALYSIS_TAHLILAPP_URL`). It must instead link to
the shown analysis with the pattern `https://api.tahlilapp.com/api/blogs/<Id>`, where `<Id>` is the `Id` of the row in
`cyclicalwaves."Analyses"` (example: `https://api.tahlilapp.com/api/blogs/55040`).

## Discovery

- The card already carries the id. `ComprehensiveAnalysisSummaryItem.AnalysisId` is set from
  `CyclicalWavesV2AnalysisRow.Id` (`CyclicalWavesV2ComprehensiveAnalysisQuery.cs`), which is mapped to
  `cyclicalwaves."Analyses"."Id"` with `ValueGeneratedNever`: it is the provider's own article id
  (`EfCoreCyclicalWavesV2AnalysisStore` writes `Id = article.Id`). No query, migration or new data is needed.
- One composer builds both replies: `ComprehensiveAnalysisCardComposer.ComposeMarkdown` (web, through
  `ComprehensiveAnalysisToolAdapter` and `FinancialCopilotAgentWorkflowRunner`) and `ComposePlainText` (Telegram,
  `TelegramAssistantResponseRenderer`). Both take one `analysesUrl` string today.
- The link appears in two places: under a found analysis («ادامهٔ این تحلیل در تحلیل‌اپ») and under the no-result
  sentence («تحلیل‌های جامع تحلیل‌اپ»). The no-result reply has no analysis, so it has no id.
- Probe of the example URL on 2026-11-11 (headers and field names only): HTTP 200, `application/json`, no redirect.
  Without sign-in the record is `locked: true`, `body` is null, `summary` is 10 characters and `required_plans` is set,
  so the link does not give away the paid text. **The browser shows raw JSON, not a TahlilApp page.**
- The legacy CyclicalWaves table (`ComprehensiveAnalyses`, used only when the chat runs on the legacy CyclicalWaves
  source) also fills `AnalysisId` from its `Id`; that this is the same TahlilApp id is not verified.

## Proposed design

1. **Found analysis:** the link line uses the analysis' own URL, built from a template with `{id}` replaced by
   `AnalysisId` (invariant digits). Web: `[ادامهٔ این تحلیل در تحلیل‌اپ](https://api.tahlilapp.com/api/blogs/55040)`.
   Telegram: `ادامهٔ این تحلیل در تحلیل‌اپ: https://api.tahlilapp.com/api/blogs/55040`. The same applies to the
   BUG-020 fallback card (no technical analysis → latest analysis): the link is that analysis' id.
2. **Configuration:** a new key `ComprehensiveAnalysis:TahlilAppAnalysisUrlTemplate`, `.env`
   `COMPREHENSIVE_ANALYSIS_TAHLILAPP_ANALYSIS_URL_TEMPLATE`, default `https://api.tahlilapp.com/api/blogs/{id}`.
   Start-up validation: absolute https and exactly one `{id}`; an invalid value stops the start, as the existing keys do.
3. **No analysis found:** unchanged; it keeps the fixed section link `TahlilAppAnalysesUrl`.
4. Nothing else changes: title, date, preview, the read-more sentence and the link text stay as they are.

### Alternatives

- Replace `TahlilAppAnalysesUrl` with the template and drop the no-result link. Rejected: the no-result reply would
  lose its only pointer to TahlilApp.
- Hard-code the pattern. Rejected: the host already moved once (`tahlilapp.com/blog/<slug>` in OQ-063); configuration
  lets operations change it without a release.

## Questions for the owner

1. The link opens raw JSON in the browser (see Discovery). Use `https://api.tahlilapp.com/api/blogs/{id}` as given, or
   is there a TahlilApp page for one analysis (for example `https://tahlilapp.com/...`) to put in the template instead?
   Recommendation: if a page exists, use it; the template key makes this a configuration change only.
2. Keep the fixed section link when no analysis is found (design 3)? Recommendation: yes.

## Tests (plan)

- Unit: composer puts `https://api.tahlilapp.com/api/blogs/<id>` in the web and Telegram cards; no-result card keeps
  the section link; template validation (missing `{id}`, two `{id}`, http, relative).
- Integration (`Feature159MappedNoavaranAminChatApiFactory`): «تحلیل جامع فولاد» ends with the seeded analysis' id link;
  «تحلیل تکنیکال کگل» links to the کگل analysis 150002; «تحلیل جامع فملی» keeps the section link.
- Docs: `AGENTS.md` card line, `docs/providers/cyclicalwaves-v2/README.md`, `.env.example`, `docker-compose.yml`,
  `appsettings.json`, OQ-063 note, `CHANGELOG.md` `[Unreleased]`.

## Risks

- A wrong id gives a broken link; the id is the provider's own key, so this needs a wrong sync, not a code path.
- Raw JSON may confuse users (question 1).
