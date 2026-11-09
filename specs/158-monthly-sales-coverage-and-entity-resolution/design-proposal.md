# Feature 158 — Monthly Sales Coverage and Entity Resolution

Status: Proposed — not approved

| Field | Value |
|---|---|
| Layers / Area | backend · AI, Conversations, FinancialData |
| Priority (proposed) | P2 |
| Complexity (proposed) | M |
| Depends On | None. Reads 135 (ReportingType routing), 150 (shared routing), 155 (classifier v1), 156 (snapshot card) |
| Evidence | Feature 155 demand theme **T07 — پوشش داده فروش و تولید ماهانه**: 23 customers, 36 questions |
| References | `specs/POLICY-V1-FREEZE.md`, `specs/135-noavaran-reporting-type-routing/Design.md`, `specs/141-schema-aware-semantic-query-engine/curated-catalog-review.md` (§ReportingType), `docs/noavaran-current-api-ingestion.md`, `docs/noavaran-current-monthly-backfill-investigation.md`, `docs/company-master-data-model.md`, `docs/domain-glossary.md` |

Line numbers below are at commit `be489cb`. Questions in this document are invented and follow the pattern of the real ones.

## 1. What is proposed

Each row can be approved, changed, or rejected on its own at Gate A. Nothing here is approved.

| Item | Kind | One-line result |
|---|---|---|
| **D1 — BUG-008** | Defect | A word written with ZWNJ stays one word and is not read as two mentions. |
| **D2 — BUG-009** | Defect | A follow-up that names another ticker answers for that ticker, not the previous one. |
| **D3 — BUG-010** | Defect | Two tickers in a monthly request are neither silently cut to one nor reported as ambiguous; a ticker with duplicate catalog rows resolves to the canonical row. |
| **D4** | Change | A company with no monthly route gets a distinct, truthful reply instead of `supported_but_no_rows`. |
| **D5 — proposed new bug** | Defect | Monthly trend reads the Noavaran company row, not another provider's row with the same ticker. |
| **D6 — proposed new bug** | Defect | A message with no capability does not silently re-run the previous capability. |

Side-by-side monthly comparison of two companies is a missing feature (T13), not part of BUG-010. It is out of scope here.

## 2. Out of scope

- New ingestion or new vendor endpoints. Whether to add them is OQ-PROPOSED-C1.
- Schema changes. No migration.
- Any change to V1 behaviour. The dialogue gate and resolver are shared with V1 (`AiQueryOrchestrationService.cs:44`), so every behaviour change below sits behind the existing V2 routing scope (`Feature154RoutingScope`, wired at `ServiceCollectionExtensions.cs:967`). Repeating the V1-freeze check is part of the plan.
- The eligibility rule itself (`NoavaranCompanyScope`), and Feature 156's detection rule. OQ-011 stays open.
- Changing other consumers of `ResolveBySymbolAsync` (listed in §7, item D5).

## 3. Bounded contexts and language

Contexts: **AI Assistant (Conversations)** owns entity mentions, follow-ups, and outcomes. **FinancialData** owns the company catalog and monthly coverage. AI reads FinancialData only through Application ports.

| Term | Meaning |
|---|---|
| Entity mention | A span of the user's message that may name a company. Not yet a company. |
| Canonical company row | The `Companies` row of provider `NoavaranCurrentApi` for the ticker, preferring one inside the eligible universe. |
| Eligible universe | `NoavaranCompanyScope.EligibleCompanies`: `PrecedencyRight = 0` on the three primary markets (`NoavaranCompanyScope.cs:31-38`). |
| Monthly route | The endpoint assigned to a company's `ReportingType` by `NoavaranMonthlyReportTypeResolver`. |
| Monthly coverage | Whether a company can have persisted monthly data at all: has route, no route, or outside scope. |

## 4. Findings

Evidence labels: **Confirmed** (reproduced with a scratch harness over the real interpreter, resolver and gate, with invented rows and in-memory stores, outside the repository); **Code** (read from source, not exercised); **Unverified** (needs production data). No database was connected in this wave.

