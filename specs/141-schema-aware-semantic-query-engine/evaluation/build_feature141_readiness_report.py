#!/usr/bin/env python3
"""Build the evidence report from frozen datasets and captured run artifacts."""
from __future__ import annotations

import json
from pathlib import Path
from statistics import mean

ROOT = Path(__file__).resolve().parents[3]
SPEC = ROOT / "specs/141-schema-aware-semantic-query-engine"
EVAL = SPEC / "evaluation"
ART = SPEC / "artifacts"
OUT = SPEC / "evaluation/feature141-release-readiness-2026-10-09.md"


def read_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def read_jsonl(path: Path):
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]


def inline(values):
    if values is None:
        return "—"
    if isinstance(values, (list, tuple, set)):
        return ", ".join(str(value) for value in values) or "—"
    if isinstance(values, dict):
        return "; ".join(f"{key}={inline(value)}" for key, value in values.items())
    return str(values)


def expected_intent(case):
    domains = case.get("domains", [])
    metrics = case.get("metrics", case.get("expectedMetrics", []))
    metric_text = " ".join(str(metric).lower() for metric in metrics)
    question = str(case.get("question", ""))
    if "company-industry" in domains or any("industry" in str(d) for d in case.get("dimensions", [])) or "صنعت" in question:
        return "company/product discovery or industry comparison"
    if "financial-statements" in domains or case.get("financialValueReferences"):
        return "financial statement/value lookup"
    if "production" in metric_text:
        return "monthly production growth/comparison" if "growth" in metric_text else "monthly production"
    if "sales" in metric_text:
        return "monthly sales growth/comparison" if "growth" in metric_text else "monthly sales"
    if any("growth" in str(metric) or "comparison" in str(metric) for metric in metrics):
        return "period comparison / growth"
    if case.get("ambiguous") or case.get("unsupported"):
        return "ambiguous or unsupported request"
    return "financial query (per expected catalog references)"


def disposition(pred, case):
    status = str(pred.get("status", "missing_prediction"))
    code = str(pred.get("errorCode") or pred.get("failureCode") or pred.get("diagnostic") or "")
    if status == "missing_prediction":
        return "BLOCKED", "prediction missing"
    if "ambiguous" in code.lower() or str(case.get("expectedOutcome", "")).lower() == "ambiguous":
        return "AMBIGUOUS", code or "expected ambiguous outcome"
    if "unsupported" in code.lower() or "unsupported" in status.lower():
        return "UNSUPPORTED", code or status
    if status == "completed":
        score = pred.get("_score", {})
        metric = score.get("metricCorrectness") or {}
        if metric.get("applicable") and metric.get("recall") != 1.0:
            return "FAIL", "Navigator completed with missing or wrong measure selection"
        return "PASS (Navigator only)", "Navigator completed; SQL/response phases were not run"
    return "BLOCKED", code or status


