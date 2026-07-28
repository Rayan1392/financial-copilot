# Tasks - Dynamic Metric Alias Learning

## Acceptance Gate

Do not implement this story until it is selected in `specs/implementation-checklist.md`.
Completion requires dynamic alias persistence, resolver integration, candidate generation,
admin approval/rollback, cache invalidation, and regression tests.

## Task List

### Domain Contracts

**Task 1.1: Add dynamic alias domain models**
- Location: `src/backend/FinancialCopilot.Domain/Financial/Metrics/`
- Add value objects/entities:
  - `DynamicMetricAlias`
  - `MetricAliasCandidate`
  - `MetricAliasSource`
  - `MetricAliasStatus`
  - `MetricAliasCandidateStatus`
  - `MetricAliasLearningDecision`
- Keep domain models framework-free.
- Enforce invariants:
  - expression and normalized expression are required,
  - language is required,
  - active aliases require a valid canonical `MetricCode`,
  - disabled aliases retain audit metadata.

**Task 1.2: Add expression normalization service contract**
- Location: `src/backend/FinancialCopilot.Application/FinancialData/Metrics/`
- Add `IMetricAliasExpressionNormalizer`.
- Normalize:
  - trim/collapse whitespace,
  - lower-case Latin text,
  - normalize slash/no-slash forms (`p/e`, `pe`, `p e`),
  - Persian Arabic/Kaf/Yeh variants,
  - zero-width joiners,
  - Persian and English digits.
- Add deterministic tests for Persian and Latin cases.

### Application Contracts

**Task 2.1: Add dynamic alias repository interfaces**
- Location: `src/backend/FinancialCopilot.Application/FinancialData/Metrics/`
- Add `IDynamicMetricAliasRepository`:
  - `GetActiveAliasesAsync(language, cancellationToken)`
  - `FindActiveAliasAsync(normalizedExpression, language, context, cancellationToken)`
  - `UpsertAliasAsync(alias, cancellationToken)`
  - `DisableAliasAsync(aliasId, actorId, reason, cancellationToken)`
- Add `IMetricAliasCandidateRepository`:
  - `UpsertCandidateAsync(candidate, cancellationToken)`
  - `QueryCandidatesAsync(filters, cancellationToken)`
  - `ApproveCandidateAsync(candidateId, actorId, reason, cancellationToken)`
  - `RejectCandidateAsync(candidateId, actorId, reason, cancellationToken)`

**Task 2.2: Add dynamic alias resolver**
- Location: `src/backend/FinancialCopilot.Application/FinancialData/Metrics/`
- Add `DynamicMetricAliasResolver` or `CompositeMetricAliasResolver` that wraps the existing
  static `MetricAliasResolver`.
- Resolution order:
  1. exact active dynamic alias,
  2. exact static alias,
  3. configured fuzzy dynamic alias match,
  4. not found or ambiguous.
- Do not call an LLM during query-time alias resolution.
- Cache active aliases per language with an invalidation token.

**Task 2.3: Add unresolved term sink**
- Extend parser flows with an application seam:
  - `IMetricAliasLearningSignalCollector`
  - `MetricAliasLearningSignal`
- Signals include:
  - original query text,
  - extracted term,
  - normalized term,
  - language,
  - parser intent (`Scanner`, `SymbolLookup`),
  - actor/tenant ids,
  - correlation id,
  - ambiguity/not-found status,
  - candidate symbols or nearby words where available.
- This may reuse `IMissingAnswerFeedbackCollector`, but alias learning signals must preserve the
  unresolved expression explicitly.

### Persistence

**Task 3.1: Add EF rows**
- Location: existing semantic persistence area:
  `src/backend/FinancialCopilot.Infrastructure/Financial/Semantics/Persistence/`
- Add:
  - `DynamicMetricAliasRow`
  - `MetricAliasCandidateRow`
  - `MetricAliasLearningSignalRow` if signals are stored separately from `MissingAnswerFeedbacks`.