### 4.1 BUG-008 — an ordinary word becomes a ticker

| Point | Evidence | Status |
|---|---|---|
| ZWNJ is mapped to a space, so one written word becomes two tokens | `QueryNormalization.cs:50`; `DeterministicCapabilityInterpreter.cs:414` | Confirmed: `آینده‌نگر` gives mentions `آینده`, `نگر` |
| Every token not on a stop-list becomes a mention; there is no "looks like a ticker" test | `DeterministicCapabilityInterpreter.cs:417-428` | Confirmed |
| Fuzzy or prefix matching is the cause | Fuzzy can only return `Ambiguous`, never `Resolved` (`CanonicalQueryEntityResolver.cs:105-115`); no prefix matching exists | **Refuted** |
| The word resolves through exact symbol match or unique-substring variant | `CanonicalQueryEntityResolver.cs:61-76`, `:87-103`. A row with symbol `آینده` gives `Resolved(آینده, approved_alias)`; a single name containing the word gives `Resolved(<other symbol>, normalized_identity_variant)` | Confirmed for both paths. Which row exists in production: **Unverified** |
| Which path ran in production | Deterministic path splits the word. The model path keeps the span whole unless the model splits it | **Unverified**; no field records it |
| A user who types a space instead of ZWNJ (`آینده نگر`) gets the same split | Harness | Confirmed; ZWNJ handling alone does not cover this case |

### 4.2 BUG-009 — a follow-up answered for the previous company

Mechanism: stale conversation entity. The newly named company is resolved and then discarded.

| Point | Evidence | Status |
|---|---|---|
| With no capability detected, slots are not built, so the resolved company is dropped | `ConversationTaskStateContracts.cs:176` resolves; `:214-216` builds slots only when `capability` is not null | Confirmed |
| The previous capability and company are carried over and the frame is executed | `ConversationTaskStateContracts.cs:683-704` (carry-over), `:457` (`effectiveCapability`), `:461-485`, `:488` | Confirmed: deterministic-only, model unavailable, and model timeout all give the previous company with `ConversationInferred` |
| Path 2: the model proposes a capability but no entity span | Model spans replace deterministic mentions (`CapabilityInterpretationGovernance.cs:424-438`, `:427`/`:432` drops spans not found verbatim); slot is `Missing`, which `ConversationTaskStateContracts.cs:467` does not protect | Confirmed: the previous company is used, labelled `UserExplicit` |
| Model proposes capability plus entity | | Refuted as a cause: the new company is answered |
| Model proposes no capability | Frame is dropped (`ConversationTaskStateContracts.cs:497`, `:513`), disposition `Unsupported` | Refuted as a cause |
| Fuzzy or token match between the two tickers | Similarity of the two tickers is far below the 0.72 threshold; no shared token | Refuted |
| Which path ran in production | No persisted field distinguishes them (`ConversationContracts.cs:37`) | **Unverified** |

Related, D6: a capability-less message such as `ممنون` inside an active task re-runs the previous capability and company (Confirmed, deterministic-only and model-unavailable paths).

### 4.3 BUG-010 — two tickers joined with a slash

The slash is punctuation, so two mentions result (`QueryNormalization.cs:74-78`). Slash handling is not the fault.

| Point | Evidence | Status |
|---|---|---|
| `monthly_activity_trend` accepts one company | `ConversationalCapabilityContracts.cs:206`, slot `symbol` only; multi-company slot exists only for `symbol_metric_lookup` (`ConversationTaskStateContracts.cs:303-318`) | Code |
| With unique catalog rows and two spans, the longest mention wins and the other is silently dropped | `CanonicalQueryEntityResolver.cs:137-140`, first `Resolved` returned | Confirmed: answers one ticker, no warning |
| `entity_ambiguous` needs duplicate catalog matches: two non-Noavaran rows with the ticker in `Ticker`, or two Noavaran rows with the same symbol | `CanonicalQueryEntityResolver.cs:61-76`; `PreferCanonical` runs per step (`:235-242`), so a Ticker hit on non-Noavaran rows stops the alias step that would find the Noavaran row. `CyclicalWavesCompanyMappingService.cs:88-92` fills `Ticker` on non-Noavaran rows; T1.1 evidence shows `Ticker` empty on all 877 eligible rows | Confirmed by harness for both shapes. Whether production has these rows: **Unverified** |
| A single ticker with duplicate rows is ambiguous too | Harness | Confirmed; matches 155 design §10 |
| One model span holding both tickers | Compacted to one key, `NotFound` | Confirmed; gives `entity_not_found` |
| Deterministic fallback path reads the first ticker as a product | `DeterministicCapabilityInterpreter.cs:168-178`; candidates led by `product_sales_trend` | Confirmed |