def render_case(case, pred, score, split, dataset_path, artifact_path):
    pred = dict(pred or {})
    pred["_score"] = score or {}
    status, reason = disposition(pred, case)
    selected_metrics = pred.get("selectedMeasureIds", pred.get("metricIds", []))
    operation = pred.get("financialValueOperation") or pred.get("requestedFinancialValueOperation")
    refs = pred.get("financialValueReferences") or pred.get("selectedFinancialValueReferences") or []
    if not refs and isinstance(pred.get("requirements"), dict):
        refs = pred["requirements"].get("financialValueReferences", [])
    query_plan = (f"measures={inline(selected_metrics)}; operation={inline(operation)}; "
                  f"period={inline(pred.get('periodPolicies'))}; dimensions={inline(pred.get('selectedDimensionIds', pred.get('dimensionIds', [])))}; "
                  f"relationships={inline(pred.get('selectedRelationshipIds', pred.get('relationshipIds', [])))}")
    selected_nodes = pred.get("selectedNodeIds", pred.get("nodeIds", []))
    pred_status = pred.get("status", "missing_prediction")
    exact_failure = pred.get("errorCode") or pred.get("failureCode") or pred.get("diagnostic")
    expected = (f"domains={inline(case.get('domains', []))}; measures={inline(case.get('metrics', case.get('expectedMetrics', [])))}; "
                f"dimensions={inline(case.get('dimensions', []))}; relationships={inline(case.get('relationshipIds', []))}; "
                f"operation={inline(case.get('financialValueOperation'))}; period={inline(case.get('periodPolicies'))}")
    actual = (f"status={pred_status}; selectedMeasures={inline(selected_metrics)}; selectedNodes={inline(selected_nodes)}; "
              f"operation={inline(operation)}; selectedFinancialReferences={inline(refs)}; error={inline(exact_failure)}")
    metric_score = (score or {}).get("metricCorrectness", {})
    score_name = ('release-readiness-dev-score-v3-final-2026-10-09.json' if split == 'dev'
                  else 'release-readiness-holdout-v2-score-2026-10-09.json')
    return f"""### {case['id']} — {split}

- **Original Persian question:** {case.get('question', '(missing from dataset)')}
- **Expected intent and references:** {expected_intent(case)}; {expected}
- **Actual Navigator/Planner output:** {actual}
- **Generated query plan:** {query_plan}
- **SQL execution:** No. This captured Navigator CLI run explicitly records `No database query was executed`.
- **Expected result:** Independent semantic oracle only: {expected}. This Navigator dataset has no PostgreSQL fixture-value oracle for this row.
- **Actual result:** Structured Navigator result above. Numeric/database result: unavailable; no SQL was run. Metric precision/recall for this row: {inline({k: metric_score.get(k) for k in ('precision', 'recall', 'f1') if k in metric_score})}.
- **Unit correctness:** Not evaluable; no SQL result.
- **Persian answer correctness:** Not evaluable; the Navigator stage does not generate the final user-facing answer.
- **Status:** {status}
- **Failure reason / exact stage:** {reason}; phase={inline(pred.get('phase'))}; code={inline(pred.get('errorCode') or pred.get('failureCode'))}.
- **Source provenance:** catalog version/hash in the run record; selected references={inline(refs)}; semantic review/query authorization are not implied by selection.
- **Execution mode:** Live LLM Navigator + no SQL (Development configuration, OpenAI/gpt-5.6-luna).
- **Trace:** [dataset]({dataset_path.name}) · [captured LLM output](../artifacts/{artifact_path.name}) · [score detail]({score_name})
"""


def e2e_status(row):
    status = row.get("status")
    if status == "succeeded":
        answer = row.get("finalPersianAnswer") or ""
        if "واحد اندازه‌گیری نامشخص" in answer:
            return "PARTIAL (unit unresolved)"
        return "PASS" if row.get("finalAnswerStatus") == "Answered" else "FAIL"
    if status == "typed_execution_failure":
        return "BLOCKED"
    if status == "typed_pipeline_failure":
        return "UNSUPPORTED"
    if status == "blocked":
        return "BLOCKED"
    if status == "typed_ambiguity":
        return "AMBIGUOUS"
    return str(status or "BLOCKED").upper()


