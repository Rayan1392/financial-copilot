# User Story - Dynamic Metric Alias Learning

> Depends on `015` (semantic metric catalog), `007` (scanner parser), `045` (symbol lookup),
> and `028` (missing-answer feedback). Uses `018` telemetry optionally.

## Story

As a scanner user,
I want the system to understand market shorthand and user-specific phrasing such as `PE`, `P/E`,
`PS`, `P/S`, and Persian spoken forms without requiring a code deployment for every new synonym,
so that common user language is learned from real prompts and resolved dynamically to governed
financial metric codes.

As a platform operator,
I want unresolved metric terms from user queries to become reviewable and, when safe, automatically
promoted into a dynamic alias catalog,
so that the semantic layer improves from logs while preserving deterministic financial semantics.

## Problem

Metric aliases are currently embedded in `PhaseOneFinancialSemanticCatalog.cs`. That is too rigid:

1. New user terms require a developer to edit C# and redeploy.
2. Parser misses are captured as feedback, but they do not automatically improve alias resolution.
3. Common market shorthand can appear in many forms (`PE`, `P/E`, `پی به ای`, `پی ای`, `p e`) and
   should be learned as aliases for the same governed metric.
4. The system must not let an LLM invent metric definitions, formulas, calculations, or SQL. Only
   the mapping from user expression to an existing canonical `MetricCode` may be learned dynamically.

## Scope

This story introduces a database-backed dynamic alias layer on top of the static semantic catalog.
Static definitions and policies remain the source of truth for canonical metrics, supported periods,
dependencies, and calculation formulas. Dynamic learning only adds or updates aliases that point to
existing canonical metric definitions.

## Acceptance Criteria

- Dynamic aliases are stored in PostgreSQL and loaded by `IMetricAliasResolver` alongside static
  aliases from `PhaseOneFinancialSemanticCatalog`.
- Alias resolution order is deterministic:
  1. exact active dynamic alias match,
  2. exact static alias match,
  3. normalized/fuzzy dynamic candidate match above a configured threshold,
  4. otherwise `NotFound` or `Ambiguous`.
- The system captures unresolved metric terms from scanner and symbol-lookup parser flows into an
  alias-learning log. This reuses or extends the existing `028` missing-answer feedback pipeline.
- A background learning worker groups unresolved terms by normalized expression, language, source
  intent, and surrounding query context, then creates `MetricAliasCandidate` rows.
- Candidates include suggested canonical metric codes, confidence, evidence examples, frequency,
  first/last seen timestamps, and status (`New`, `AutoApproved`, `NeedsReview`, `Rejected`,
  `Promoted`, `Disabled`).
- Suggestions can be produced by deterministic rules, existing successful query patterns, and an
  LLM classifier, but the LLM output is never trusted directly. Suggested metric codes must exist
  in the governed registry and pass validation.
- Safe aliases may be auto-promoted only when all configured gates pass:
  - high confidence,
  - repeated frequency across queries or actors,
  - candidate maps to exactly one active metric definition,
  - no collision with an existing alias for another metric,
  - expression is not a stop word, symbol name, industry name, or unsupported metric concept.
- Risky or ambiguous aliases require DataAdmin approval through an admin API before promotion.
- Operators can approve, reject, disable, or roll back dynamic aliases without code changes.
- Dynamic aliases are cacheable but invalidated when aliases are promoted, disabled, or edited.
- The public AI facade still uses only `POST /api/ai/v1/query`; no public parser endpoint is added.
- Financial formulas, metric definitions, calculation policies, and SQL remain code-governed and are
  never created or changed by this learning loop.

## Required Examples

The following terms must resolve after seeding or learning dynamic aliases:

| User term | Language | Canonical metric |
|---|---|---|
| `PE` | `en-US` / `fa-IR` | `PE_TTM` |
| `P/E` | `en-US` / `fa-IR` | `PE_TTM` |
| `پی ای` | `fa-IR` | `PE_TTM` |
| `پی به ای` | `fa-IR` | `PE_TTM` |
| `PS` | `en-US` / `fa-IR` | `PS_TTM` |
| `P/S` | `en-US` / `fa-IR` | `PS_TTM` |
| `پی اس` | `fa-IR` | `PS_TTM` |
| `پی به اس` | `fa-IR` | `PS_TTM` |

## Learning Flow

```text
User query
  -> Parser extracts metric term
  -> Dynamic+static alias resolver fails or returns ambiguous
  -> Missing-answer feedback logs unresolved metric term and context
  -> Alias learning worker groups repeated unresolved terms
  -> Candidate generator suggests existing canonical metric code
  -> Validation gates decide AutoApprove vs NeedsReview
  -> Promotion creates/updates active DynamicMetricAlias row
  -> Resolver cache invalidated
  -> Future queries resolve without code deployment
```

## Admin Flow

```text
GET /api/v1/admin/metric-alias-candidates
  -> shows unresolved terms, suggested metric, confidence, examples

POST /api/v1/admin/metric-alias-candidates/{id}/approve
  -> promotes candidate to active dynamic alias
  -> records reviewer, reason, timestamp
  -> invalidates resolver cache

POST /api/v1/admin/metric-aliases/{id}/disable
  -> disables a bad alias without deleting audit history
```

## Data Model

### DynamicMetricAlias

- `Id`
- `Expression`
- `NormalizedExpression`
- `Language`
- `MetricCode`
- `MetricVersion`
- `PeriodType`
- `ComparisonQualifier`
- `Source` (`Seeded`, `AutoLearned`, `AdminApproved`)
- `Status` (`Active`, `Disabled`)
- `Confidence`
- `EvidenceJson`
- `CreatedAt`
- `CreatedBy`
- `ApprovedAt`
- `ApprovedBy`
- `DisabledAt`
- `DisabledBy`
- `DisableReason`

Unique active index:

```text
(NormalizedExpression, Language, MetricCode, PeriodType, ComparisonQualifier)
where Status = Active
```

### MetricAliasCandidate

- `Id`
- `Expression`
- `NormalizedExpression`
- `Language`
- `SuggestedMetricCode`
- `SuggestedMetricVersion`
- `Status`
- `Confidence`
- `FrequencyCount`
- `DistinctActorCount`
- `FirstSeenAt`
- `LastSeenAt`
- `EvidenceExamplesJson`
- `RejectionReason`
- `PromotedAliasId`

## Validation Rules

- A dynamic alias cannot point to a nonexistent or inactive metric definition.
- A dynamic alias cannot conflict with an active alias for another metric unless an admin explicitly
  disables the older alias first.
- A dynamic alias cannot create a new `MetricCode`.
- A dynamic alias cannot alter `MetricCalculationPolicy`, dependencies, units, or formulas.
- Candidate generation must treat symbol names, company names, industry names, and ordinary Persian
  words as non-metric terms unless evidence is strong.
- Ambiguous expressions must return clarification instead of silently picking one metric.

## Out Of Scope

- Auto-creating new financial metrics.
- Auto-generating metric calculators or formulas.
- Auto-modifying `PhaseOneFinancialSemanticCatalog.cs`.
- Auto-training or fine-tuning LLMs.
- Public alias-management endpoints for ordinary users.
- Letting user prompts or LLM output execute SQL or change deterministic calculations.

## Verification

- Unit tests prove dynamic aliases resolve before static aliases and respect language/period/context.
- Unit tests prove `PE` and `PS` resolve to `PE_TTM` and `PS_TTM` through dynamic aliases.
- Integration tests prove unresolved parser terms create candidates.
- Integration tests prove admin approval promotes a candidate and invalidates resolver cache.
- Integration tests prove conflicts are rejected and disabled aliases are ignored.
- Full backend test suite passes with no scanner or symbol-lookup regression.