Verdict: **defect** (BUG-010). The request names two exact tickers and gets either a wrong "ambiguous" reply or a silent cut. The comparison answer is a separate missing feature.

### 4.4 Data gap: how a company gets monthly data

```text
Catalog sync → Companies (ReportingType) → eligible scope → resolver route
  → ProductSales / ServiceSales fetch → MonthlyReports
  → trend snapshot → read by ExternalCompanyId
```

- Eligibility: rights rows and other markets are never requested (`NoavaranCompanyScope.cs:31-38`).
- Route: `1000000` and `1000008` ProductSales; `1000005` ServiceSales. Codes `1000001` construction, `1000002` investment, `1000003` banking, `1000004` leasing, `1000006` insurance, `1000007` maritime transport, `1000009` investment banking have no route and the client throws instead of calling the vendor (`NoavaranMonthlyReportTypeResolver.cs:37-54`, `NadpcoApiDataProviderClient.cs:167-179`). Missing and unknown codes also get no route.
- Monthly data starts at 1404 (`docs/noavaran-current-api-ingestion.md:37-38`).
- Snapshots are built only for eligible companies from accepted reports (`CompanyMonthlyActivityTrendSnapshotBackfillService.cs:45-59`).
- `supported_but_no_rows` covers all of these equally: `SemanticCapabilityExecutors.cs:169-171` maps any null result to `NoData`; the use case returns null for company-not-found and for no snapshot (`MonthlyActivityTrendQueryUseCase.cs:36-42`, `:59-69`).

A second cause is in the code. `ResolveBySymbolAsync` is Ticker-first with `FirstOrDefault` across all providers (`CompanyResolverService.cs:119-121`). Confirmed: with a Noavaran row and another provider's row carrying the ticker, it returns the other row's `ExternalCompanyId`, and the snapshot lookup finds nothing. The provider-scoped method already exists (`CompanyResolverService.cs:28-60`).

**Important limit of the code evidence.** "No route" means no verified endpoint in this codebase (`specs/135-noavaran-reporting-type-routing/Design.md:180-187`, `:351-352`). It does not prove the company publishes no monthly report. The reply in D4 must not say that it does not publish one.

### 4.5 Per-ticker cause

`فسبزوار`, `دسبحا`, `تابان`, `سابیک`, `غشکو`, `ثجنوب`, `والبر`, `ولساپا`, `ومعادن`, `ثهام`: nothing in the repository proves the category of any of them. Only `ومعادن` and `دسبحا` occur at all, in an unrelated report and in mock data. Each ticker's cause is **Unverified** and needs the read-only check. The mapping from result to cause:

| Read-only result | Cause | Action |
|---|---|---|
| No Noavaran row for the ticker | Catalog gap | Ingestion or catalog review |
| Row exists, not eligible | Outside scope (rights, market) | Explain; no ingestion |
| `ReportingType` in the seven no-route codes | No monthly route | D4 reply; OQ-PROPOSED-C1 |
| `ReportingType` null or unknown | Catalog metadata gap | Catalog refresh |
| Snapshot exists under the Noavaran id, and another provider's row carries the ticker | Resolver picks wrong row | D5 |
| Snapshot exists, no other row | Data present; recheck routing | Investigate the turn |
| Accepted reports exist, no snapshot | Snapshot calculation gap | Snapshot backfill |
| Supported route, no reports; completed runs with zero rows | Vendor had nothing | No action; retries continue |
| Supported route, no reports; failed runs or no runs | Ingestion gap | Backfill |