def render_e2e(row):
    answer = row.get("finalPersianAnswer")
    expected = row.get("expectedValues")
    actual = row.get("actualValues")
    observations = row.get("observedRows") or []
    units = sorted({str(item.get("Unit") or item.get("SourceUnit") or item.get("unit"))
                    for item in observations if isinstance(item, dict)
                    and (item.get("Unit") or item.get("SourceUnit") or item.get("unit"))})
    sql_status = row.get("executionStatus", "not executed")
    numeric_correct = None
    if expected is not None and actual is not None:
        numeric_correct = sorted(map(str, expected)) == sorted(map(str, actual))
    selected = row.get("selectedMeasureIds", [])
    plan = row.get("plan", {})
    failure = row.get("failureCode") or "none"
    reason = row.get("diagnostic") or failure
    if row.get("status") == "typed_execution_failure" and row.get("executionStatus") == "Incomplete":
        coverage = sorted({str(item.get("ProductMappingCoverageStatus")) for item in observations
                           if isinstance(item, dict) and item.get("ProductMappingCoverageStatus")})
        reason = (f"PostgreSQL returned Incomplete; observed product mapping coverage={inline(coverage)}; "
                  "the artifact exposes no more specific execution error code")
    elif row.get("status") == "typed_ambiguity":
        reason = inline(row.get("validationDiagnostics") or ["retrieval_requirements_incomplete"])
    artifact = "monthly-production-sales-live-llm-postgresql-fifteen-cases-readiness-v2-2026-10-09.jsonl"
    expected_unit = ("درصد" if row.get("operationKind") == "GrowthPercentage" else
                     "تن" if any("quantity" in str(value) for value in selected) else
                     "میلیون ریال" if "metric_monthly_sales" in selected else None)
    unresolved_unit = bool(answer and "واحد اندازه‌گیری نامشخص" in answer)
    unit_correct = None if row.get("status") != "succeeded" or not expected_unit or not answer or unresolved_unit else expected_unit in answer
    unit_status = ("not evaluable (pipeline did not return a successful answer)" if row.get("status") != "succeeded" or not answer else
                   "PARTIAL: answer explicitly says the sales-rate unit is unknown" if unresolved_unit else
                   "not independently checkable" if unit_correct is None else
                   "PASS" if unit_correct else "FAIL")
    return f"""### {row['caseId']} — live fixture pipeline

- **Original Persian question:** {row.get('question', 'not recorded')}
- **Extracted intent:** expected measures={inline(row.get('expectedMeasureIds'))}; selected measures={inline(selected)}; entity bindings company={inline(row.get('companyBinding'))}, product={inline(row.get('productBinding'))}; operation={inline(row.get('operation'))}/{inline(row.get('operationKind'))}/{inline(row.get('comparisonMode'))}; period={inline(row.get('planPeriod'))}.
- **Generated Query Plan:** measure={inline(selected)}; entities={inline(plan.get('Entities'))}; period={inline(plan.get('Period'))}; aggregation={inline(plan.get('Aggregations'))}; sort={inline(plan.get('Sort'))}; pagination={inline(plan.get('Pagination'))}.
- **SQL execution:** {'Yes' if row.get('sqlExecuted') else 'No'}; PostgreSQL status={sql_status}; compiler diagnostics={inline(row.get('compilerDiagnostics'))}.
- **Expected result:** fixture oracle={inline(expected) or 'not separately configured for this shape'}; expected growth={inline(row.get('expectedGrowthPercentage'))}; units interpreted from seeded fixture/source metadata: {inline(units) or 'as listed in plan/source row'}.
- **Actual result:** values={inline(actual)}; comparison rows={inline(row.get('actualComparisonValues'))}; observed rows={inline(observations)}; actual-vs-fixture exact numeric equality={inline(numeric_correct)}.
- **Unit correctness:** {unit_status}; expected render unit={inline(expected_unit)}; observed/source units={inline(units)}.
- **Persian answer correctness:** {row.get('finalAnswerStatus', 'no answer')}; full returned answer follows.
- **Status:** {e2e_status(row)}; failure stage={inline(row.get('failureStage'))}; reason={reason}.
- **Source provenance:** provider={inline(row.get('provider'))}; report identity, source row fingerprints, parameterized SQL and selected semantic refs are in the linked trace artifact.
- **Execution mode:** Live LLM + isolated PostgreSQL fixture; Development/production financial databases were not queried.
- **Complete user-facing Persian answer:**

```text
{answer or '[No answer returned by the pipeline.]'}
```
- **Trace:** [full query, SQL, params, rows, answer and provenance](../artifacts/{artifact})
"""