**Task 3.2: Configure EF mappings**
- Add indexes:
  - active alias lookup by `(NormalizedExpression, Language)`,
  - active alias uniqueness by `(NormalizedExpression, Language, MetricCode, PeriodType, ComparisonQualifier)`,
  - candidate lookup by `(NormalizedExpression, Language, Status)`,
  - candidate trend by `LastSeenAt`,
  - audit lookup by `ApprovedBy`, `DisabledBy`.
- Store evidence/context as bounded JSON text.
- Add max lengths to prevent unbounded user input storage.

**Task 3.3: Add migration**
- Add EF migration for `SemanticCatalogDbContext` or the chosen semantic persistence context.
- Migration creates dynamic alias, candidate, and optional signal tables.
- Confirm migration is additive and does not mutate existing static catalog tables.

### Learning Worker

**Task 4.1: Add candidate generation service**
- Add `IMetricAliasCandidateGenerator`.
- Inputs: unresolved alias signals and successful resolved query examples.
- Outputs: candidate expression, suggested metric code, confidence, evidence examples.
- Candidate sources:
  - deterministic shorthand rules (`pe` -> possible `PE_TTM`, `ps` -> possible `PS_TTM`),
  - nearest-neighbor similarity against existing aliases,
  - co-occurrence with successful parser outputs,
  - optional LLM classifier.
- All suggestions must validate against `IFinancialMetricRegistry`.

**Task 4.2: Add validation gates**
- Add `MetricAliasLearningPolicy`.
- Configurable thresholds:
  - minimum confidence for auto-approval,
  - minimum frequency,
  - minimum distinct actors,
  - maximum ambiguity count,
  - fuzzy-match threshold.
- Reject or mark `NeedsReview` when:
  - candidate conflicts with another metric,
  - candidate is likely a symbol/company/industry name,
  - candidate maps to multiple metrics,
  - candidate points to an inactive metric.

**Task 4.3: Add background worker**
- Location: Worker project or Infrastructure service used by Worker.
- Periodically reads unresolved signals and updates candidates.
- Bounded batch size and retry handling.
- Idempotent candidate upsert by `(NormalizedExpression, Language)`.
- Emits telemetry for candidate created/promoted/rejected.

**Task 4.4: Add auto-promotion path**
- Auto-promote only when validation gates pass.
- Promotion creates an active `DynamicMetricAliasRow`.
- Invalidate alias resolver cache.
- Record source `AutoLearned`, confidence, evidence, and timestamp.

### Admin API

**Task 5.1: Add candidate query endpoint**
- Add protected DataAdmin endpoint:
  - `GET /api/v1/admin/metric-alias-candidates`
- Filters:
  - status,
  - language,
  - metric code,
  - date range,
  - min frequency,
  - search expression.
- Response includes evidence examples and conflict warnings.

**Task 5.2: Add approval/rejection endpoints**
- Add:
  - `POST /api/v1/admin/metric-alias-candidates/{id}/approve`
  - `POST /api/v1/admin/metric-alias-candidates/{id}/reject`
  - `POST /api/v1/admin/metric-aliases/{id}/disable`
- Require DataAdmin permission.
- Require reason text for reject/disable.
- Approval validates again transactionally before creating active alias.

**Task 5.3: Add audit visibility**
- Alias changes must be auditable:
  - who approved/rejected/disabled,
  - when,
  - reason,
  - before/after values,
  - correlation id where available.
- Reuse existing admin audit patterns if possible.

### Parser Integration

**Task 6.1: Update scanner parser**
- `LlmScannerQueryParser` continues extracting user terminology.
- `IMetricAliasResolver` resolves through composite dynamic+static resolver.
- On `NotFound` or `Ambiguous`, emit alias-learning signal.
- Query response behavior remains unchanged: clarification required where appropriate.