## 5. Design

### D1 — BUG-008

Tokenize for mentions with a ZWNJ-preserving variant, so a ZWNJ compound stays one mention. `QueryNormalization.Normalize` keeps its behaviour for routing and scoring. The resolver is unchanged: `NormalizeIdentity` already removes joiners, so tickers containing ZWNJ keep working.

This does not cover a space-typed word. Coverage of that case, and of tickers that equal ordinary words, is the shared common-word policy of OQ-011 and OQ-PROPOSED-C3. If the owner chooses a list, one owner `TickerMentionPolicy` in Application is consulted by both `SymbolSnapshotIntentRules` and mention extraction.

Regression tests, written first and failing: a ZWNJ compound yields one mention; a compound over a catalog row whose symbol equals its first part is `NotFound`, not `Resolved`; a ticker with ZWNJ still resolves.

### D2 — BUG-009

In `ConversationDialogueGate.PrepareAsync`, before the slot step:

1. Compute an **explicit company**: a mention that resolves by an exact kind only (`exact_ticker`, `exact_company_name`, `approved_alias`), never by substring or fuzzy.
2. When no capability is detected, an active task exists with no pending action, and an explicit company exists, take the capability from the active task and validate against the explicit company. It then wins at the carry-over step (`ConversationTaskStateContracts.cs:701`) and is labelled `UserExplicit`.
3. When the model proposes a capability but no entity span, resolve the deterministic mentions for an explicit company. If one is found, it is used instead of carry-over.

Follow-ups that name nothing, such as `همین رو جدولی کن`, keep today's carry-over.

Regression tests, written first and failing, with fakes: after a monthly turn for `شپنا`, `کگل هم نشون بده` answers `کگل` on the deterministic and model-unavailable paths; a capability-with-no-entity proposal answers `کگل`; a follow-up that names nothing keeps `شپنا`; an ordinary word is not an explicit company.

### D3 — BUG-010

- **Canonical priority.** Apply provider priority across the whole exact match, not per step: eligible Noavaran rows first, then other Noavaran rows, then other providers. A non-canonical `Ticker` hit no longer blocks the alias step. Regression tests: duplicate legacy `Ticker` rows and duplicate non-eligible Noavaran rows both resolve to the canonical row; two rows that are truly distinct stay ambiguous.
- **Two companies, one-company capability.** When the capability has no multi-company slot and two distinct companies resolve exactly, return `ClarificationNeeded` with a new reason code and a pending disambiguation holding both companies (the existing `PendingDialogueAction` candidates). Options are OQ-PROPOSED-C2. Regression test: `روند فروش ماهانه کگل/شپنا` does not produce a single-company frame and does not produce `entity_ambiguous`.
- **Fallback path.** A pair joined by a separator is not read as a product mention (`DeterministicCapabilityInterpreter.cs:168-178`).
- **Budget.** `CanonicalQueryEntityResolver.cs:60` loads all of `Companies` once per mention. Load once per turn, so that D2 and D3 do not add load.

### D4 — distinct reply for no monthly route

Yes: a distinct reply is justified. Today the user is told the request is supported and data is missing, which is misleading for a company we cannot fetch monthly data for.

Fit with `AiDialogueOutcomePolicy`: Outcome stays **`NoData`**, so billing stays zero-charge and the Feature 141 fallback rules are unchanged. The reason changes.

- New reason `monthly_report_not_covered` in `DialogueOutcomeReasonCodes`.
- New Application port `IMonthlyReportCoverageReader` with a plain result `{ HasRoute | NoRoute(categoryFa) | OutsideScope | Unknown }`. Infrastructure implements it by reading the canonical row's `ReportingType` through `INoavaranMonthlyReportTypeResolver`. The resolver stays the one owner of the code-to-route map, and no provider code enters Application.
- `MonthlyActivityTrendCapabilityExecutor` calls the port only when the result is null. `NoRoute` gives the new reason. `OutsideScope` gives the existing `data_stale_or_ineligible`. `HasRoute` and `Unknown` stay `supported_but_no_rows`.
- A one-row read in the no-data branch only. A port failure is logged and falls back to `supported_but_no_rows`.

