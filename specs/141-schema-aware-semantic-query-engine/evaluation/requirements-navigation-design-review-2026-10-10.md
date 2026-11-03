# Feature 141 requirements extraction and vectorless navigator design review

Date: 2026-10-10  
Scope: read-only architecture and evidence review  
Implementation changes: none

## Executive conclusion

Recommend **Option B: separate semantic intent from schema navigation**, implemented incrementally behind adapters to the current planner and compiler.

The vectorless, catalog-governed premise is not the primary problem. The dominant semantic-error source is the current requirements-extraction stage, which asks one LLM response to select catalog references, classify analytical operation, construct period semantics, identify entities, and emit most of a query-plan shape. The navigator then treats those requirements as authoritative and primarily locates context nodes; it does not independently correct a wrong measure. The deterministic planner generally preserves the extracted contract and rejects incompatible combinations. PostgreSQL evidence shows that compilation, restricted execution, and Persian rendering are dependable when supplied a valid plan.

Two reported quality conclusions require correction:

1. Holdout-v3's 0% operation and 0% period scores each have an applicable denominator of **one**, not nine. The sole applicable case, `readiness-holdout-v3-09`, failed during requirements extraction and therefore had no operation or period output.
2. Several v3 and frozen-v2/dev expected measure labels contradict their Persian questions. For example, a threshold on tons sold is labeled `MONTHLY_SALES_GROWTH_YOY`, and a three-month production ranking is also labeled sales growth. Those cases measure annotation error rather than model error.

The evidence does not justify a rewrite or abandoning vectorless navigation. It justifies establishing one canonical semantic-intent owner before catalog binding and removing duplicated semantic decisions from navigation and planning.

## 1. Actual runtime architecture

### 1.1 End-to-end trace

The offline Feature 141 path is:

```text
Persian question
  -> LlmVectorlessSchemaNavigator.ExtractAsync
       LLM: QuestionSchemaRequirements + StructuredPlanShape
       deterministic NormalizeRequirements
       ValidateRequirementsAgainstOutline
       ValidateRequirementPolicies
  -> LlmVectorlessSchemaNavigator.NavigateAsync
       force required catalog IDs into compact outline
       LLM: selected node IDs only
       expand catalog context
       SchemaContextCompletenessValidator
  -> OfflineStructuredQueryPlanningService.PlanAsync
       company/product/population/equivalence binding
       selected special-case normalizations
       StructuredQueryPlanBuilder
       StructuredQueryPlanValidator
  -> StructuredQueryCompiler
  -> restricted read-only PostgreSQL executor
  -> PersianStructuredQueryResponseGenerator
```

