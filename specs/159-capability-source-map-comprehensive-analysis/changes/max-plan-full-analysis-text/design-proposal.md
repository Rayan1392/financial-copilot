# Change to Feature 159 — whole analysis text for Max subscribers

Status: Gate A: Approved by the owner on 2026-11-11

Approval record: on 2026-11-11, after the plan-freshness answer, the owner wrote «پس این مشکلی نداره و با همه
پیشنهادات موافقم و تایید میکنم»: the design is approved and the four questions take the recommended answers (only Max;
for Max the read-more sentence is dropped and the fixed link line stays; the whole text stays in the user's history;
Telegram cut at 4,096 characters). Gate B (the plan in `tasks.md`) is not yet approved.

Would supersede, once approved: change `latest-analysis-teaser` design 3 («the whole text never reaches the API
response, conversation history, Telegram or a model prompt») for users whose plan grants the new capability.

## Request (owner, 2026-11-11)

«اگر کاربر اشتراک max داشت کل متن تحلیل را ببیند به همراه لینک ولی اگر اشتراک max نداشت به همان سناریوی فعلی عمل کند»

A user with an active Max plan gets the whole text of the latest analysis plus the link; every other user keeps the
current card (30% / 300-character preview, read-more sentence, fixed link).

## Discovery

- **Plans.** Plans come from TahlilApp (`TahlilAppSubscriptionPolicy`): `Free`, `Plus`, `Pro`, `Max`. An active,
  unexpired Max window wins; an expired or unknown plan becomes `Free`.
- **Per-plan capabilities already exist.** Table `billing_plan_capabilities` (`CapabilityCode`, `PlanCode`,
  `PolicyVersion`, `IsEnabled`, `Limit`), checked through `IPlanCapabilityService`; Feature 152 seeded the Max rows
  (`20261101160414_Feature152MaxProCapabilityMatrix`). Admins can change rows without a release.
- **Where the preview is cut.** At the source: `CyclicalWavesV2ComprehensiveAnalysisQuery` (line 208) calls
  `ComprehensiveAnalysisTeaserPolicy.Teaser` and returns only the preview. The query does not know the user today.
- **Stored text.** `cyclicalwaves."Analyses"."PlainTextSummary"` holds the whole text (provider `summary`, owner
  2026-11-10). Measured 2026-11-10 on 200 analyses: median 305 characters, longest about 4,000.
- **Composer.** `ComprehensiveAnalysisCardComposer` builds the web (Markdown) and Telegram (plain text) card; no LLM
  writes the card (BUG-020 F1).
- **Telegram** messages are limited to 4,096 characters.
- **Plan freshness (code read 2026-11-11).** TahlilApp status is read (`TahlilAppEntitlementRefresher.RefreshIfDueAsync`,
  one call, 3 s timeout, no retry): at sign-in; on every chat question, inside the billing reservation
  (`UsageReservationAuthorizationService`), but only when due — 15 minutes after the last successful check, or at the
  known expiry if sooner (`TahlilAppEntitlementSchedule`, `StatusTtlMinutes=15`); and on a credit top-up. The
  background revalidation worker exists but is off by default and is not set in `docker-compose.yml`. Effects: a plan
  bought or cancelled on TahlilApp is seen here within 15 minutes, at the user's next question; a plan that simply
  expires lapses at its expiry; if the TahlilApp status call fails, the last known paid plan is kept for at most 6 hours
  (`StaleGraceHours`) and never past its expiry, then becomes Free. The reservation runs before the answer, so the
  full-text check must read the plan after it.

## Proposed design

1. **Entitlement as a plan capability, not a hard-coded plan name.** New capability `ComprehensiveAnalysis.FullText`,
   one row `(ComprehensiveAnalysis.FullText, Max, v1, IsEnabled=true)` added by a billing migration. Other plans have no
   row, so they keep the preview. Turning the row off returns Max users to the preview without a release (this is the
   kill switch).
2. **Application port** `IComprehensiveAnalysisFullTextEntitlement.IsGrantedAsync(tenantId, actorId, ct)`;
   Infrastructure implements it with the user's billing account, its effective plan (expiry applied) and
   `IPlanCapabilityService`. **Fail closed:** no account, an expired plan, an error or more than 2 seconds → preview.
3. **Cut at the source stays.** The comprehensive-analysis request gets `IncludeFullText`; only when it is true does
   the query return the whole text (with a repeated title removed). For everyone else the whole text still never
   leaves the query.
4. **Card for an entitled user:** title, publish date, whole text, then the fixed link line. The read-more sentence
   «متن کامل این تحلیل را در بخش تحلیل جامع، تحلیل اپ بخوانید» is left out, since the whole text is shown (question 2).
   Same for the BUG-020 fallback card (no technical analysis → latest analysis).
5. **Telegram:** if the card would pass 4,096 characters, the text is cut at the last sentence end that fits, with
   «…», and the link line stays.
6. **Unchanged:** charge (1 credit; Max is unlimited anyway), the no-result reply, routing, the preview numbers.
7. **Observability:** a counter of full-text and preview cards by plan (no text, no user id in logs).

### Alternatives

- Check `PlanCode == "Max"` in code. Rejected: the plan matrix already exists for exactly this, and a row can be
  turned off or given to another plan without a release.
- Fetch the whole text live from TahlilApp with the user's own TahlilApp session. Rejected: a new outbound call on the
  chat path, and the stored text is already the whole text.
- Follow each article's own TahlilApp `required_plans`. Not now: that field is not stored (backlog item), and the
  owner asked for Max.

## Questions for the owner

1. Only Max? Plus and Pro keep the preview. Recommendation: yes, as requested; the row makes extending it a data change.
2. For Max, drop the read-more sentence and keep only the link line «تحلیل‌های جامع تحلیل‌اپ» to the fixed section
   page? Recommendation: yes.
3. The whole text is stored in that user's conversation history and stays there after the Max plan ends. Acceptable?
   Recommendation: yes (it is the user's own history; new questions get the preview again).
4. Telegram: cut at 4,096 characters as in design 5 (rare: the longest measured text is about 4,000)? Recommendation: yes.

## Tests (plan)

- Unit: entitlement (Max active → granted; Max expired, Pro, Free, no account, error, timeout → not granted);
  composer full-text card for web and Telegram, without the read-more sentence; Telegram 4,096 cut; the query returns
  the preview unless `IncludeFullText`.
- Integration (`Feature159MappedNoavaranAminChatApiFactory`): a Max user's «تحلیل جامع فولاد» shows the whole seeded
  text; a Free user's shows the preview; the Max row disabled → preview; «تحلیل تکنیکال کگل» fallback for Max.
- Migration test for the new capability row; architecture tests (port in Application, implementation in
  Infrastructure).
- Docs: `AGENTS.md` card, `docs/providers/cyclicalwaves-v2/README.md`, billing capability docs, `CHANGELOG.md`.

## Risks

- Paid content is shown to the wrong user if the plan is wrong. Mitigation: fail closed, effective plan with expiry,
  tests for every plan.
- Plan freshness (see Discovery): up to 15 minutes after a purchase or a cancellation on TahlilApp, and up to 6 hours
  while the TahlilApp status call is failing. Natural expiry has no delay. Same window as every other Max capability.