Reply text, to be placed by Track A, who own refusal wording. Editing `AiDialogueOutcomePolicy` is a stated dependency:

> پوشش گزارش ماهانهٔ فروش برای شرکت‌های {categoryFa} در منبع دادهٔ فعلی فراهم نیست، بنابراین برای «{symbol}» نمودار فروش ماهانه نداریم.

This wording avoids claiming that no report exists, and avoids the Feature 155 soft-no-answer phrases.

Regression tests: each of the seven codes gets the new reason; supported codes, null, and unknown keep today's reason; a port failure does not fail the turn.

### D5 — monthly trend reads the canonical row

`MonthlyActivityTrendQueryUseCase` uses the existing provider-scoped `ICompanyResolutionOutcomeService.ResolveAsync(symbol, NoavaranCurrentApi)`. `Ambiguous` maps to the existing ambiguous outcome. Regression test: a Noavaran row plus another provider's row with the same ticker returns the Noavaran snapshot.

### D6 — capability-less message

Continue the active task when no capability is detected only if a pending action exists, an explicit company exists (D2), or the message carries an explicit continuation cue. Otherwise the turn is `Unsupported` as on the model path. Options are OQ-PROPOSED-C5. Regression test: `ممنون` inside an active task does not produce a frame.

## 6. Cross-cutting

**Database:** no schema change, no write, no migration. Read-only verification happens before planning finishes; its SQL is delivered to the lead and is not stored in this repository.

**API and frontend:** no contract change. New reason codes are additive strings in the persisted payload. No frontend change is expected, because `ComposeSystemMessage` already renders the text.

**External integrations:** none. Whether Noavaran has endpoints for no-route categories is a question for the vendor.

**Security, reliability, error handling:** no new outbound call. D4 adds one bounded read with existing timeout behaviour and a fail-safe fallback. D3 lowers database load. Mentions come from user text and remain parameters to in-memory comparison only.

**Testing:** failing regression tests first for each defect, in `tests/FinancialCopilot.UnitTests`, using fakes and invented rows. V1-freeze tests must stay green. Green means no new failure against `specs/test-baseline.md`.

**Observability:** `reasonCode` already reaches the dialogue event sink. Add a bounded `entity_source` tag (`explicit` or `carried_over`) so that BUG-009 paths can be measured in production.

## 7. Alternatives considered

| Topic | Alternative | Verdict |
|---|---|---|
| D1 | Add more stop-words | Rejected as the only fix: unbounded, and it does not fix the split |
| D1 | Limit the resolver to eligible rows | Rejected for now: changes answers for funds and off-market companies; that is a product decision |
| D2 | Pass conversation history to the model proposal | Rejected: cost and non-determinism, and it does not cover the model-failure path that is reachable today |
| D3 | Pick the first resolved ticker and add a note | Option in OQ-PROPOSED-C2; not recommended |
| D4 | Reuse `data_stale_or_ineligible` | Rejected: the data is not stale, and the distinct reason is what lets 155 report this group |
| D4 | Change the return type of `IMonthlyActivityTrendQueryUseCase` | Rejected: wider blast radius, including Feature 156 |
| D5 | Fix `ResolveBySymbolAsync` globally | Rejected: about 15 consumers, including CyclicalWaves normalizers that need other-provider rows. Candidates for a follow-up: `MonthlyProductTrendQueryUseCase`, `MonthlyProductComparisonUseCase`, `ProductRevenueMixQueryUseCase` |

## 8. Effect on Feature 155 classifier v1

