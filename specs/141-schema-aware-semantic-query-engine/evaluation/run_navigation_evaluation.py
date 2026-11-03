#!/usr/bin/env python3
"""Score recorded vectorless navigator outputs against the golden or an external held-out dataset.

The runner deliberately consumes captured outputs; it never fabricates model quality or calls a model.
Dataset and prediction files are JSON/JSONL. The golden .yaml file is JSON-compatible YAML.
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from pathlib import Path
from typing import Any


def load_dataset(path: Path) -> list[dict[str, Any]]:
    payload = json.loads(path.read_text(encoding="utf-8-sig"))
    cases = payload.get("cases") if isinstance(payload, dict) else None
    if not isinstance(cases, list):
        raise ValueError("dataset must be a JSON object containing a cases array")
    if any(not isinstance(case, dict) or not case.get("id") for case in cases):
        raise ValueError("every dataset case must be an object with an id")
    return cases


def load_predictions(path: Path) -> dict[str, dict[str, Any]]:
    result: dict[str, dict[str, Any]] = {}
    for line_number, raw in enumerate(path.read_text(encoding="utf-8-sig").splitlines(), 1):
        if not raw.strip():
            continue
        row = json.loads(raw)
        case_id = row.get("caseId") or row.get("id")
        if not case_id or case_id in result:
            raise ValueError(f"prediction line {line_number} has a missing or duplicate caseId")
        result[case_id] = row
    return result


def strings(row: dict[str, Any], *keys: str) -> set[str]:
    value: Any = row
    for key in keys:
        value = value.get(key, {}) if isinstance(value, dict) else None
    if isinstance(value, list):
        return {str(item) for item in value if item is not None}
    return set()


def normalize_metric(value: str) -> str:
    return re.sub(r"[^a-z0-9]", "", value.lower().removeprefix("metric"))


def score_set(expected: set[str], actual: set[str]) -> dict[str, Any]:
    if not expected:
        return {"applicable": False, "recall": None, "precision": None, "f1": None,
                "correct": 0, "expected": 0, "actual": len(actual)}
    common = expected & actual
    recall = len(common) / len(expected)
    precision = len(common) / len(actual) if actual else 0.0
    f1 = 2 * precision * recall / (precision + recall) if precision + recall else 0.0
    return {"applicable": True, "recall": len(common) / len(expected),
            "precision": precision, "f1": f1,
            "correct": len(common), "expected": len(expected), "actual": len(actual),
            "missing": sorted(expected - actual), "unsupported": sorted(actual - expected)}


def metric_values(row: dict[str, Any]) -> set[str]:
    # Navigator precision is measured on explicit answer references. Expanded
    # hierarchy metrics are candidates and must never count as selected output.
    values = strings(row, "selectedMeasureIds") or strings(row, "metricIds") or strings(row, "metrics")
    return {normalize_metric(item) for item in values}


def node_values(row: dict[str, Any]) -> set[str]:
    values = strings(row, "selectedNodeIds") | strings(row, "nodeIds")
    return values


def requirement(row: dict[str, Any], key: str) -> set[str]:
    return strings(row, key) | strings(row, "requirements", key) | strings(row, "declaredRequirements", key)


def financial_reference_values(row: dict[str, Any]) -> set[str]:
    values: Any = row.get("selectedFinancialValueReferences", row.get("SelectedFinancialValueReferences",
        row.get("financialValueReferences", row.get("FinancialValueReferences"))))
    if values is None and isinstance(row.get("requirements"), dict):
        values = row["requirements"].get("financialValueReferences")
    if not isinstance(values, list):
        return set()
    return {f"{item.get('referenceType')}:{item.get('catalogId')}" for item in values if isinstance(item, dict)}


def financial_references(row: dict[str, Any], *, independent: bool = False) -> list[dict[str, Any]]:
    source = row.get("independentRequirements", {}) if independent else row
    if not isinstance(source, dict):
        return []
    values: Any = source.get("financialValueReferences", source.get("FinancialValueReferences"))
    if values is None and independent and isinstance(row.get("requirements"), dict):
        values = row["requirements"].get("financialValueReferences")
    return [item for item in values if isinstance(item, dict)] if isinstance(values, list) else []


def financial_metric_codes(row: dict[str, Any]) -> set[str]:
    return {str(item.get("metricCode")) for item in financial_references(row) if item.get("metricCode")}


def requirement_values(row: dict[str, Any], key: str) -> set[str]:
    extracted = row.get("independentRequirements", {})
    if not isinstance(extracted, dict):
        extracted = {}
    return {str(value) for value in extracted.get(key, []) if value is not None}


def financial_operation(row: dict[str, Any]) -> Any:
    operation = row.get("financialValueOperation", row.get("FinancialValueOperation",
        row.get("requestedFinancialValueOperation", row.get("RequestedFinancialValueOperation"))))
    if operation is None and isinstance(row.get("requirements"), dict):
        operation = row["requirements"].get("financialValueOperation")
    return operation


def mean(values: list[float]) -> float | None:
    return sum(values) / len(values) if values else None


def evaluate(cases: list[dict[str, Any]], predictions: dict[str, dict[str, Any]]) -> dict[str, Any]:
    rows: list[dict[str, Any]] = []
    dimensions = {"nodeRecall": [], "metricPrecision": [], "metricRecall": [], "metricExactSetAccuracy": [], "dimensionRecall": [], "relationshipRecall": [],
                  "domainAccuracy": [], "domainPrecision": [], "domainRecall": [], "domainF1": [],
                  "statementScopeAccuracy": [], "financialPolicyRecall": [],
                  "contextCompletenessAccuracy": [], "missingUnsupportedAccuracy": [],
                  "financialReferenceAccuracy": [], "financialOperationAccuracy": [],
                  "financialReferenceTypeAccuracy": [], "sourceItemReferenceAccuracy": [],
                  "governedMetricCodeAccuracy": [], "providerFinancialIndicatorAccuracy": [],
                  "requirementsFinancialReferenceRecall": [], "entityResolutionAccuracy": [],
                  "periodInterpretationAccuracy": [], "falsePositiveAcceptanceRate": []}

    for case in cases:
        case_id = case["id"]
        pred = predictions.get(case_id)
        if pred is None:
            rows.append({"caseId": case_id, "status": "missing_prediction"})
            continue

        actual_nodes = node_values(pred)
        expected_nodes = set(case.get("nodeIds", []))
        node_score = score_set(expected_nodes, actual_nodes)
        if node_score["applicable"]:
            dimensions["nodeRecall"].append(node_score["recall"])

        expected_domains = {f"domain-{item}" for item in case.get("domains", [])}
        actual_domains = {node.removeprefix("domain-") for node in actual_nodes if node.startswith("domain-")}
        expected_domain_names = {item.removeprefix("domain-") for item in expected_domains}
        domain_score = score_set(expected_domain_names, actual_domains)
        if domain_score["applicable"]:
            dimensions["domainPrecision"].append(domain_score["precision"])
            dimensions["domainRecall"].append(domain_score["recall"])
            dimensions["domainF1"].append(domain_score["f1"])
            exact_domain_hit = 1.0 if expected_domain_names == actual_domains else 0.0
            dimensions["domainAccuracy"].append(exact_domain_hit)
            domain_score["accuracy"] = exact_domain_hit
            domain_score["contract"] = "exact-set-match-of-catalog-domain-node-ids"

        expected_metrics = {normalize_metric(value) for value in case.get("metrics", case.get("expectedMetrics", []))}
        metric_score = score_set(expected_metrics, metric_values(pred))
        if metric_score["applicable"]:
            dimensions["metricPrecision"].append(metric_score["precision"])
            dimensions["metricRecall"].append(metric_score["recall"])
            dimensions["metricExactSetAccuracy"].append(1.0 if expected_metrics == metric_values(pred) else 0.0)

        dimension_score = score_set(set(case.get("dimensions", [])), requirement(pred, "dimensionIds"))
        if dimension_score["applicable"]:
            dimensions["dimensionRecall"].append(dimension_score["recall"])

        relationship_score = score_set(set(case.get("relationshipIds", [])), requirement(pred, "relationshipIds"))
        if relationship_score["applicable"]:
            dimensions["relationshipRecall"].append(relationship_score["recall"])

        expected_scope = case.get("statementScope")
        scope_score = None
        if expected_scope is not None:
            actual_scope = requirement(pred, "statementScopes")
            if isinstance(expected_scope, dict) and expected_scope.get("field") == "IsComposing":
                values = expected_scope.get("values", [expected_scope.get("value")])
                expected_scopes = {"consolidated" if value is True else "standalone" for value in values if isinstance(value, bool)}
                scope_score = 1.0 if expected_scopes and expected_scopes <= actual_scope else 0.0
            elif isinstance(expected_scope, str):
                scope_score = 1.0 if expected_scope in actual_scope else 0.0
            else:
                scope_score = 0.0
            dimensions["statementScopeAccuracy"].append(scope_score)

        expected_policies = set(case.get("financialPolicies", []))
        actual_policies = requirement(pred, "sourcePolicies") | requirement(pred, "periodPolicies") | requirement(pred, "statementScopes")
        policy_score = score_set(expected_policies, actual_policies)
        if policy_score["applicable"]:
            dimensions["financialPolicyRecall"].append(policy_score["recall"])

        expected_complete = case.get("expectedComplete")
        completeness_score = None
        if expected_complete is not None:
            actual_complete = pred.get("isComplete", pred.get("completenessStatus") == "Complete")
            completeness_score = 1.0 if bool(actual_complete) == bool(expected_complete) else 0.0
            dimensions["contextCompletenessAccuracy"].append(completeness_score)

        expected_outcome = case.get("expectedOutcome")
        outcome_score = None
        if expected_outcome is not None:
            outcome_score = 1.0 if str(pred.get("outcome", "")).casefold() == str(expected_outcome).casefold() else 0.0
            dimensions["missingUnsupportedAccuracy"].append(outcome_score)
            if str(expected_outcome).casefold() == "unsupported":
                dimensions["falsePositiveAcceptanceRate"].append(
                    1.0 if str(pred.get("outcome", "")).casefold() in {"complete", "selected", "accepted"} else 0.0)

        expected_financial_references = case.get("expectedFinancialValueReferences")
        financial_reference_score = None
        financial_type_score = None
        source_item_score = None
        metric_code_score = None
        provider_indicator_score = None
        requirements_reference_recall = None
        failure_classification: list[str] = []
        actual_financial = financial_references(pred)
        if expected_financial_references is not None:
            expected = {f"{item.get('referenceType')}:{item.get('catalogId')}"
                        for item in expected_financial_references if isinstance(item, dict)}
            actual = financial_reference_values(pred)
            financial_reference_score = 1.0 if expected == actual else 0.0
            dimensions["financialReferenceAccuracy"].append(financial_reference_score)

            expected_types = {item.get("referenceType") for item in expected_financial_references if isinstance(item, dict)}
            actual_types = {item.get("referenceType") for item in actual_financial}
            financial_type_score = 1.0 if expected_types == actual_types else 0.0
            dimensions["financialReferenceTypeAccuracy"].append(financial_type_score)

            expected_source_ids = {item.get("catalogId") for item in expected_financial_references
                                   if isinstance(item, dict) and item.get("referenceType") == "DirectSourceItem"}
            actual_source_ids = {item.get("catalogId") for item in actual_financial
                                 if item.get("referenceType") == "DirectSourceItem"}
            source_item_score = 1.0 if expected_source_ids == actual_source_ids else 0.0
            dimensions["sourceItemReferenceAccuracy"].append(source_item_score)

            expected_metric_codes = {item.get("metricCode") for item in expected_financial_references
                                     if isinstance(item, dict) and item.get("metricCode")}
            metric_code_score = 1.0 if expected_metric_codes == financial_metric_codes(pred) else 0.0
            dimensions["governedMetricCodeAccuracy"].append(metric_code_score)

            expected_indicator_ids = {item.get("catalogId") for item in expected_financial_references
                                      if isinstance(item, dict) and item.get("referenceType") == "ProviderFinancialIndicator"}
            actual_indicator_ids = {item.get("catalogId") for item in actual_financial
                                    if item.get("referenceType") == "ProviderFinancialIndicator"}
            provider_indicator_score = 1.0 if expected_indicator_ids == actual_indicator_ids else 0.0
            dimensions["providerFinancialIndicatorAccuracy"].append(provider_indicator_score)

            expected_ref_set = expected
            extracted_ref_set = financial_reference_values({"financialValueReferences": financial_references(pred, independent=True)})
            if expected_ref_set:
                requirements_reference_recall = len(expected_ref_set & extracted_ref_set) / len(expected_ref_set)
                dimensions["requirementsFinancialReferenceRecall"].append(requirements_reference_recall)
            if expected_ref_set != extracted_ref_set:
                failure_classification.append("requirements_extraction_error")
            elif expected_ref_set != actual:
                failure_classification.append("financial_reference_navigation_error")

        if "expectedEntityReferences" in case:
            expected_entities = set(case["expectedEntityReferences"])
            actual_entities = requirement_values(pred, "entityReferences")
            entity_score = 1.0 if expected_entities == actual_entities else 0.0
            dimensions["entityResolutionAccuracy"].append(entity_score)
            if entity_score == 0:
                failure_classification.append("entity_resolution_or_extraction_error")

        if "expectedPeriodPolicies" in case:
            expected_periods = set(case["expectedPeriodPolicies"])
            actual_periods = requirement_values(pred, "periodPolicies")
            period_score = 1.0 if expected_periods.issubset(actual_periods) else 0.0
            dimensions["periodInterpretationAccuracy"].append(period_score)
            if period_score == 0:
                failure_classification.append("period_interpretation_error")

        expected_financial_operation = case.get("expectedFinancialValueOperation")
        financial_operation_score = None
        if expected_financial_operation is not None:
            financial_operation_score = 1.0 if financial_operation(pred) == expected_financial_operation else 0.0
            dimensions["financialOperationAccuracy"].append(financial_operation_score)
            if financial_operation_score == 0:
                failure_classification.append("financial_operation_classification_error")

        if expected_scope is not None and scope_score == 0:
            failure_classification.append("statement_scope_interpretation_error")
        if outcome_score == 0 and expected_outcome is not None:
            failure_classification.append("unsupported_or_ambiguity_classification_error")
        diagnostics = pred.get("diagnostics", [])
        if isinstance(diagnostics, list) and any("not authorized" in str(item).lower() or "query_not_authorized" in str(item).lower() for item in diagnostics):
            failure_classification.append("authorization_limitation")

        rows.append({"caseId": case_id, "status": "scored", "nodeRecall": node_score,
                     "selectedNodeCount": len(actual_nodes),
                     "finalExpandedContextNodeCount": len(actual_nodes),
                     "nodeTruePositives": node_score["correct"],
                     "nodeFalsePositives": len(actual_nodes - expected_nodes),
                     "missingRequiredNodeCount": len(expected_nodes - actual_nodes),
                     "llmSelectedNodeCount": len(strings(pred, "llmSelectedNodeIds")) if "llmSelectedNodeIds" in pred else None,
                     "domainAccuracy": domain_score, "metricCorrectness": metric_score,
                     "expectedMeasureIds": sorted(expected_metrics),
                     "selectedMeasureIds": sorted(metric_values(pred)),
                     "candidateMeasureIds": sorted(strings(pred, "candidateMeasureIds")),
                     "dimensionCorrectness": dimension_score, "relationshipCoverage": relationship_score,
                     "statementScopeAccuracy": scope_score, "financialPolicyRecall": policy_score,
                     "contextCompletenessAccuracy": completeness_score, "missingUnsupportedAccuracy": outcome_score,
                     "financialReferenceAccuracy": financial_reference_score,
                     "financialOperationAccuracy": financial_operation_score,
                     "financialReferenceTypeAccuracy": financial_type_score,
                     "sourceItemReferenceAccuracy": source_item_score,
                     "governedMetricCodeAccuracy": metric_code_score,
                     "providerFinancialIndicatorAccuracy": provider_indicator_score,
                     "requirementsFinancialReferenceRecall": requirements_reference_recall,
                     "entityResolutionAccuracy": entity_score if "expectedEntityReferences" in case else None,
                     "periodInterpretationAccuracy": period_score if "expectedPeriodPolicies" in case else None,
                     "failureClassification": sorted(set(failure_classification)) or ["none"],
                     "expectedFinancialValueReferences": expected_financial_references,
                     "predictedFinancialValueReferences": actual_financial,
                     "predictedFinancialValueOperation": financial_operation(pred),
                     "modelCalls": pred.get("modelCalls"), "inputTokens": pred.get("inputTokens"),
                     "outputTokens": pred.get("outputTokens"), "latencyMs": pred.get("latencyMs"),
                     "providerReportedCost": pred.get("providerReportedCost"),
                     "providerReportedCurrency": pred.get("providerReportedCurrency")})

    measured = [row for row in rows if row["status"] == "scored"]
    completed_rows = [row for row in measured if predictions.get(row["caseId"], {}).get("status") == "completed"]
    failed_rows = [row for row in measured if predictions.get(row["caseId"], {}).get("status") == "failed"]
    return {
        "datasetCaseCount": len(cases), "scoredCaseCount": len(measured),
        "completedCaseCount": len(completed_rows), "failedCaseCount": len(failed_rows),
        "unconditionalSuccessRate": len(completed_rows) / len(cases) if cases else None,
        "failureCodes": {code: sum(1 for row in failed_rows if row.get("failureCode") == code)
                         for code in sorted({str(row.get("failureCode")) for row in failed_rows})},
        "unscoredCaseIds": [row["caseId"] for row in rows if row["status"] != "scored"],
        "aggregate": {key: mean(value) for key, value in dimensions.items()},
        "operations": {
            "modelCalls": sum(row.get("modelCalls") or 0 for row in measured),
            "inputTokens": sum(row.get("inputTokens") or 0 for row in measured),
            "outputTokens": sum(row.get("outputTokens") or 0 for row in measured),
            "latencyMs": sum(row.get("latencyMs") or 0 for row in measured),
            "providerReportedCost": sum((row.get("providerReportedCost") or 0) for row in measured)
                if all(row.get("providerReportedCost") is not None for row in measured) and measured else None,
            "costCurrency": next(iter({row.get("providerReportedCurrency") for row in measured
                                        if row.get("providerReportedCurrency")} ), None),
        },
        "perCase": rows,
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dataset", required=True, type=Path, help="Golden JSON-compatible YAML or held-out JSON dataset")
    parser.add_argument("--predictions", required=True, type=Path, help="Captured navigator outputs as JSONL")
    parser.add_argument("--output", type=Path, help="Optional report path; stdout when omitted")
    args = parser.parse_args()
    try:
        report = evaluate(load_dataset(args.dataset), load_predictions(args.predictions))
    except (OSError, ValueError, json.JSONDecodeError) as error:
        print(f"evaluation input error: {error}", file=sys.stderr)
        return 2
    serialized = json.dumps(report, ensure_ascii=False, indent=2) + "\n"
    if args.output:
        args.output.write_text(serialized, encoding="utf-8")
    else:
        print(serialized, end="")
    return 0 if report["scoredCaseCount"] == report["datasetCaseCount"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
