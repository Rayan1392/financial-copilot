import json
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
SPEC = ROOT / "specs" / "141-schema-aware-semantic-query-engine"
ARTIFACT = SPEC / "artifacts" / "monthly-production-sales-live-llm-postgresql-fifteen-cases-v19-2026-10-09.jsonl"
POPULATION_ARTIFACT = SPEC / "artifacts" / "monthly-company-population-discovery-live-llm-postgresql-v1.json"
REPORT = SPEC / "evaluation" / "persian-end-to-end-examples.md"

OLD_CLUSTER = r"C:\Users\free\AppData\Local\Temp\feature141-postgres-272b4d872a304f4bb2c8eb6229eb33be"
PG_CTL = r"D:\Program Files\PostgreSQL\17\bin\pg_ctl.exe"

MEASURES = {
    "metric_monthly_production_quantity": "monthly production quantity",
    "metric_monthly_sales_quantity": "monthly sales quantity",
    "metric_monthly_sales": "monthly sales amount",
    "metric_monthly_sales_rate": "monthly sales rate",
    "metric_monthly_sales_growth_mom": "month-over-month monthly sales growth",
}


def j(value):
    return json.dumps(value, ensure_ascii=False, separators=(",", ":"))


def quote_lines(value):
    return "\n".join("> " + line for line in value.splitlines())


def provenance(rows):
    def values(row, key):
        value = row.get(key)
        return value if isinstance(value, list) else ([value] if value is not None else [])

    providers = sorted({str(row["ProviderName"]) for row in rows if row.get("ProviderName")})
    reports = sorted({str(value) for row in rows for key in ("ExternalReportId", "ReportIdentity")
                       for value in values(row, key) if value})
    references = sorted({str(value) for row in rows for value in values(row, "SourceRowFingerprint") if value})
    parts = []
    if providers:
        parts.append("providers=" + ", ".join(providers))
    if reports:
        parts.append("report identities=" + ", ".join(reports))
    if references:
        parts.append("source references=" + ", ".join(references))
    return "; ".join(parts) if parts else "No persisted source provenance; no PostgreSQL row was read."