**Task 6.2: Update symbol lookup parser**
- `LlmSymbolLookupParser` uses the same composite resolver.
- On unresolved metric term, emit alias-learning signal.
- Ensure direct lookup questions like `PE سهم حفاری؟` and `PS سهم فجر؟` can be learned/resolved
  without code changes after alias promotion.

**Task 6.3: Keep deterministic execution unchanged**
- Scanner and symbol lookup services consume only resolved canonical `MetricCode`.
- No dynamic alias path may change execution SQL, metric calculations, or billing.

### Seed Data

**Task 7.1: Seed known market shorthand aliases**
- Seed initial dynamic aliases or static seed rows for:
  - `PE`, `P/E`, `p e`, `پی ای`, `پی به ای` -> `PE_TTM`
  - `PS`, `P/S`, `p s`, `پی اس`, `پی به اس` -> `PS_TTM`
- Mark source as `Seeded`.
- Make seed idempotent.

**Task 7.2: Consider migration from static aliases**
- Decide whether market shorthand remains in `PhaseOneFinancialSemanticCatalog.cs` or moves to
  seed rows.
- Preferred target state: `PhaseOneFinancialSemanticCatalog.cs` keeps canonical definitions and
  minimal stable aliases; operational synonyms live in dynamic alias tables.

### Tests

**Task 8.1: Unit tests for normalization**
- `PE`, `pe`, `P/E`, `p e` normalize consistently.
- Persian variants normalize consistently.
- Whitespace, zero-width joiners, and digit variants are handled.

**Task 8.2: Unit tests for composite resolver**
- Dynamic alias resolves before static alias.
- Static alias still resolves when no dynamic alias exists.
- Disabled dynamic alias is ignored.
- Conflicting aliases return `Ambiguous` or fail validation.
- `PE` resolves to `PE_TTM`; `PS` resolves to `PS_TTM`.

**Task 8.3: Candidate generation tests**
- Repeated unresolved term creates or updates one candidate.
- Candidate frequency increments.
- Candidate with unknown metric suggestion is rejected.
- Candidate with single high-confidence metric can be auto-promoted.
- Ambiguous candidate requires review.

**Task 8.4: Repository integration tests**
- Active alias lookup works.
- Candidate query filters work.
- Approve candidate creates alias transactionally.
- Disable alias removes it from resolution without deleting audit history.

**Task 8.5: Parser integration tests**
- Scanner parser emits learning signal for unresolved metric term.
- Symbol lookup parser emits learning signal for unresolved metric term.
- After dynamic alias exists, both parsers resolve the term without clarification.
- Collector/repository failure does not break user query response.

**Task 8.6: Admin endpoint tests**
- DataAdmin can list, approve, reject, and disable.
- Non-admin and API client without permission are rejected.
- Approval conflict returns validation error.
- Cache invalidation occurs after approval/disable.

### Documentation

**Task 9.1: Update semantic layer docs**
- Document static vs. dynamic alias responsibilities.
- Document why formulas and canonical metric definitions remain code-governed.

**Task 9.2: Update operations docs**
- Document candidate review workflow.
- Document auto-approval thresholds and rollback.
- Document how to inspect alias-learning trends.

**Task 9.3: Update implementation checklist after implementation**
- Mark this story complete only after full backend verification.
- Add completion evidence and any deferred items.

## Verification Checklist

- [ ] Dynamic alias tables exist and migrations apply.
- [ ] Resolver uses dynamic aliases without code deployment.
- [ ] Unresolved user terms become learning signals/candidates.
- [ ] Candidate generator groups repeated misses.
- [ ] Safe candidates can auto-promote under policy gates.
- [ ] Risky candidates require DataAdmin approval.
- [ ] Alias approval/disable invalidates resolver cache.
- [ ] `PE` and `PS` resolve to `PE_TTM` and `PS_TTM`.
- [ ] No dynamic alias can create formulas, SQL, or metric definitions.
- [ ] Unit, integration, and architecture tests pass.