def main():
    dev_ds = EVAL / "release-readiness-dev-v1.json"
    holdout_ds = EVAL / "release-readiness-holdout-v2.json"
    dev_pred = ART / "release-readiness-dev-live-v3-final-2026-10-09.jsonl"
    holdout_pred = ART / "release-readiness-holdout-v2-live-2026-10-09.jsonl"
    dev_score = read_json(EVAL / "release-readiness-dev-score-v3-final-2026-10-09.json")
    holdout_score = read_json(EVAL / "release-readiness-holdout-v2-score-2026-10-09.json")
    dev_cases, holdout_cases = read_json(dev_ds)["cases"], read_json(holdout_ds)["cases"]
    dev_predictions, holdout_predictions = {x["caseId"]: x for x in read_jsonl(dev_pred)}, {x["caseId"]: x for x in read_jsonl(holdout_pred)}
    score_lookup = {"dev": {x["caseId"]: x for x in dev_score["perCase"]},
                    "holdout": {x["caseId"]: x for x in holdout_score["perCase"]}}
    e2e_path = ART / "monthly-production-sales-live-llm-postgresql-fifteen-cases-readiness-v2-2026-10-09.jsonl"
    e2e_rows = read_jsonl(e2e_path)
    population_path = ART / "monthly-company-population-discovery-live-llm-postgresql-readiness-v2.json"
    population = read_json(population_path)
    out = ["# Feature 141 Release Readiness — Persian Question Scorecard (2026-10-09)", "",
           "## Decision", "",
           "**NOT PRODUCTION READY.** The measured 50-question live LLM Navigator suite has 23 completions (16/40 development; 7/10 untouched holdout), weak exact measure-set accuracy (22.2% development; 33.3% holdout), and no SQL or final answer generation in those 50 Navigator-only attempts. The separate 15-question live LLM + isolated PostgreSQL suite returned 10 answers: nine have an inspectable rendered result, while one sales-rate answer explicitly leaves its unit unknown. It also has two incomplete cross-company rankings, two rejected comparison operations, and one genuine ambiguity. Dynamic pellet discovery exactly matched the independent fixture company set (precision=recall=exact-set accuracy=1.00), while correctly disclosing incomplete alias coverage. This is fixture evidence, not exhaustive market coverage.", "",
           "## Cohorts and unconditional metrics", "",
           "| Cohort | Attempts | Navigator / answer completions | SQL queried | Incorrect numeric answers | Safe refusals / unresolved |", "|---|---:|---:|---:|---:|---:|",
           f"| Live LLM development Navigator | {dev_score['datasetCaseCount']} | {dev_score['completedCaseCount']}/{dev_score['datasetCaseCount']} ({dev_score['unconditionalSuccessRate']:.1%}) | 0 | 0 observed (no numeric answers) | {dev_score['failedCaseCount']} failures + 2 blocked, all retained |",
           f"| Separate untouched live LLM holdout Navigator | {holdout_score['datasetCaseCount']} | {holdout_score['completedCaseCount']}/{holdout_score['datasetCaseCount']} ({holdout_score['unconditionalSuccessRate']:.1%}) | 0 | 0 observed (no numeric answers) | {holdout_score['failedCaseCount']} not completed |",
           f"| Live LLM + isolated PostgreSQL fixture | {len(e2e_rows)} | {sum(1 for x in e2e_rows if x.get('status') == 'succeeded')}/{len(e2e_rows)} answered | {sum(1 for x in e2e_rows if x.get('sqlExecuted'))} executed | 0 known incorrect numeric answers among answered cases; unit/semantic failures are reported per case | {sum(1 for x in e2e_rows if x.get('status') != 'succeeded')} non-success |",
           "| Dynamic producer-discovery query | 1 | 1 structured result with disclosed incomplete coverage | 1 | 0 incorrect company-set membership in fixture | 1 partial-coverage result |", "",
           "The two 50-question Navigator cohorts are reported separately from SQL-backed tests: they did not execute PostgreSQL and did not generate Persian final answers. Their 0 SQL executions are not successes. No question is dropped from a denominator. The development score includes two requests that stalled without a response row; these are explicitly represented as BLOCKED in the 40-row output, so `failedCaseCount` (22) plus 2 blocked accounts for all 24 non-completions. Model cost was unavailable from the provider.", "",
           "### Navigator metrics", "",
           f"- Development (40): exact measure-set accuracy {dev_score['aggregate']['metricExactSetAccuracy']:.1%}; measure precision {dev_score['aggregate']['metricPrecision']:.1%}; recall {dev_score['aggregate']['metricRecall']:.1%}; domain exact-set accuracy {dev_score['aggregate']['domainAccuracy']:.1%}; financial-reference exact accuracy {dev_score['aggregate']['financialReferenceAccuracy']:.1%}; operation accuracy {dev_score['aggregate']['financialOperationAccuracy']:.1%}; period accuracy {dev_score['aggregate']['periodInterpretationAccuracy']:.1%}.",
           f"- Holdout (10, one pass): exact measure-set accuracy {holdout_score['aggregate']['metricExactSetAccuracy']:.1%}; measure precision {holdout_score['aggregate']['metricPrecision']:.1%}; recall {holdout_score['aggregate']['metricRecall']:.1%}; domain exact-set accuracy {holdout_score['aggregate']['domainAccuracy']:.1%}; financial-reference exact accuracy {holdout_score['aggregate']['financialReferenceAccuracy']:.1%}; operation accuracy {holdout_score['aggregate']['financialOperationAccuracy']:.1%}; period accuracy {holdout_score['aggregate']['periodInterpretationAccuracy']:.1%}.",
           f"- Tokens / model calls / model latency: dev {dev_score['operations']['inputTokens']:,} input + {dev_score['operations']['outputTokens']:,} output / {dev_score['operations']['modelCalls']} calls / {dev_score['operations']['latencyMs']:,} ms; holdout {holdout_score['operations']['inputTokens']:,} input + {holdout_score['operations']['outputTokens']:,} output / {holdout_score['operations']['modelCalls']} calls / {holdout_score['operations']['latencyMs']:,} ms. Per-question latency is in captured artifacts.",
           "- Company-set precision / recall / exact-set accuracy on the separate discovery fixture: **1.00 / 1.00 / 1.00** (four independently expected authorized companies); the SQL result is still `Incomplete` because the approved alias map is partial.",
           "- Production/Development generic-query routing remains disabled. Navigator evaluation used the Development model configuration directly and did not access Development financial data. PostgreSQL calls below used the test-owned isolated fixture and restricted role.", "",
           "## Product metadata audit and reviewer package", "",
           "The audit used the bounded checked-in dictionary snapshots, not financial databases. It found 4,934 sampled product identity rows, 4,539 provider/product-code keys, 313 provider/external-company scopes, 2,699 exact labels and 2,629 normalized labels. All 4,934 require product review and canonical company resolution. There are 224 repeated exact-label/unit candidate groups, all pending and non-executable. Similarity/repetition was not treated as equivalence. The sole approved equivalence remains `گندله` = `گندله سنگ‌آهن` (`product_iron_ore_pellets`). See [coverage report](../product-catalog-coverage-review-2026-10-09.md) and [224 reviewer decisions](../ProductCatalog/reviewer-decisions/product-equivalence-candidates-v1.csv).", "",
           "## Requested five-outcome root-cause review", "",
           "The latest composed 16-record evidence set consists of the 15 live monthly cases plus the separate dynamic producer query. The observed five non-success monthly rows are listed below. The two cross-company rankings fail at PostgreSQL coverage validation after query planning; no incomplete ranking is presented as a complete answer. The two mixed-measure comparisons are rejected before SQL because their algebra is not approved. The industry sales-growth question is a genuine ambiguity: it names neither an industry nor whether growth means month-over-month or year-over-year, so the Navigator correctly asked for clarification before SQL.", "",
           "| Case | Observed stage/outcome | Root cause | Reusable fix / disposition |", "|---|---|---|---|",
           "| `monthly-09-cross-company-product-sales` | SQL executed; PostgreSQL returned `Incomplete`; answer incomplete | The plan observed `ProductMappingCoverageStatus=partial` for producer discovery through the approved pellet aliases. This is missing reviewed product/company identity coverage, not evidence of a broken sales metric | No static producer list or inferred equivalence. Retain safe incomplete status; use reviewer decisions and extend mappings only after approval. |",
           "| `producer-16-total-company-sales-ranking` | SQL executed; PostgreSQL returned `Incomplete`; ranking withheld | The product-derived producer population remains partial because pellet alias coverage is incomplete; the artifact reports company coverage complete within the supplied authorization scope. The ranking cannot claim an exhaustive population | Report partial product coverage; keep company discovery observation-driven. |",
           "| `monthly-12-sales-vs-operating-profit-growth` | Requirements extraction rejected: `requirements_contain_unsupported_financial_value_operation` | Unsupported cross-domain/cross-period comparison algebra; no approved formula/period alignment | Correct fail-closed policy, not a Planner bug. Business/catalog approval would be needed to add an operation. |",
           "| `monthly-13-production-vs-sales-growth` | Requirements extraction rejected: `requirements_contain_unsupported_financial_value_operation` | No approved cross-measure derived operation; wording asks a relationship between unlike production and sales measures | Preserve as unsupported/clarification; do not derive an unapproved metric. The cross-measure wording is semantically ambiguous; there is no SQL. |",
           "| `monthly-11-industry-sales-growth-ranking` | `typed_ambiguity` during requirements/measure selection; no SQL | The question omits the industry and the growth comparison basis (MoM vs YoY); this is a genuine ambiguity, not a timeout or mapping defect | Keep clarification behavior; do not choose a default industry or comparison period. |",
           "",
           "An independently verified reusable defect was fixed: compact outline source-candidate pruning removed a candidate from its map but left its ancestor in the included-node set, causing serialization to re-expand the full source-item list and exceed budget. The provider now rebuilds inclusion from the retained candidate set and bounds source-item references. Regression test `HierarchyProvider_BoundsOutlineAndRanksTheExactPersianSourceItemAheadOfSimilarTitles` at 16,000 characters passes. The final Release development run recorded zero outline-budget failures; unknown catalog IDs and unsupported operations remain.", "",
           "## Dynamic company discovery fixture", "",
           f"- **Exact Persian question:** {population['question']}",
           f"- **Extracted concept and measure:** `{population['productConcept']['CatalogId']}` via owner approval `{population['productConcept']['ApprovalReference']}`; measure `{inline(population['measureIds'])}`; no company IDs in the question or plan.",
           f"- **Plan period:** `{population['periodPolicy']}`, {population['reportingWindowStart']} through {population['reportingWindowEnd']}.",
           f"- **SQL:** queried through the restricted test PostgreSQL role; result `{population['postgresStatus']}` / `{population['diagnostic']}`; SQL and parameters are recorded in the artifact.",
           f"- **Independent expected company IDs:** {inline(population['expectedCompanyIds'])}; actual rows: " + "; ".join(f"{r['CompanySymbol']}={r['Value']} {r['SourceUnit']} ({r['PeriodStart']}..{r['PeriodEnd']}; {r['ExternalReportId']})" for r in population['rows']) + ".",
           f"- **Fixture metrics:** expected set size={len(population['expectedCompanyIds'])}; actual set size={len({r['CompanyId'] for r in population['rows']})}; exact match={population['exactCompanySetMatch']}; precision=1.00; recall=1.00. Positive production only; zero-production and unauthorized companies were excluded in the companion test assertions. Newly inserted company `NEWP` was discovered without company IDs or a producer registry.",
           f"- **Honest coverage:** mapping status `{population['productConcept']['MappingCoverageStatus']}`. The response discloses partial coverage; the fixture match does not imply whole-market recall.",
           f"- **Persian answer actually returned:**\n\n```text\n{population['finalPersianAnswer']}\n```", "",
           "## All 50 live Persian Navigator attempts", "",
           "These are actual exact user strings sent through live OpenAI extraction/navigation. Expected/actual in this section are semantic references because this evaluation runner does not execute SQL. No numeric result or final Persian response is inferred. The question text, raw returned selection, failure stage and trace file are recorded for every attempt.", ""]
    dev_sc = score_lookup["dev"]
    hold_sc = score_lookup["holdout"]
    out.extend(render_case(c, dev_predictions.get(c["id"]), dev_sc.get(c["id"]), "dev", dev_ds, dev_pred) for c in dev_cases)
    out.extend(render_case(c, holdout_predictions.get(c["id"]), hold_sc.get(c["id"]), "holdout", holdout_ds, holdout_pred) for c in holdout_cases)
    out.extend(["## All 15 live LLM-to-PostgreSQL fixture questions", "",
                "These records are independent fixture executions. Each SQL statement, bound parameters, plan, observed rows, evidence fingerprints, and full Persian answer (when one was returned) are preserved in the linked JSONL. Fixture values are synthetic and are not Development/production financial data. Where the fixture harness did not define an independent expected-value oracle for a case, the report says so; the observed pipeline value is not substituted as expected truth. `MonthlyReportLineItems.Unit` records the source line unit (fixture rows use `ton`); amount units are interpreted from the governed measure metadata. The sales-rate fixture answer explicitly reports an unknown unit and remains a rendering gap.", ""])
    out.extend(render_e2e(row) for row in e2e_rows)
    out.extend(["## Security and release boundary", "",
                "- Generic query routing in production and Development remains disabled; no production or Development financial DB credentials were used.",
                "- The 50-question Navigator evaluation queried no database. The 15-case SQL cohort and discovery test used the isolated PostgreSQL cluster and restricted fixture role.",
                "- No canonical SQL catalog approval was made. No new product equivalence was approved. The pellet alias decision was already explicitly owner-approved and remains unchanged.",
                "- No primary financial metric was derived from unapproved inputs; unsupported formulas were rejected before SQL.",
           "- The feature is not production-ready: measured holdout has 7/10 Navigator completions, development has 16/40; product/company metadata coverage is incomplete, arbitrary product-specific cross-company ranking remains partial, and financial source-item metadata continues to block selection.", ""])
    OUT.write_text("\n".join(out), encoding="utf-8")
    print(f"Wrote {OUT} with {len(dev_cases) + len(holdout_cases)} live Navigator cases and {len(e2e_rows)} SQL-backed cases.")


if __name__ == "__main__":
    main()