The composition is visible in [`OfflineStructuredQueryPlanningService.PlanAsync`](../../../src/backend/FinancialCopilot.Application/FinancialData/Semantics/Navigation/StructuredQueryPlanning.cs#L554), which calls extraction, navigation, context expansion, completeness validation, identity binding, plan building, and deterministic validation in that order.

### 1.2 Independent requirements extraction

[`QuestionSchemaRequirements`](../../../src/backend/FinancialCopilot.Application/FinancialData/Semantics/Navigation/SchemaNavigationContracts.cs#L16) contains all of the following in one contract:

- catalog measure, dimension, relationship, and financial-reference IDs;
- company and product mentions;
- source, statement, period, comparison, aggregation, ordering, and presentation policies;
- ambiguity state;
- a string `RequestedFinancialValueOperation`;
- a full [`StructuredPlanShape`](../../../src/backend/FinancialCopilot.Application/FinancialData/Semantics/Navigation/StructuredQueryPlanning.cs#L119), including filters, aggregation, sort, pagination, operation kind, comparison mode, date bounds, and maximum periods.

[`ExtractAsync`](../../../src/backend/FinancialCopilot.Application/FinancialData/Semantics/Navigation/LlmVectorlessSchemaNavigator.cs#L63) supplies a compact catalog outline to the LLM and requires exact catalog IDs. Its prompt also defines measure semantics, statement-scope rules, operation rules, period conversion, ranking behavior, cross-company behavior, and unsupported-derived behavior. This is not merely requirements extraction; it performs semantic interpretation, catalog selection, and much of logical planning in one output.

After the LLM responds, [`NormalizeRequirements`](../../../src/backend/FinancialCopilot.Application/FinancialData/Semantics/Navigation/LlmVectorlessSchemaNavigator.cs#L829) rewrites reference types, comparison scopes, operation kind, comparison mode, aggregation, sort, and the top-level operation. It uses both typed fields and Persian/English substring checks for growth and comparison language. [`ValidateRequirementPolicies`](../../../src/backend/FinancialCopilot.Application/FinancialData/Semantics/Navigation/LlmVectorlessSchemaNavigator.cs#L1268) then rejects unsupported operation values by throwing `SchemaNavigationOutputException`.

### 1.3 Vectorless navigation

The compact hierarchy provider lexically scores normalized question tokens against concepts, source-item titles, metric descriptions, and aliases. Optional nodes require score 12, direct source-item candidates require score 20, and the outline is capped at ten optional nodes and twelve source candidates before budget trimming. Required IDs supplied by extraction are force-included with their ancestors. See [`GetCompactOutlineAsync`](../../../src/backend/FinancialCopilot.Application/FinancialData/Semantics/Navigation/JsonCatalogNavigationHierarchyProvider.cs#L25) and its scoring functions at [lines 469–527](../../../src/backend/FinancialCopilot.Application/FinancialData/Semantics/Navigation/JsonCatalogNavigationHierarchyProvider.cs#L469).

The navigation LLM emits only `selectedNodeIds`, `missingRequirements`, `expansionNodeIds`, and `outcome`. Its prompt explicitly says that node IDs are context anchors and that exact answer references come from independent requirements ([`BuildNavigationPrompt`](../../../src/backend/FinancialCopilot.Application/FinancialData/Semantics/Navigation/LlmVectorlessSchemaNavigator.cs#L719)).

Most importantly, the final `SelectedMeasureIds` are not an independent navigator interpretation. They are computed as:

```text
independentlyExtractedRequirements.MeasureIds ∩ expandedContext.MeasureIds
```

at [`NavigateAsync` lines 436–445](../../../src/backend/FinancialCopilot.Application/FinancialData/Semantics/Navigation/LlmVectorlessSchemaNavigator.cs#L436). Therefore the evaluation's “navigator measure selection” metric mostly measures requirements extraction plus context availability. A wrong extractor measure is normally made self-confirming because that ID is force-included in the navigator outline.

### 1.4 Completeness and governance

[`SchemaContextCompletenessValidator`](../../../src/backend/FinancialCopilot.Application/FinancialData/Semantics/Navigation/SchemaNavigationContracts.cs#L283) separately checks catalog identity, ambiguity, allowed operation, required comparison scope, presence of every requirement, direct-source identity, semantic approval, and query authorization.

This separation is safety-positive. However, `Unsupported` conflates at least three materially different outcomes:

- semantically unrepresentable or unsupported operation;
- missing/ambiguous intent;
- representable intent whose catalog nodes are still unapproved or unauthorized.

All 45 completed records across the original 50 plus v3 ended as either `UnsupportedOperation` (37) or `SemanticValidationFailure`/clarification (8), largely because catalog node governance is pending. That terminal status masks whether the semantic interpretation itself was right.

### 1.5 Identity and population resolution

Named companies and products are bound after navigation. [`StructuredQueryCompanyBinder`](../../../src/backend/FinancialCopilot.Infrastructure/Financial/Semantics/StructuredQueryCompanyBinder.cs#L7) reuses the canonical company resolver and requires tenant scope. [`StructuredQueryProductBinder`](../../../src/backend/FinancialCopilot.Infrastructure/Financial/Semantics/StructuredQueryProductBinder.cs#L7) uses company-scoped provider product identity.

Dynamic populations are supported without a fixed company registry. Reviewed populations and product equivalence use owner-approved exact alias records in [`ReviewedSemanticScopeCatalogs.cs`](../../../src/backend/FinancialCopilot.Application/FinancialData/Semantics/Navigation/ReviewedSemanticScopeCatalogs.cs#L21). Product-driven company discovery uses an approved product concept and tenant discovery scope in [`PopulationDiscoveryCatalog.cs`](../../../src/backend/FinancialCopilot.Application/FinancialData/Semantics/Navigation/PopulationDiscoveryCatalog.cs#L22). This preserves the approved equivalence `گندله = گندله سنگ‌آهن` through reviewed aliases rather than question-specific SQL.

The planner also contains a narrow Persian phrase classifier for producer-sales ranking ([`ProducerSalesQueryIntentClassifier`](../../../src/backend/FinancialCopilot.Application/FinancialData/Semantics/Navigation/ProducerSalesQueryIntentClassifier.cs#L7)) and performs substantial post-navigation requirement rewriting for population queries ([`PlanAsync` lines 655–837](../../../src/backend/FinancialCopilot.Application/FinancialData/Semantics/Navigation/StructuredQueryPlanning.cs#L655)). These are secondary semantic owners and increase drift risk.

### 1.6 Planning, validation, compilation, and execution

[`StructuredQueryPlanBuilder`](../../../src/backend/FinancialCopilot.Application/FinancialData/Semantics/Navigation/StructuredQueryPlanning.cs#L199) is mostly a deterministic projection from requirements and bound context. It does not reinterpret the question. [`StructuredQueryPlanValidator`](../../../src/backend/FinancialCopilot.Application/FinancialData/Semantics/Navigation/StructuredQueryPlanning.cs#L316) verifies catalog membership, shape equality, period equality, identity binding, governed sources, operation-kind compatibility, comparison scope, and population/equivalence approvals.

[`StructuredQueryCompiler`](../../../src/backend/FinancialCopilot.Application/FinancialData/Semantics/Navigation/StructuredQueryCompilation.cs#L185) has separate direct-source-item and monthly compilation subsets. The monthly compiler supports direct retrieval/time series and approved bounded MoM/YoY comparisons with strict binding, aggregation, period, unit, and product-equivalence checks ([lines 285–405](../../../src/backend/FinancialCopilot.Application/FinancialData/Semantics/Navigation/StructuredQueryCompilation.cs#L285)).

The live PostgreSQL integration proof is downstream evidence, not proof of general NL interpretation. Before compilation, the test requires the extracted and selected measure to equal the case's expected measure; only the matching subset proceeds. It then creates an `approvedFixturePlan` by setting retrieval, review, authorization, execution eligibility, and comparison `FormulaAuthorized=true` ([`MonthlyStructuredQueryPostgreSqlTests` lines 1274–1299](../../../tests/FinancialCopilot.IntegrationTests/MonthlyStructuredQueryPostgreSqlTests.cs#L1274)). This is appropriate for isolated execution testing but must not be interpreted as production authorization evidence.

## 2. Operation and period contract map

| Layer | Measure/value representation | Operation representation | Period representation | Observed issue |
|---|---|---|---|---|
| Requirements LLM | `MeasureIds` plus typed `FinancialValueReferences` | string `RequestedFinancialValueOperation` | string `PeriodPolicies`, `ComparisonScopes`, plus shape fields | one response owns concept selection, binding, and logical shape |
| Structured shape | projection/filter/aggregate/sort catalog IDs | `PlanFinancialOperationKind` | `PlanComparisonMode`, start/end, `MaximumPeriods` | duplicates top-level operation and policy strings |
| Normalizer | may replace reference types and measures indirectly through rules | rewrites operation/kind/mode | infers scopes from question substrings | multiple sources of truth; order-sensitive repairs |
| Navigator | no independent measure choice; binds node context | copied from requirements into result | copied from requirements into result | cannot correct upstream semantic errors |
| Plan | `PlanSourceReference` and projections | `FinancialValueOperation` + `PlanFinancialOperationKind` + `PlanComparisonMode` + `FormulaAuthorized` | `PlanPeriodSelection` policies/scopes/latest/bounds/window | coherent typed target, but reached through lossy duplicated fields |
| Compiler | exactly one mapped source for current subsets | validates combinations again | validates latest versus explicit bounds and minimum comparison windows | rejects combinations accepted by earlier completeness checks |
| Scorer | dataset `metrics` vs `selectedMeasureIds` | dataset `expectedFinancialValueOperation` vs result top-level string | dataset `expectedPeriodPolicies` subset of normalized requirements | does not score shape kind, comparison mode, date bounds, or maximum window |

### Verified contract mismatches

1. `FinancialValueOperation` has only `DirectValueRetrieval`, `ApprovedPeriodComparison`, and `UnsupportedDerivedFinancialMetric`, while `PlanFinancialOperationKind` separately expresses direct, absolute change, growth percentage, time series, and unsupported-derived; `PlanComparisonMode` separately expresses MoM, YoY, and multi-company comparison ([contracts](../../../src/backend/FinancialCopilot.Application/FinancialData/Semantics/Navigation/StructuredQueryPlanning.cs#L133)). A valid semantic operation therefore requires three coordinated values plus scopes and period bounds.
2. Time series is a `PlanFinancialOperationKind` but usually retains top-level `DirectValueRetrieval`. This is legal in the validator only with time-series/chart presentation and a bounded period ([validator lines 456–462](../../../src/backend/FinancialCopilot.Application/FinancialData/Semantics/Navigation/StructuredQueryPlanning.cs#L456)). The scorer sees only `DirectValueRetrieval`, so it cannot evaluate time-series intent.
3. MoM/YoY exist both as `ComparisonScopes` strings and `PlanComparisonMode`; the normalizer maps between them and also detects Persian phrases directly ([normalizer lines 856–886](../../../src/backend/FinancialCopilot.Application/FinancialData/Semantics/Navigation/LlmVectorlessSchemaNavigator.cs#L856)).
4. “Latest” is a string policy that the plan builder converts to `SelectLatestByReportingPeriod=true`; population discovery may instead inject an explicit trailing-twelve-month observation window later in the planner. These are different recency semantics under one pipeline.
5. `FormulaAuthorized` is always false in the plan builder. The compiler requires it for comparisons, and the integration harness sets it true only on the isolated approved fixture plan. The architecture lacks a production approval transition between validated semantic intent and compilation.
6. A governed YoY growth metric and a comparison over a base monthly observation are different source models. The extractor can currently produce a governed growth metric together with `ApprovedPeriodComparison/GrowthPercentage`, as v3-06 did, but the plan validator requires growth comparison to have a `DirectSourceItem` or `MonthlyMeasure` base source.

## 3. Trace-based root-cause matrix

The artifacts store normalized `independentRequirements`, not the raw model JSON. “Requirements extraction” below therefore means the combined LLM extraction plus deterministic normalization/policy validation unless a narrower stage is provable.

| Case and Persian question | Independent expectation | Observed requirements / navigator | First verified divergence | Classification |
|---|---|---|---|---|
| `v3-02`: `کدام شرکت‌های صنعت معدن در تازه‌ترین گزارش، گندله تولید کرده‌اند؟` | production quantity, latest, product-driven company discovery | no measure/reference; marked ambiguous; navigator selected industry/financial nodes | requirements extraction omitted the clearly requested production measure | verified requirements defect; population discovery support was not reached |
| `v3-03`: `... بیش از ۳۰ تن فروش داشته‌اند؟` | language requires sales quantity + `>30` + latest | extractor chose `metric_monthly_sales_quantity`, direct value, latest | dataset expects `MONTHLY_SALES_GROWTH_YOY` before runtime is evaluated | verified annotation defect; runtime measure is semantically consistent |
| `v3-04`: `... مجموع تولید سه ماه اخیر ... مرتب کن.` | production quantity + sum + three-period ranking | extractor chose production quantity, `MaximumPeriods=3`, time-series/ranking shape | dataset expects `MONTHLY_SALES_GROWTH_YOY` | verified annotation defect |
| `v3-05`: `... بیشترین رشد مقدار تولید ماهانه ...` | base production observation + growth/ranking over three periods | extractor chose production quantity, approved comparison, GrowthPercentage, MoM, max 3 | dataset expects `NET_PROFIT` and financial-statement scope | verified annotation defect; runtime interpretation is materially closer to the question |
| `v3-06`: `... نسبت به شهریور سال قبل چند درصد تغییر ...` | YoY percentage change with explicit target/baseline | extractor chose governed `metric_monthly_production_growth_yoy` plus ApprovedPeriodComparison/GrowthPercentage/YoY and 13 periods | requirements combine derived governed metric with base-observation comparison algebra | verified cross-layer contract defect; measure score alone hides it |
| `v3-09`: `وجه نقد پایان سال مالی ۱۴۰۴ کگهر ...` | direct BalanceSheet source item, standalone, explicit period, safe unsupported until authorization | provider RequirementsExtraction completed (4,170/531 tokens), then policy validation rejected `UnsupportedDerivedFinancialMetric`; no normalized requirements persisted | requirements policy validation | verified semantic classification/recovery defect, not malformed JSON |
| `v3-10`: `آیا رشد تولید و مقدار فروش کگهر ... یکسان بوده است؟` | genuine cross-measure comparison requiring product/value-definition clarification | extractor selected production-growth governed metric plus sales quantity, `compare`, latest, and marked ambiguity; navigation exhausted request token budget and returned no selected references | request is genuinely under-specified, then navigation/budget representation loses the useful partial intent | mixed: legitimate ambiguity plus navigation budget symptom |
| `fa-value-02`: `آخرین سود عملیاتی کگل چقدر است؟` | exact DirectSourceItem `source_item_9c617846fc7c5c13`, direct retrieval, latest | extractor selected both governed `metric_operating_profit` and the exact DirectSourceItem; navigator preserved both | requirements over-selection | verified requirements defect; planner cannot choose which source is authoritative |
| `readiness-dev-06`: `موجودی وجه نقد کگهر در پایان سال ۱۴۰۴ ...` | BalanceSheet cash item `source_item_b7bcc0f92e19ac4f` | extractor selected CashFlow item `source_item_e3c400252fd144d3` (source item 63) | catalog-reference selection during requirements stage | verified wrong source binding; raw output/outline are missing, so LLM versus candidate-pruning responsibility is unresolved |
| `fa-value-08`: `حاشیه سود خالص کگهر چقدر است؟` | dataset says unsupported derived metric | extractor selected governed `metric_net_profit_margin` as direct retrieval | expectation and current catalog capability disagree | verified contract/label drift; requires owner decision, not prompt tuning |
| `readiness-dev-18`: `... مقدار فروش گندله ... چند تن ...` | dataset says production quantity | extractor correctly chose sales quantity and preserved unit ambiguity | dataset diverges from question before runtime | verified annotation defect |
| `holdout-v2-03`: `... بیش از ۳۰ تن گندله فروخته‌اند؟` | dataset says YoY sales growth | extractor chose sales quantity with `>30` filter and latest | dataset diverges from question before runtime | verified annotation defect |

### All non-completed attempts

Across the original 50 plus v3, the exact terminal distribution is:

- 37 completed with safe `UnsupportedOperation`;
- 8 completed with clarification/semantic validation;
- 14 failed during requirements extraction with error category `invalid_structured_output`;
- 1 failed during requirements extraction due hard timeout (`readiness-dev-19`).

All 14 `invalid_structured_output` records carry the same diagnostic: `requirements_contain_unsupported_financial_value_operation`. The IDs are `fa-07`, `fa-09`, `fa-10`, `fa-value-04`, `fa-value-07`, `readiness-dev-02`, `-08`, `-09`, `-10`, `-11`, `readiness-holdout-v2-08`, `-09`, `-10`, and `readiness-holdout-v3-09`.

This classification is misleading. For v3-09, provider diagnostics explicitly show `RequirementsExtraction: Completed`; rejection occurred afterward in `ValidateRequirementPolicies`. No evidence shows malformed JSON. The same exception type is used for both schema/output defects and semantic-policy rejection, and the runner maps both to `invalid_structured_output`.

The prior reliability report says there were 13 invalid structured-output cases. The raw artifacts show 14. Its 72% original-50 completion and 90% v3 completion figures remain correct.

## 4. Holdout-v3 0% operation/period investigation

### Exact denominators

The scorer only appends an operation score when the dataset contains `expectedFinancialValueOperation`, and only appends a period score when it contains `expectedPeriodPolicies` ([`run_navigation_evaluation.py` lines 274–288](run_navigation_evaluation.py#L274)). In v3:

- operation applicable cases: **1** (`readiness-holdout-v3-09`);
- period applicable cases: **1** (`readiness-holdout-v3-09`);
- metric applicable cases: **9** (all except v3-09).

V3-09 failed before a prediction operation or period was available. The scorer nevertheless evaluates the missing fields as incorrect. Therefore both reported aggregates are `0/1 = 0%`.

### Interpretation

The 0% figures reflect a real failure on one important direct-source-item case, but they do **not** support a claim that operation or period interpretation failed across the holdout. They are primarily a coverage/denominator defect in the evaluation design and a reporting defect where the denominator was omitted.

The scorer also has these limitations:

- it evaluates failed predictions as ordinary zero-valued semantic predictions instead of separately reporting conditional accuracy on completed extraction;
- it scores only the top-level three-value operation, not `FinancialOperationKind`, `ComparisonMode`, comparison scope, bounds, or `MaximumPeriods`;
- period scoring checks only that expected policy strings are a subset; it ignores actual date conversion and comparison window correctness;
- `failureCodes` is computed from scorer rows that never copy prediction `failureCode`, producing an uninformative `None` bucket;
- it calls `selectedMeasureIds` navigator output even though those IDs are derived from independent requirements and expanded context;
- it cannot distinguish raw LLM mistakes from deterministic normalization because raw requirements JSON is not persisted.

### V3 label integrity

At least four v3 labels are inconsistent with their questions:

- v3-03: sales quantity threshold labeled YoY sales growth;
- v3-04: summed production ranking labeled YoY sales growth;
- v3-05: monthly production growth labeled net profit/financial statements;
- v3-10: comparison of production growth and sales quantity labeled only YoY sales growth.

These labels existed before inference, which protects against prediction leakage but does not guarantee annotation correctness. They should remain immutable as historical evidence; the next phase should create a separately versioned, independently reviewed benchmark rather than silently editing v3.

## 5. Requirements extractor assessment

| Capability | Evidence-based assessment |
|---|---|
| Production quantity vs sales quantity vs sales amount | Often correct: v3-03, v3-04, v3-07, v3-08 and PostgreSQL cases. Some golden labels incorrectly penalize correct distinctions. |
| Production quantity vs production growth | Mixed. The extractor can represent growth as base measure + operation (v3-05), but also selects a governed growth metric plus comparison operation (v3-06). |
| Measure versus analytical operation | Not cleanly separated. Both are chosen in one LLM contract and repaired by `NormalizeRequirements`. |
| Explicit/implicit periods | Date conversion works in several cases; representation is fragmented across policies, scopes, bounds, mode, and maximum periods. |
| Multiple measures | Contract supports lists, but cross-measure operations are not coherently represented; v3-10 ends ambiguous and v2-10 is rejected. |
| No named company | Contract supports product references and planner-side population discovery; v3-02 shows extraction can fail before that capability activates. |
| Company-population discovery | Downstream architecture supports it and the isolated discovery trace succeeds with partial-coverage disclosure. |
| Genuine ambiguity | Preserved in v3-10 and several sales-growth questions, which is safety-positive. V3-02 is over-conservative because the requested measure is clear. |
| Stable typed output | JSON schema is strict, but the semantic payload is too wide. Fourteen completed provider outputs are reported as invalid because a later policy gate rejects their operation. |

Extracting requirements “independently” of catalog navigation is conceptually sound only for semantic intent. Requiring exact catalog IDs in that same independent step is internally contradictory: the extractor is already performing catalog navigation. It then passes those IDs to the navigator as required references, preventing the navigator from serving as an independent binding/checking stage.

## 6. Vectorless navigator assessment

### Strengths

- deterministic catalog identity/hash validation;
- no embeddings or uncontrolled external retrieval;
- strict node-ID schema prevents hallucinated node IDs;
- required-reference expansion and bounded fan-out;
- direct-source identity and governance metadata preserved;
- explicit separation of navigation from query authorization;
- safe handling of reviewed product equivalence and population scope.

### Weaknesses

1. It is called a schema navigator but its measured answer references come from extraction. It selects context anchors, not measures.
2. Required IDs from extraction are force-included, so a wrong extractor reference determines the branches the navigator sees and makes correction unlikely.
3. First-pass lexical pruning can omit semantically relevant statement source items. The cash-balance error is consistent with this risk, but the exact outline sent for that historical request was not persisted, so causation is unverified.
4. The outline and expanded context expose many incidental monthly measures. Artifacts correctly distinguish candidates from selected references, but earlier reports can mistake candidate breadth for measure over-selection.
5. A navigation LLM call is used even when required IDs deterministically identify the containing nodes. This adds cost and failure surface without necessarily adding semantic independence.
6. Retry is only for provider-reported truncation. Semantic-policy rejection after a valid response is not repaired with a typed diagnostic.

The navigator is not doing too many semantic responsibilities in its current prompt; the extractor is. The navigator does, however, carry expansion, context assembly, budget management, completeness invocation, and result shaping. Catalog reference binding should be its explicit responsibility, while analytical operation and period semantics should be removed from it entirely.

## 7. Verified defects versus hypotheses

| Type | Finding | Status/evidence |
|---|---|---|
| Defect | Navigator measure score is attributed to the wrong stage | Verified by `SelectedMeasureIds = requirements ∩ context` |
| Defect | Fourteen semantic-policy rejections are labeled invalid structured output | Verified by artifacts and exception mapping |
| Defect | V3 operation and period aggregates each use denominator one | Verified from dataset and scorer implementation |
| Defect | Multiple evaluation labels contradict the Persian question | Verified directly from immutable datasets |
| Defect | Operation/period intent has multiple coupled representations | Verified in contracts and normalizer |
| Defect | Governed growth metric can be combined with a base-observation comparison operation | Verified in v3-06; later validator would reject it |
| Defect | PostgreSQL matrix comparison authorization is test-injected | Verified at integration-test lines 1295–1298 |
| Defect | Raw requirements output is not persisted | Verified in runner serialization; only normalized requirements and provider metadata are stored |
| Hypothesis | Cash source-item error was caused by outline candidate pruning | Plausible, but historical outline/raw response absent |
| Hypothesis | The hard timeout was provider latency versus local post-processing | The outer deadline is known; stage-level timing is insufficient to isolate more precisely |
| Hypothesis | A smaller structured schema alone will fix semantic accuracy | Unproven; it should reduce output failures/cost, not necessarily improve meaning |

## 8. Architecture alternatives

| Criterion | Option A: repair current design | Option B: canonical semantic intent before navigation | Option C: simplify/collapse orchestration |
|---|---|---|---|
| Expected correctness | medium improvement; duplicated ownership remains | highest: one semantic owner, explicit binding boundary | medium-high if carefully designed, but easy to lose independent checks |
| Complexity | low–medium | medium | medium–high |
| LLM calls | usually unchanged (requirements + navigation) | one intent call plus optional reference-binding call; deterministic navigation where IDs bind directly | potentially one call |
| Token/latency | small improvement | material reduction if intent schema is compact and deterministic node lookup replaces routine navigation calls | largest potential reduction |
| Existing-code compatibility | highest | high via adapters into `QuestionSchemaRequirements` and existing plan | lower; broad orchestration changes |
| Regression risk | low initially, high long-term drift | moderate and containable with shadow comparison | highest |
| Unseen questions | limited by prompt/normalizer patches | strongest: typed semantics plus generic catalog binding | depends heavily on a single prompt/contract |
| Population discovery | preserved, but planner special cases remain | first-class entity scope and product concept; preserves dynamic discovery | risk of collapsing reviewed scope into prompt logic |
| Governance safety | unchanged | unchanged or stronger; authorization remains after binding | at risk if independent gates are removed |

### Recommendation

Choose **Option B**, with one constraint borrowed from Option C: each semantic decision must have exactly one owner.

This is not a full rewrite. Introduce a canonical semantic-intent record and an adapter to the existing requirements/plan contracts. Keep the hierarchy, catalog integrity, completeness gate, identity resolvers, reviewed equivalence, compiler, restricted executor, and Persian evidence renderer.

## 9. Recommended target architecture

```text
Question
  -> SemanticIntentExtractor (one compact LLM contract)
       RequestedValueIntent
         known monthly quantity/amount OR verbatim reported statement line
       AnalyticalOperation
       ComparisonMode
       PeriodSpec
       EntityScope / ProductScope / PopulationScope
       Aggregation / Ranking / Presentation
       Ambiguities with typed reason codes
  -> CatalogReferenceBinder (vectorless)
       lexical candidate construction
       exact catalog/source-item binding
       zero/one/many typed result
       no operation or period reinterpretation
  -> DeterministicIntentValidator
       compatibility matrix: value type × operation × period × scope
  -> Existing identity/population/equivalence binders
  -> StructuredQueryPlanBuilder adapter
  -> Existing deterministic plan validator
  -> explicit authorization transition
  -> existing compiler / restricted executor / renderer
```

For arbitrary statement lines, `RequestedValueIntent` must preserve the user's verbatim concept and domain rather than require a closed enum. Known monthly concepts can be typed (`ProductionQuantity`, `SalesQuantity`, `SalesAmount`). Catalog binding then resolves an exact governed metric or DirectSourceItem. This preserves generic unseen-question support without creating per-question handlers.

The intent contract should have one canonical operation algebra, for example:

- `DirectValue`
- `TimeSeries`
- `AbsoluteChange`
- `GrowthPercentage`
- `Aggregate`
- `Rank`
- `CompareValues`

with orthogonal `ComparisonMode`, `PeriodSpec`, and `EntityScope`. The existing three-value `FinancialValueOperation` can remain as a downstream authorization/compiler category derived deterministically from that intent.

## 10. Minimal prioritized implementation slices

No slice is implemented by this review.

1. **Evaluation integrity first — small complexity, high decision impact.** Add explicit applicable denominators, separate extraction failures from conditional semantic accuracy, preserve the current datasets unchanged, and create a new independently double-reviewed holdout. Add dataset lint rules that flag obvious measure/question contradictions without auto-changing labels.
2. **Evidence contract — small complexity, high diagnostic impact.** Persist redacted raw provider structured JSON, post-parse/pre-normalization requirements, post-normalization requirements, exact compact outline ID/hash, and the first deterministic rejection code. Separate `MalformedStructuredOutput` from `SemanticContractRejected`.
3. **Canonical intent record and compatibility validator — medium complexity, high correctness impact.** Introduce the new typed IR and deterministic mapping to existing `QuestionSchemaRequirements`; initially run it in shadow mode against frozen evidence.
4. **Reference binding boundary — medium complexity, high correctness impact.** Move exact catalog-ID/source-item selection out of semantic extraction. Make the navigator/binder return typed zero/one/many bindings and supporting candidate evidence. Do not let a proposed binding change operation or period intent.
5. **Remove duplicated normalizations — medium complexity, medium-high impact.** Replace question-string repair rules with deterministic transformations from canonical intent. Retire planner-side semantic rewrites only after parity tests cover population discovery and ranking.
6. **Explicit authorization transition — medium complexity, safety-critical.** Add a production-owned step that can set comparison formula authorization only after semantic, catalog, unit, period, and governance checks. Do not copy the integration test's blanket fixture approval behavior.
7. **Call/cost optimization — small-to-medium complexity after correctness.** Skip the navigation LLM when deterministic required-reference binding yields a complete unambiguous context; keep a bounded binding call for ambiguous statement-line candidates.

## 11. Risks and regression safeguards

| Risk | Safeguard |
|---|---|
| Intent IR becomes a fixed list that blocks arbitrary statement questions | support verbatim reported-line intent plus catalog binding; keep enums only for analytical algebra and known monthly quantity bases |
| Moving catalog IDs out of extraction weakens governance | require binder outputs to carry catalog hash, exact reference identity, candidate evidence, and zero/one/many status |
| Population discovery regresses to fixed companies | retain reviewed product concept, authorized tenant discovery scope, observation-based company discovery, and partial-coverage disclosure |
| Existing compiler behavior changes | adapter must emit byte-for-byte equivalent plans for currently passing approved fixtures before replacing the old path |
| Safer ambiguity behavior is mistaken for lower accuracy | score correct clarification separately from answer accuracy and define ambiguity gold labels explicitly |
| Prompt reduction loses Persian distinctions | retain contrast tests for `مبلغ فروش`, `مقدار فروش`, `مقدار تولید`, MoM, YoY, and statement-line requests |
| Product equivalence broadens without review | retain exact approved alias/equivalence records and unit compatibility checks |
| A one-call design weakens independent validation | keep deterministic intent compatibility, completeness, plan validation, compiler validation, and restricted authorization as separate gates |

## 12. Proposed acceptance criteria for the next phase

### Evaluation and evidence

- Every metric reports numerator and applicable denominator.
- Failed extraction is reported separately from conditional semantic accuracy.
- A new untouched benchmark has at least 30 questions, with at least 10 operation-applicable and 10 period-applicable cases.
- Two independent reviewers approve each expected measure, operation, comparison mode, and period specification before inference.
- Raw/redacted pre-normalization output and post-normalization output are persisted for every attempt.
- Terminal record reliability remains 100%; semantic-contract rejection is never labeled malformed JSON.

### Semantic interpretation

- At least 90% exact value-intent/measure accuracy on supported new-holdout cases.
- At least 85% exact analytical-operation and period-spec accuracy, each on denominators of at least 10.
- 100% distinction on a targeted contrast suite for sales amount, sales quantity, production quantity, MoM, YoY, direct value, and time series.
- Multiple measures are either represented by a supported typed cross-value operation or returned as a typed clarification; never silently collapsed.

### Binding and governance

- Exact DirectSourceItem selection includes source-item catalog ID, provider, statement type, source item ID, unit status, and candidate evidence.
- No unknown/hallucinated catalog ID reaches planning.
- Product-driven population discovery continues to find newly introduced fixture companies without a fixed registry.
- The reviewed `گندله = گندله سنگ‌آهن` equivalence and unit checks remain mandatory for cross-company comparisons.
- Authorization, semantic approval, and retrieval completeness remain independent gates.

### End to end

- Every supported case in the existing PostgreSQL matrix either returns the exact expected numeric values/Persian answer or a predeclared typed safe outcome.
- No successful SQL execution returns a wrong numeric answer.
- The generic financial-statement DirectSourceItem path continues to return the exact `۳٬۷۵۳` fixture value.
- Comparison compilation requires an explicit production authorization decision; tests may not be the only place where `FormulaAuthorized` can transition to true.
- LLM call count and token usage do not exceed the current two-call design for the common case; deterministic unambiguous bindings should use one interpretation call.

## 13. Evidence references

- [Reliability report](feature141-live-evaluation-reliability-postfix-2026-10-10.md)
- [Frozen development predictions](../artifacts/feature141-original50-dev-postfix-2026-10-09.jsonl)
- [Frozen development score](../artifacts/feature141-original50-dev-postfix-score-2026-10-09.json)
- [Frozen holdout-v2 predictions](../artifacts/feature141-original50-holdout-postfix-2026-10-09.jsonl)
- [Frozen holdout-v2 score](../artifacts/feature141-original50-holdout-postfix-score-2026-10-09.json)
- [Untouched holdout-v3 dataset](release-readiness-holdout-v3.json)
- [Untouched holdout-v3 predictions](../artifacts/feature141-holdout-v3-first-pass-2026-10-09.jsonl)
- [Untouched holdout-v3 score](../artifacts/feature141-holdout-v3-first-pass-score-2026-10-09.json)
- [Fresh PostgreSQL matrix trace](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-readiness-v2-2026-10-09-postfix-2026-10-09.jsonl)
- [Dynamic population discovery trace](../artifacts/monthly-company-population-discovery-live-llm-postgresql-readiness-v2-postfix-2026-10-09.json)
- [Generic financial-statement trace](../artifacts/financial-statement-generic-pipeline-2026-10-09-postfix-2026-10-09.json)

## 14. Remaining unknowns

- The raw requirements JSON and exact compact outline for historical failures were not persisted, so model generation cannot always be separated from deterministic normalization or candidate pruning.
- Provider completion reasons are absent for successful calls; only token usage, latency, and response ID are available.
- The cash source-item error cannot be attributed conclusively to lexical candidate construction versus LLM selection without the exact outline and raw response.
- Production wiring for a legitimate comparison formula-authorization transition is not present in the reviewed path; integration evidence uses a fixture-only override.
- Current governance metadata makes every completed evaluation non-executable, so production semantic accuracy after approval remains unmeasured.
