# Option B Navigator Contract Resolution — 2026-10-10

## Decision: NO_GO — governance metadata blocks safe binding

The contract incompatibility is confirmed. I stopped before changing Navigator or Planner: the present catalog provides candidate labels/descriptions and unit labels, but does not provide an approved, machine-readable mapping from canonical business concepts and qualifier combinations to query-authorized catalog references. Guessing from aliases or unit labels would be an ungoverned semantic mapping and could select a merely related measure. This meets the task's explicit stop condition.

## Contract compatibility map

| Boundary | Current contract | Consequence |
|---|---|---|
| Canonical intent | `CanonicalSemanticIntent` has schema-independent concept IDs, per-measure dimension/qualifiers, requested and effective source units, entity/product scope, period, comparison, filters, aggregation, and ranking. | Appropriate semantic input, but no physical catalog IDs by design. |
| Navigator request | `SchemaNavigationRequest` carries question and catalog identity only. | Cannot receive canonical intent as a typed input. |
| Requirement extraction | `QuestionSchemaRequirements` uses `MeasureIds`, `DimensionIds`, and `RelationshipIds` as catalog references; financial values and structured-shape fields also carry catalog IDs. | Putting concept IDs here would mislabel them as catalog IDs; leaving them empty would omit required semantics. |
| Candidate discovery | `JsonCatalogNavigationHierarchyProvider` ranks hierarchy nodes from text and exposes metric descriptions, aliases, and unit code/label. The LLM extractor is instructed to choose catalog IDs from that outline. | Useful discovery hints, not an approved canonical-concept binding contract. Unit labels/codes alone do not encode all intent dimensions, qualifier compatibility, or source-unit uncertainty. |
| Completeness/governance | `SchemaContextCompletenessValidator` verifies exact reference presence and gates on node governance, semantic approval, query authorization, and financial-reference approval. | It does not prove that a discovered catalog reference means the requested canonical concept with all its qualifiers. |
| Planner | `StructuredQueryPlanBuilder` builds `PlanSourceReference` objects from `requirements.FinancialValueReferences`; projection/filters/aggregation/sort are catalog-ID based. | Planner requires bound references. It has no canonical-intent binding input or general semantic-equivalence check. |

Primary source locations: `SchemaNavigationContracts.cs` (request, requirements, context and validator); `LlmVectorlessSchemaNavigator.cs` (ID-constrained extraction); `JsonCatalogNavigationHierarchyProvider.cs` (outline metadata); `StructuredQueryPlanning.cs` (bound-reference planning); `SemanticCatalog/metrics/metric_eps.yaml` and `metric_monthly_sales.yaml` (example metric metadata).

The examples distinguish some meanings in descriptions and units (EPS uses `amount-per-share`; monthly sales describes monetary SalesAmount in million IRR), but both metric files currently say `genericQuery.approved=false`, `executable=false`, and `reviewRequired=true`. More importantly, the catalog metadata has no canonical concept key/dimension/qualifier binding which an independent validator could enforce. Similarity scoring is not a substitute for that governance.

## Changes and verification

- No code or catalog metadata changed. No interface change was safe to implement without a governed binding source.
- No focused contract tests or five-case Canonical Intent → Navigator → Planner slice were run: a deterministic expected binding cannot be established from currently approved metadata. They are not counted as passes.
- No PostgreSQL execution was attempted. The earlier controlled-integration report records Docker/Testcontainers unavailable; this task did not retry the database fixture.
- No full benchmark or holdout was run.
- Existing build/unit-test results from the previous milestone are not evidence of this contract integration; no new test or build result is claimed here.

## Safe continuation required

Before implementation can resume, catalog governance must approve machine-readable canonical concept bindings, compatible measure dimensions/value forms, relevant statement/reporting qualifiers, and source-unit evidence/uncertainty rules for the intended references. Then Navigator can accept a typed canonical intent and return typed bound/incomplete outcomes, while the existing Planner continues consuming only approved catalog IDs. Population/product scope and population discovery must remain explicit through that contract. A five-case slice and isolated PostgreSQL test should follow only after deterministic contract tests pass and the database fixture is available.

**Recommendation: NO_GO for broader shadow integration.** No semantic improvement or end-to-end execution evidence is established.