| Item | Before | After | Bucket effect |
|---|---|---|---|
| D1 | Often `answered` for a wrong company | `entity_not_found` | Moves to `no_data`; gap rate rises slightly, honestly |
| D2, D6 | `answered` for a wrong company | Answered correctly, or `unsupported` for D6 | `answered` is more truthful; D6 may raise `unsupported` |
| D3 | `clarification` (reason `entity_ambiguous`) | `answered` when canonical priority removes duplicates; otherwise `clarification` under the new reason | `clarification` falls |
| D4 | `no_data`, `supported_but_no_rows` | `no_data`, new reason | Bucket unchanged; Q4 shows the split |
| D5 | `no_data` | `answered` | Moves out of the gap list |

Theme regexes are unaffected. New reason codes do not need a classifier version bump unless the owner wants a new bucket (OQ-PROPOSED-C4). Track D should expect both new reason codes.

## 9. Risks and dependencies

- **Track A (routing rules, refusal text):** overlaps in `DeterministicCapabilityInterpreter` stop-lists and the gate; wording in `AiDialogueOutcomePolicy`. Merge order to be set by the lead.
- **Track B (symbol overview executor):** benefits from canonical priority; must call the same resolver and not add a second ticker rule.
- **Track D (admin report):** must add the new reason codes to any allowed-value list.
- **Feature 156:** `appsettings.json:605` has `SymbolSnapshot:Enabled: true` in the committed default, while the Feature 156 spec and OQ-011 describe the flag as off in production. Production's value is unverified.
- Canonical priority changes which row a ticker resolves to; existing resolver tests pin some of this and must be reviewed, not bulk-updated.
- D6 is the most behaviour-changing item; tests pinning current carry-over must be read one by one.
- Per-ticker causes remain unknown until the read-only check runs; D4's real reach cannot be sized yet.

## 10. Open questions

See `specs/open-questions.md` once the lead records them.

- **OQ-011 (existing):** tickers equal to common words. Facts and options are in the report to the lead.
- **OQ-PROPOSED-C1:** no-route categories: explanatory reply only, or also seek vendor endpoints.
- **OQ-PROPOSED-C2:** two tickers in a one-company capability: clarify, answer first with a note, or build comparison.
- **OQ-PROPOSED-C3:** ordinary-word ticker named in a follow-up or sentence: does it switch the company.
- **OQ-PROPOSED-C4:** whether Feature 155 needs a separate `not_covered` bucket.
- **OQ-PROPOSED-C5:** capability-less messages inside an active task.

## 11. Files expected to change at implementation

- `src/backend/FinancialCopilot.Application/AI/Orchestration/QueryNormalization.cs` (D1)
- `src/backend/FinancialCopilot.Application/AI/Orchestration/DeterministicCapabilityInterpreter.cs` (D1, D3)
- `src/backend/FinancialCopilot.Application/AI/Orchestration/ConversationTaskStateContracts.cs` (D2, D3, D6)
- `src/backend/FinancialCopilot.Application/AI/Orchestration/AiDialogueOutcomePolicy.cs` (D3, D4; reason codes, text by agreement with Track A)
- `src/backend/FinancialCopilot.Application/AI/Orchestration/SemanticCapabilityExecutors.cs` (D4)
- `src/backend/FinancialCopilot.Application/FinancialData/Ingestion/MonthlyActivityTrendQueryContracts.cs` or a new file beside it (D4 port)
- `src/backend/FinancialCopilot.Infrastructure/AI/OrchestrationV2/Adapters/CanonicalQueryEntityResolver.cs` (D3)
- `src/backend/FinancialCopilot.Infrastructure/Financial/Ingestion/NadpcoApi/MonthlyActivityTrendQueryUseCase.cs` (D5)
- New Infrastructure adapter for the D4 port, and its registration in `ServiceCollectionExtensions.cs`
- `tests/FinancialCopilot.UnitTests`: new regression test files for each item

Conditional: `SymbolSnapshotIntentRules.cs` and a new `TickerMentionPolicy.cs` only if OQ-011 and OQ-PROPOSED-C3 choose a word list.

## 12. Next step

Present to the owner for **Gate A**, item by item. No `user-story.md`, `tasks.md`, or production change until each approval is recorded.