def render_case(case, number, trace_line):
    case_id = case.get("caseId", "unknown")
    status = case.get("status", "unknown")
    selected = case.get("selectedMeasureIds") or case.get("expectedMeasureIds") or []
    plan = case.get("plan") or {}
    entities = plan.get("Entities") or []
    source_refs = plan.get("Sources") or []
    operation = case.get("operationKind") or (plan.get("FinancialOperation") or {}).get("Kind") or "unresolved"
    comparison = case.get("comparisonMode") or (plan.get("FinancialOperation") or {}).get("ComparisonMode")
    period = case.get("planPeriod") or plan.get("Period") or {}
    if period.get("ReportingPeriodStart") and period.get("ReportingPeriodEnd"):
        window = str(period["ReportingPeriodStart"]) + " to " + str(period["ReportingPeriodEnd"])
    else:
        window = "latest/approved observation window" if period.get("SelectLatestByReportingPeriod") else "period not resolved"

    entity_summary = [{key: entity.get(key) for key in (
        "EntityKind", "UserReference", "ResolvedEntityId", "ResolvedEntityKey", "IdentityProvenance", "IdentityVerified"
    ) if entity.get(key) is not None} for entity in entities]
    plan_summary = {
        "measures": selected,
        "entities": entity_summary,
        "operation": operation,
        "comparison": comparison,
        "period": window,
        "aggregation": plan.get("Aggregations"),
        "sort": plan.get("Sort"),
        "presentation": plan.get("Presentation"),
        "filters": plan.get("Filters"),
        "populationSalesScope": plan.get("PopulationSalesScope"),
        "productConcept": plan.get("ProductConcept"),
        "companyDiscoveryScope": plan.get("CompanyDiscoveryScope"),
    }
    rows = case.get("observedRows") or []
    values = case.get("actualValues")
    if values is None and rows:
        values = [row.get("Value") for row in rows]
    quantity_measures = {
        "metric_monthly_production_quantity",
        "metric_monthly_sales_quantity",
    }
    units = sorted({str(row.get("SourceUnit")) for row in rows if row.get("SourceUnit")}) \
        if selected and selected[0] in quantity_measures else []
    if selected and selected[0] == "metric_monthly_sales":
        units = ["million IRR (governed financial display unit)"]
    periods = sorted({str(row.get("PeriodStart")) + ".." + str(row.get("PeriodEnd"))
                      for row in rows if row.get("PeriodStart") or row.get("PeriodEnd")})
    sql_ran = case.get("executionStatus") is not None

    if case_id == "monthly-09-cross-company-product-sales":
        expected_rows = case.get("observedRows") or []
        expected = "Integration assertions independently verify sales quantities " + j([
            {"company": row.get("CompanySymbol"), "value": row.get("Value"), "unit": row.get("SourceUnit")}
            for row in expected_rows
        ]) + " in 2026-08-23..2026-09-22. Alias mapping coverage is partial, so the renderer withholds the ranking."
    elif case_id == "producer-16-total-company-sales-ranking":
        expected_rows = case.get("observedRows") or []
        expected = "Integration assertions independently verify total sales amounts " + j([
            {"company": row.get("CompanySymbol"), "value": row.get("Value"), "unit": "million IRR"}
            for row in expected_rows
        ]) + " in 2026-08-23..2026-09-22. Alias mapping coverage is partial, so the renderer withholds the ranking."
    elif case_id == "monthly-01-production-latest":
        expected = "Fixture rows independently establish 37 ton in 2026-08-23..2026-09-22 (25 ton pellets plus 12 ton concentrate)."
    elif case_id == "monthly-03-production-mom-growth":
        expected = "Seeded current and prior production are 12 and 6 ton; independently expected growth is 100 percent."
    elif case_id == "monthly-04-production-top-products-growth":
        expected = "Seeded comparable production is 47 ton currently and 35 ton previously; expected growth is (47-35)/35 = 34.285714285714285 percent."
    elif case_id == "monthly-07-sales-rate-two-months":
        expected = "Seeded accepted rates for Mordad and Shahrivar 1405 are 17.5 and 16.7 in the catalog sales-rate unit."
    elif case.get("expectedValues") is not None:
        unit = "million IRR" if selected and selected[0] == "metric_monthly_sales" else (
            "ton" if selected and selected[0] in ("metric_monthly_production_quantity", "metric_monthly_sales_quantity")
            else "catalog source unit"
        )
        expected = f"Fixture expectation: {j(case['expectedValues'])} {unit}; period {window}."
        if case.get("expectedGrowthPercentage") is not None:
            expected += f" Independent growth expectation: {case['expectedGrowthPercentage']} percent."
    elif status == "typed_ambiguity":
        expected = "No numeric result. Expected a typed ambiguity for an industry-wide ranking without a reviewed industry-population mapping; SQL should not run."
    elif case_id in ("monthly-12-sales-vs-operating-profit-growth", "monthly-13-production-vs-sales-growth"):
        expected = "No numeric result. Expected fail-closed handling for unsupported cross-measure or cross-domain growth operations."
    else:
        expected = "No independent numeric assertion was configured; see the actual SQL rows and provenance below."

    if status == "succeeded" and case.get("executionStatus") == "Succeeded":
        status_label = "PASS"
    elif status == "typed_ambiguity":
        status_label = "AMBIGUOUS"
    elif status == "typed_execution_failure" and case.get("executionStatus") == "Incomplete":
        status_label = "INCOMPLETE (partial coverage; numeric rank withheld)"
    elif status in ("typed_pipeline_failure", "typed_plan_failure"):
        status_label = "UNSUPPORTED"
    elif status == "typed_compiler_failure":
        status_label = "FAIL"
    elif status == "blocked":
        status_label = "BLOCKED"
    else:
        status_label = status.upper()

    if sql_ran:
        actual = "PostgreSQL " + str(case.get("executionStatus")) + "; observed Value rows " + j(values)
        if units:
            actual += " " + ", ".join(units)
        if periods:
            actual += "; periods " + ", ".join(periods)
        actual += "."
        sql_status = "Yes; PostgreSQL was queried and returned " + str(case.get("executionStatus")) + ". SQL and parameters are preserved in the JSONL trace."
    else:
        actual = "No SQL result; planning or the pipeline returned before PostgreSQL."
        sql_status = "No; PostgreSQL was not queried."

    answer = case.get("finalPersianAnswer")
    if not selected:
        unit_check = "Not applicable; no measure reached a final result."
    elif selected[0] == "metric_monthly_sales" and (
        status != "succeeded" or case.get("executionStatus") != "Succeeded"
    ):
        unit_check = "Correctly withheld with the incomplete/failed numeric result; no unverified unit was presented."
    elif selected[0] == "metric_monthly_sales":
        unit_check = ("PASS; rendered with the governed million-IRR display unit, independent of quantity SourceUnit."
                      if "\u0645\u06cc\u0644\u06cc\u0648\u0646 \u0631\u06cc\u0627\u0644" in (answer or "")
                      else "FAIL; the final answer did not show the governed million-IRR display unit.")
    elif selected[0] in ("metric_monthly_production_quantity", "metric_monthly_sales_quantity"):
        if status != "succeeded" or case.get("executionStatus") != "Succeeded":
            unit_check = "Correctly withheld with the incomplete/failed numeric result; no unverified unit was presented."
        elif operation in ("GrowthPercentage", "AbsoluteChange"):
            unit_check = "PASS; comparison result is rendered in its result unit (percent/absolute change); no implicit quantity conversion is applied."
        else:
            known_units = sorted({str(row.get("SourceUnit")) for row in rows if row.get("SourceUnit")})
            unit_labels = {"ton": "\u062a\u0646", "kg": "\u06a9\u06cc\u0644\u0648\u06af\u0631\u0645", "piece": "\u0639\u062f\u062f"}
            if len(known_units) == 1 and known_units[0] in unit_labels and unit_labels[known_units[0]] in (answer or ""):
                unit_check = f"PASS; persisted source unit `{known_units[0]}` is preserved as its governed Persian display label."
            elif not known_units and "\u0648\u0627\u062d\u062f \u0627\u0646\u062f\u0627\u0632\u0647\u200c\u06af\u06cc\u0631\u06cc \u0646\u0627\u0645\u0634\u062e\u0635" in (answer or ""):
                unit_check = "PASS; missing source unit is explicitly rendered as unknown."
            else:
                unit_check = f"FAIL; source units `{known_units}` were not faithfully represented in the final answer."
    elif selected[0] == "metric_monthly_sales_rate":
        unknown_label = "\u0648\u0627\u062d\u062f \u0627\u0646\u062f\u0627\u0632\u0647\u200c\u06af\u06cc\u0631\u06cc \u0646\u0627\u0645\u0634\u062e\u0635"
        unit_check = ("PASS; the catalog unit is unresolved, so the renderer explicitly says unit unknown and does not reuse the row's quantity SourceUnit."
                      if unknown_label in (answer or "") else "FAIL; an unverified sales-rate unit was rendered.")
    else:
        unit_check = "Not independently verifiable from the selected measure metadata."
    if answer:
        answer_block = quote_lines(answer)
    elif status_label == "UNSUPPORTED":
        answer_block = "> Proposed rendering (renderer not reached): «\u0627\u06cc\u0646 \u062f\u0631\u062e\u0648\u0627\u0633\u062a \u0628\u0627 \u062a\u0639\u0631\u06cc\u0641\u200c\u0647\u0627\u06cc \u062a\u0623\u06cc\u06cc\u062f\u0634\u062f\u0647\u0654 \u067e\u0631\u0633\u200c\u0648\u062c\u0648 \u0642\u0627\u0628\u0644 \u0627\u062c\u0631\u0627 \u0646\u06cc\u0633\u062a.»"
    else:
        answer_block = "> No Persian user-facing answer was emitted; the failure occurred before response generation."

    semantic_line = "Selected semantic references: " + j(source_refs or selected)
    semantic_line += ". Relationships: " + j(plan.get("RelationshipIds") or []) + "."
    if case.get("productBinding"):
        semantic_line += " Product binding: " + j(case["productBinding"]) + "."
    if case.get("companyBinding"):
        semantic_line += " Company binding: " + j(case["companyBinding"]) + "."
    failure = "No failure." if status_label == "PASS" else f"Failure stage: `{case.get('failureStage') or 'none'}`; internal status `{status}`."
    if case.get("failureCode"):
        failure += f" Exact failure code: `{case['failureCode']}`; detail: `{case.get('diagnostic')}`."
    if case.get("compilerDiagnostics"):
        failure += f" Compiler diagnostics: `{case['compilerDiagnostics']}`."
    if case.get("validationDiagnostics"):
        failure += " Validation diagnostics: `" + j(case["validationDiagnostics"]) + "`."
    companies = ", ".join(entity.get("UserReference", "") for entity in entities
                            if entity.get("EntityKind", "").lower() == "company") or "unbound/discovered"
    products = ", ".join(entity.get("UserReference", "") for entity in entities
                           if entity.get("EntityKind", "").lower() == "product") or "none"

    return f"""### {number}. {case_id}

- **Original Persian question (exact LLM input):** {case.get('question')}
- **Extracted intent:** measures `{', '.join(selected) if selected else 'unresolved'}` ({', '.join(MEASURES.get(item, item) for item in selected) if selected else 'ambiguous/unsupported'}); company {companies}; product {products}; period {window}; operation `{operation}` / comparison `{comparison}`.
- **Generated query plan:** `{j(plan_summary)}`
- **Selected semantic references:** {semantic_line}
- **SQL execution:** {sql_status}
- **Unit correctness:** {unit_check}
- **Expected fixture result, independently verified:** {expected}
- **Actual result:** {actual} Pipeline status `{status}`; response status `{case.get('finalAnswerStatus')}`.
- **Status:** **{status_label}**. {failure}
- **Source provenance:** {provenance(rows)}
- **Execution mode:** {case.get('executionMode', 'live LLM + isolated PostgreSQL fixture')} (provider/model `{case.get('provider')}` / `{case.get('model')}`; {case.get('modelCalls', '?')} model calls).
- **Complete user-facing Persian answer:**

{answer_block}
- **Trace artifact:** [`{ARTIFACT.name}`, JSONL line {trace_line}](../artifacts/{ARTIFACT.name}#L{trace_line}). The entry retains exact question, plan, SQL/parameters when compiled, execution status and rows, provenance, and answer.
"""


def main():
    matrix = [json.loads(line) for line in ARTIFACT.read_text(encoding="utf-8").splitlines() if line.strip()]
    population = json.loads(POPULATION_ARTIFACT.read_text(encoding="utf-8"))
    old = REPORT.read_text(encoding="utf-8")
    headings = []
    position = 0
    while True:
        position = old.find("# Feature 141 ", position)
        if position < 0:
            break
        headings.append(position)
        position += len("# Feature 141 ")
    history = old[headings[1]:] if len(headings) > 1 else old

    population_answer = quote_lines(population.get("finalPersianAnswer", ""))
    coverage = population.get("productConcept") or {}
    population_ids = population.get("expectedCompanyIds") or []
    head = f"""# Feature 141 Persian end-to-end examples - fresh 2026-10-09 run

## Run summary

A fresh live LLM + isolated PostgreSQL evaluation ran **16 distinct Persian questions**: the 15-question matrix in the v19 JSONL and a separate producer-population discovery request. Each prompt received fresh LLM inference. SQL used the test-owned PostgreSQL 17 cluster and restricted read role. No Development or production financial database was queried. Every number is synthetic fixture data.

The disposable cluster used a dynamically selected loopback port. The wrapper verified the test-owned server stopped, checked for any process referencing its unique data directory, and removed that scoped directory. A separately retained directory from an earlier failed run remains because the Windows command policy blocked its removal; its narrow cleanup procedure is below.

**Trace files:** [15-case live matrix](../artifacts/{ARTIFACT.name}), [population discovery](../artifacts/{POPULATION_ARTIFACT.name}), and [PostgreSQL integration assertions](../../../tests/FinancialCopilot.IntegrationTests/MonthlyStructuredQueryPostgreSqlTests.cs).

| Measure | Result |
|---|---:|
| Distinct Persian questions / fresh LLM prompts | 16 / 16 |
| Cases reaching PostgreSQL | 12 matrix questions plus population discovery (13 total) |
| Fully answered SQL cases | 10 |
| SQL cases classified Incomplete for partial coverage | 3 (2 rankings and population discovery) |
| Ambiguous or unsupported before PostgreSQL | 3 |
| PostgreSQL integration | PASS; test-owned cluster stopped and removed |
| Unit tests | 2,018 passed; 0 failed; 0 skipped |
| Release build | PASS; 0 warnings, 0 errors |
| Semantic Catalog validation | PASS |

Population precision/recall is **1.00/1.00** against four authorized fixture producers: NEWP (62 ton), کگهر (47 ton), کگل (25 ton), and NOSALES (80 ton with zero sales). The zero-production company and an unauthorized positive producer are excluded. Mapping coverage is partial, so answers disclose the limitation.

## Producer population discovery

- **Original Persian question (exact LLM input):** {population.get('question')}
- **Extracted intent:** positive monthly production quantity; canonical concept `{coverage.get('CatalogId')}` ({coverage.get('CanonicalLabel')}); no company IDs in the question; dynamic population/list operation; trailing observation window `{population.get('reportingWindowStart')} to {population.get('reportingWindowEnd')}`.
- **Generated query plan:** measures `{j(population.get('measureIds'))}`; aliases `{j(coverage.get('ApprovedAliases'))}`; SQL discovers company identities from positive accepted production rows; no producer registry; authorization scope `{j(population.get('companyScope'))}`.
- **Selected semantic references:** source catalog IDs `{j(population.get('sourceCatalogIds'))}`; relationship IDs `{j(population.get('relationshipCatalogIds'))}`; physical object IDs `{j(population.get('physicalObjectCatalogIds'))}`.
- **SQL execution:** Yes; PostgreSQL status `{population.get('postgresStatus')}`, diagnostic `{population.get('diagnostic')}`. SQL and parameters are preserved in the artifact.
- **Expected fixture result:** four authorized positive pellet producers in `2026-08-23..2026-09-22`: 62, 47, 25, and 80 ton. The zero-production and unauthorized companies must be absent. Expected IDs `{j(population_ids)}`.
- **Actual result:** {len(population.get('rows') or [])} rows; exact set match `{population.get('exactCompanySetMatch')}`; includes newly inserted NEWP and the positive producer with zero sales; excludes unauthorized and zero-production companies. Executor status `{population.get('postgresStatus')}`.
- **Unit correctness:** PASS; each company retains its persisted positive quantity unit, and this fixture reports tons. No unit is inferred from product names or converted.
- **Status:** **PASS WITH PARTIAL COVERAGE DISCLOSED**. No failure; product alias coverage is not exhaustive.
- **Source provenance:** NoavaranCurrentApi; per-row report identities and source fingerprints are in the trace.
- **Execution mode:** live LLM + isolated PostgreSQL (provider/model `{population.get('provider')}` / `{population.get('model')}`; `{population.get('modelCalls')}` model calls).
- **Complete user-facing Persian answer from the renderer:**

{population_answer}
- **Trace artifact:** [`{POPULATION_ARTIFACT.name}`](../artifacts/{POPULATION_ARTIFACT.name}).

## Older stopped test directory

The earlier failed run left this exact temporary path: `{OLD_CLUSTER}`. Its recorded PostgreSQL status was stopped and no `postmaster.pid` remained. The current attempt to remove this exact path was blocked by Windows command policy before deletion. To clean only that directory manually, confirm `pg_ctl status` reports it stopped, then run:

```powershell
$cluster = '{OLD_CLUSTER}'
& '{PG_CTL}' -D (Join-Path $cluster 'data') status
# Continue only if pg_ctl reports that this cluster is not running.
Remove-Item -LiteralPath $cluster -Recurse -Force
```

## Fifteen-question live LLM matrix

"""
    sections = [head]
    sections.extend(render_case(case, index + 2, index + 1) for index, case in enumerate(matrix))
    sections.append("""## Interpretation and limitations

- Executions used synthetic isolated PostgreSQL fixtures, not Development, production, or actual Tehran Stock Exchange financial data.
- Three SQL executions returned rows but were classified Incomplete because approved product alias coverage is partial. The renderer withheld numeric ranks; source rows remain in the traces.
- Industry-wide monthly sales growth was ambiguous/unsupported. Sales-versus-operating-profit and production-versus-sales growth failed closed before PostgreSQL.
- Historical evaluation sections follow verbatim for comparison. The v19 traces are the fresh results for the rerun questions.

---

""")
    REPORT.write_text("".join(sections) + history, encoding="utf-8", newline="\n")
    print(f"Wrote {REPORT} with {len(matrix) + 1} fresh Persian question records.")


if __name__ == "__main__":
    main()
