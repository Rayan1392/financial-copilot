#!/usr/bin/env python3
"""Score the isolated legacy-vs-canonical intent shadow run with visible denominators."""

import argparse
import json
import re
import unicodedata
from collections import Counter
from pathlib import Path


FAILURE_CATEGORIES = (
    "malformed_or_truncated_model_output",
    "structured_schema_validation_failure",
    "invalid_structured_output",
    "request_serialization_failure",
    "response_deserialization_failure",
    "invalid_request_schema",
    "provider_transport_failure",
    "provider_authentication_failure",
    "provider_quota_failure",
    "provider_rate_limited",
    "semantic_validation_failure",
    "unsupported_semantic_intent",
    "unknown_catalog_reference",
    "missing_catalog_coverage",
    "semantic_policy_rejection",
    "query_authorization_rejection",
    "planner_or_compiler_unsupported_operation",
    "provider_timeout_or_cancellation",
    "provider_failure_or_unavailable",
    "internal_implementation_error",
    "completed",
    "unmatched_attempt_reconciled_as_canceled",
)

LEGACY_MEASURE_MAP = {
    "metric_monthly_production_quantity": "productionquantity",
    "metric_monthly_sales_quantity": "salesquantity",
    "metric_monthly_sales": "salesamount",
    "metric_monthly_sales_amount": "salesamount",
    "metric_monthly_production_growth_yoy": "productionquantity",
    "metric_monthly_sales_growth_yoy": "salesamount",
    "metric_net_profit": "netprofit",
    "metric-pe-ttm": "priceearningsratio",
    "metric_pe_ttm": "priceearningsratio",
    "metric-latest-price": "latestprice",
    "metric_operating_profit": "operatingprofit",
    "metric_operating_profit_per_share": "operatingprofitpershare",
    "metric_net_profit_per_share": "earningspershare",
    "metric_eps": "earningspershare",
    "metric_monthly_sales_rate": "salesrate",
    "metric_cash_balance": "cashbalance",
    "metric_depreciation_expense": "depreciationexpense",
}

# Early shadow rows used System.Text.Json's default numeric enum encoding. Keep the scorer
# backwards-compatible with those rows while the runner emits stable camelCase enum strings.
ENUM_NAMES = {
    "operation": ["retrieve", "absoluteChange", "growthRate", "compare", "timeSeries", "aggregate", "rank", "discoverPopulation", "unsupported"],
    "comparison": ["none", "monthOverMonth", "yearOverYear", "periodOverPeriod", "multiCompany", "crossMeasure", "statementScope"],
    "period": ["unspecified", "latestAvailable", "explicitMonth", "explicitQuarter", "explicitFiscalYear", "relativePeriods", "dateRange"],
    "granularity": ["unspecified", "month", "quarter", "fiscalYear", "day"],
    "entityScope": ["namedCompanies", "unboundCompanyPopulation"],
    "output": ["scalar", "table", "timeSeries", "rankedList", "comparison", "explanation"],
    "unit": ["unspecified", "count", "ton", "irr", "millionIRR", "irrPerShare", "percent", "ratio", "currencyPerUnit", "billionIRR"],
    "thresholdOperator": ["equal", "greaterThan", "greaterThanOrEqual", "lessThan", "lessThanOrEqual"],
    "logicalOperator": ["and", "or"],
}


def enum_name(value, enum_kind):
    names = ENUM_NAMES[enum_kind]
    if isinstance(value, int) and not isinstance(value, bool):
        return names[value] if 0 <= value < len(names) else f"<invalid:{value}>"
    return value


def read_jsonl(path):
    rows = []
    if not path:
        return rows
    with Path(path).open(encoding="utf-8") as stream:
        for line_number, line in enumerate(stream, 1):
            if line.strip():
                try:
                    rows.append(json.loads(line))
                except json.JSONDecodeError as exc:
                    raise ValueError(f"Invalid JSON at {path}:{line_number}: {exc}") from exc
    return rows


def classify(result):
    if not result:
        return "internal_implementation_error"
    if result.get("status") == "completed":
        return "completed"
    category = str(result.get("failureCategory") or "").casefold()
    diagnostic = str(result.get("diagnostic") or "").casefold()
    combined = category + " " + diagnostic
    if category in FAILURE_CATEGORIES:
        return category
    if "timeout" in combined or "cancel" in combined or "deadline" in combined:
        return "provider_timeout_or_cancellation"
    if "truncat" in combined or "malformed_model_output" in combined or "oversized_model_output" in combined:
        return "malformed_or_truncated_model_output"
    if "schema_validation" in combined or "structured_schema" in combined:
        return "structured_schema_validation_failure"
    if "response_deserialization" in combined:
        return "response_deserialization_failure"
    if "request_serialization" in combined:
        return "request_serialization_failure"
    if "invalid_request_schema" in combined or "invalid_request" in combined:
        return "invalid_request_schema"
    if "invalid_structured_output" in combined or "structured_output_" in combined or "invalid_json" in combined:
        return "invalid_structured_output"
    if "authentication" in combined or "unauthorized" in combined:
        return "provider_authentication_failure"
    if "quota" in combined:
        return "provider_quota_failure"
    if "rate_limit" in combined or "rate_limited" in combined:
        return "provider_rate_limited"
    if "transport" in combined or "http_request" in combined:
        return "provider_transport_failure"
    if "catalog_reference" in combined or "unknown_reference" in combined:
        return "unknown_catalog_reference"
    if "coverage" in combined or "catalog_unavailable" in combined:
        return "missing_catalog_coverage"
    if "authorization" in combined or "not_authorized" in combined:
        return "query_authorization_rejection"
    if "unsupportedoperation" in combined or "planner" in combined or "compiler" in combined:
        return "planner_or_compiler_unsupported_operation"
    if "policy" in combined or "semantic_policy" in combined:
        return "semantic_policy_rejection"
    if "invalid_semantic_intent" in combined or "semantic_validation" in combined:
        return "semantic_validation_failure"
    if "unsupported_semantic" in combined or "unsupported_intent" in combined:
        return "unsupported_semantic_intent"
    if "provider" in combined or "unavailable" in combined:
        return "provider_failure_or_unavailable"
    if "malformed" in combined or "invalid_json" in combined:
        return "malformed_or_truncated_model_output"
    return "internal_implementation_error"


def evaluated_case_ids(rows, terminals):
    terminal_ids = {row.get("caseId") for row in terminals.values()}
    unmatched_ids = {
        row.get("caseId") for row in rows
        if row.get("eventType") == "attempt_started" and row.get("attemptId") not in terminals
    }
    return terminal_ids | unmatched_ids, unmatched_ids


def concept_list(actual):
    if not isinstance(actual, list):
        return None
    values = [item.get("conceptId") if isinstance(item, dict) else item for item in actual]
    return sorted(str(value).casefold() for value in values if value is not None)


def measure_qualifiers(actual):
    if not isinstance(actual, list):
        return None
    return sorted(
        (str(item.get("conceptId") or "").casefold(),
         str((item.get("qualifiers") or {}).get("valueForm", "")).casefold(),
         str((item.get("qualifiers") or {}).get("reportingBasis", "")).casefold(),
         str((item.get("qualifiers") or {}).get("statementScope", "")).casefold(),
         str((item.get("qualifiers") or {}).get("valueGrain", "")).casefold())
        for item in actual if isinstance(item, dict)
    )


def measure_resolutions(actual):
    if not isinstance(actual, list):
        return None
    return sorted((str(item.get("userPhrase", "")).casefold(),
                   str(item.get("canonicalizationStatus", "")).casefold(),
                   tuple(sorted(str(value).casefold() for value in item.get("candidateConceptIds", []))))
                  for item in actual if isinstance(item, dict))


def measure_units(actual, field):
    if not isinstance(actual, list):
        return None
    if field == "unitDimensions":
        key = "dimension"
    elif field == "requestedUnits":
        key = "requestedUnit"
    else:
        key = "effectiveSourceUnit"
    if field == "effectiveSourceUnits":
        return sorted((str(item.get("conceptId") or "").casefold(),
                       str(item.get(key) or "null").casefold(),
                       str(item.get("effectiveSourceUnitEvidence", "")).casefold())
                      for item in actual if isinstance(item, dict))
    return sorted((str(item.get("conceptId") or "").casefold(),
                   str(item.get(key) or "null").casefold())
                  for item in actual if isinstance(item, dict))


def flatten_filters(expression):
    if not isinstance(expression, dict):
        return []
    if expression.get("kind") == "group":
        return [item for child in expression.get("children", []) for item in flatten_filters(child)]
    return [expression]


def normalize_period(period):
    if not isinstance(period, dict):
        return None
    kind = enum_name(period.get("kind"), "period")
    granularity = enum_name(period.get("granularity"), "granularity")
    if kind == "relativePeriods":
        count = period.get("numberOfPeriods")
        return f"relativePeriods:{granularity}:{count}" if count is not None else "relativePeriods"
    if kind in ("explicitMonth", "explicitQuarter", "explicitFiscalYear", "dateRange"):
        label = period.get("userPeriodLabel") or period.get("startLabel")
        return f"{kind}:{normalize_period_label(label)}" if label else kind
    return kind


def normalize_period_label(label):
    """Compare period meaning while ignoring common Persian date-label wrappers."""
    if not isinstance(label, str):
        return label
    value = unicodedata.normalize("NFKC", label).translate(str.maketrans("۰۱۲۳۴۵۶۷۸۹٠١٢٣٤٥٦٧٨٩", "01234567890123456789"))
    value = value.replace("\u200c", " ").replace("\u200f", " ").replace("\u200e", " ").casefold()
    value = re.sub(r"^(?:سال\s+مالی|پایان\s+سال\s+مالی|پایان)\s+", "", value).strip()
    return re.sub(r"\s+", " ", value)


def normalize_thresholds(intent):
    filters = flatten_filters(intent.get("filters"))
    return sorted(
        (item.get("measureConcept"), enum_name((item.get("threshold") or {}).get("operator"), "thresholdOperator"),
         (item.get("threshold") or {}).get("value"), enum_name((item.get("threshold") or {}).get("unit"), "unit"))
        for item in filters if item.get("kind") == "measureThreshold"
    )


def actual_value(field, intent):
    if not isinstance(intent, dict):
        return None
    if field == "measureConcepts":
        return concept_list(intent.get("measures"))
    if field == "measureQualifiers":
        return measure_qualifiers(intent.get("measures"))
    if field == "measureResolution":
        return measure_resolutions(intent.get("measures"))
    if field in ("unitDimensions", "requestedUnits", "effectiveSourceUnits"):
        return measure_units(intent.get("measures"), field)
    if field == "operation":
        return enum_name((intent.get("operation") or {}).get("kind"), "operation")
    if field == "comparison":
        return enum_name((intent.get("comparison") or {}).get("mode"), "comparison")
    if field == "period":
        return normalize_period(intent.get("period"))
    if field == "entityScope":
        return enum_name((intent.get("entityScope") or {}).get("kind"), "entityScope")
    if field == "productLabels":
        return sorted(str(item.get("userLabel", "")).casefold() for item in intent.get("products", []) if isinstance(item, dict))
    if field == "categoryLabels":
        return sorted(normalize_category_label(str(item.get("userLabel", ""))) for item in intent.get("categories", []) if isinstance(item, dict))
    if field == "thresholds":
        return normalize_thresholds(intent)
    if field == "aggregation":
        value = intent.get("aggregation")
        return f"{value.get('function')}:{value.get('scope')}" if isinstance(value, dict) else None
    if field == "ranking":
        value = intent.get("ranking")
        return f"{value.get('direction')}:{value.get('measureConcept')}" if isinstance(value, dict) else None
    if field == "output":
        return enum_name((intent.get("output") or {}).get("kind"), "output")
    if field == "requiresClarification":
        return (intent.get("ambiguity") or {}).get("requiresClarification")
    if field == "unit":
        measures = intent.get("measures") or []
        return enum_name(measures[0].get("unit"), "unit") if measures and isinstance(measures[0], dict) else None
    return None


def expected_value(field, expected):
    value = expected.get(field)
    if field in ("measureConcepts", "productLabels", "categoryLabels") and isinstance(value, list):
        normalize = normalize_category_label if field == "categoryLabels" else lambda item: str(item).casefold()
        return sorted(normalize(item) for item in value)
    if field == "measureQualifiers" and isinstance(value, list):
        return sorted((str(item.get("conceptId", "")).casefold(), str(item.get("valueForm", "")).casefold(),
                       str(item.get("reportingBasis", "")).casefold(), str(item.get("statementScope", "")).casefold(),
                       str(item.get("valueGrain", "")).casefold()) for item in value)
    if field == "measureResolution" and isinstance(value, list):
        return sorted((str(item.get("userPhrase", "")).casefold(), str(item.get("status", "")).casefold(),
                       tuple(sorted(str(candidate).casefold() for candidate in item.get("candidateConceptIds", [])))) for item in value)
    if field == "period" and isinstance(value, str):
        kind, separator, label = value.partition(":")
        if separator and kind in ("explicitMonth", "explicitQuarter", "explicitFiscalYear", "dateRange"):
            return f"{kind.casefold()}:{normalize_period_label(label)}"
    if field in ("unitDimensions", "requestedUnits", "effectiveSourceUnits") and isinstance(value, list):
        if field == "effectiveSourceUnits":
            return sorted((str(item.get("conceptId", "")).casefold(),
                           ("null" if item.get("value") is None else str(item.get("value")).casefold()),
                           str(item.get("evidence", "")).casefold()) for item in value)
        return sorted((str(item.get("conceptId", "")).casefold(),
                       ("null" if item.get("value") is None else str(item.get("value")).casefold()))
                      for item in value)
    if field == "thresholds" and isinstance(value, list):
        return sorted((item.get("measureConcept"), item.get("operator"), item.get("value"), item.get("unit")) for item in value)
    if isinstance(value, str):
        return value.casefold()
    return value


def normalize_category_label(label):
    """Ignore the generic Persian industry prefix when comparing a category label."""
    value = unicodedata.normalize("NFKC", label).replace("\u200c", " ").casefold().strip()
    value = re.sub(r"^صنعت\s+", "", value)
    return re.sub(r"\s+", " ", value)


def legacy_value(field, legacy):
    raw = (legacy or {}).get("extractedIntent") or {}
    if field == "measureConcepts":
        values = [LEGACY_MEASURE_MAP.get(str(item).casefold()) for item in raw.get("measureIds", [])]
        return sorted(value for value in values if value is not None)
    if field in ("measureResolution", "measureQualifiers", "unitDimensions", "requestedUnits", "effectiveSourceUnits"):
        return None
    if field == "operation":
        raw_operation = str(raw.get("operation") or "").casefold()
        operation_map = {"directvalue": "retrieve", "directvalueoperation": "retrieve", "timeSeries".casefold(): "timeseries",
                         "growthpercentage": "growthrate", "absolutechange": "absolutechange", "unsupportedderived": ""}
        return operation_map.get(raw_operation)
    if field == "comparison":
        raw_comparison = str(raw.get("comparison") or "").casefold()
        comparison_map = {"none": "none", "monthovermonth": "monthovermonth", "yearoveryear": "yearoveryear",
                          "multicompanycomparison": "multicompany"}
        return comparison_map.get(raw_comparison)
    if field == "period":
        count = raw.get("maximumPeriods")
        if count:
            return f"relativeperiods:month:{count}"
        policies = " ".join(raw.get("periodPolicies") or []).casefold()
        return "latestavailable" if "latest" in policies else None
    if field == "entityScope":
        references = raw.get("entityReferences") or []
        if raw.get("populationDiscoveryRequested") or not references:
            return "unboundcompanypopulation"
        return "namedcompanies"
    if field == "productLabels":
        return sorted(str(item).casefold() for item in raw.get("productReferences", []) if item)
    if field == "requiresClarification":
        return raw.get("hasUnknownOrAmbiguousRequirements")
    return None


def score_field(expected, actual, field):
    exp = expected_value(field, expected)
    got = actual_value(field, actual)
    if isinstance(exp, list):
        got = sorted(value.casefold() if isinstance(value, str) else value for value in (got or []))
    elif isinstance(exp, str) and isinstance(got, str):
        got = got.casefold()
    return exp == got, exp, got


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--benchmark", default=str(Path(__file__).with_name("canonical-intent-benchmark-v3.json")))
    parser.add_argument("--shadow", required=True, help="JSONL produced by evaluate-canonical-intent")
    parser.add_argument("--output", required=True)
    args = parser.parse_args()
    benchmark = json.loads(Path(args.benchmark).read_text(encoding="utf-8"))
    cases = {row["id"]: row for row in benchmark["cases"]}
    rows = read_jsonl(args.shadow)
    terminals = {}
    for row in rows:
        if row.get("eventType") == "attempt_started":
            pass
        elif row.get("eventType") == "attempt_terminal":
            terminals[row.get("attemptId")] = row

    details = []
    metric_totals = Counter()
    conditional_metric_totals = Counter()
    legacy_metric_totals = Counter()
    option_categories = Counter()
    evaluated_ids, unmatched_by_case = evaluated_case_ids(rows, terminals)
    for case_id in sorted(evaluated_ids):
        if case_id not in cases:
            continue
        case = cases[case_id]
        terminal = next((row for row in terminals.values() if row.get("caseId") == case_id), None)
        expected = case.get("expected", {})
        expected_fields = case.get("expectedFields", [])
        option = terminal.get("optionB", {}) if terminal else None
        intent = option.get("intent") if option else None
        category = classify(option) if option else ("unmatched_attempt_reconciled_as_canceled" if case_id in unmatched_by_case else "internal_implementation_error")
        option_categories[category] += 1
        per_field = {}
        for field in expected_fields:
            correct, exp, got = score_field(expected, intent, field)
            metric_totals[field + ":denominator"] += 1
            metric_totals[field + (":correct" if correct else ":incorrect")] += 1
            legacy_got = legacy_value(field, terminal.get("legacy") if terminal else None)
            if isinstance(exp, list):
                legacy_got = sorted(value.casefold() if isinstance(value, str) else value for value in (legacy_got or []))
            elif isinstance(exp, str) and isinstance(legacy_got, str):
                legacy_got = legacy_got.casefold()
            legacy_correct = exp == legacy_got
            legacy_metric_totals[field + ":denominator"] += 1
            legacy_metric_totals[field + (":correct" if legacy_correct else ":incorrect")] += 1
            if option and option.get("status") == "completed" and isinstance(intent, dict):
                conditional_metric_totals[field + ":denominator"] += 1
                conditional_metric_totals[field + (":correct" if correct else ":incorrect")] += 1
            per_field[field] = {"legacyCorrect": legacy_correct, "legacyActual": legacy_got,
                                "optionBCorrect": correct, "expected": exp, "optionBActual": got}
        details.append({
            "caseId": case_id,
            "question": case["question"],
            "goldStatus": case.get("goldStatus"),
            "catalogStatus": case.get("catalogStatus"),
            "expectedFields": expected_fields,
            "optionBFailureCategory": category,
            "expectedOptionBFailureCategory": case.get("expectedOptionBFailureCategory"),
            "unsupportedClassificationCorrect": category == case.get("expectedOptionBFailureCategory")
                if case.get("expectedOptionBFailureCategory") else None,
            "optionBValidIntent": bool(option and option.get("status") == "completed" and isinstance(intent, dict)),
            "fieldScores": per_field,
            "legacy": terminal.get("legacy") if terminal else None,
            "optionB": option,
        })

    total = len(evaluated_ids)
    valid_count = sum(item["optionBValidIntent"] for item in details)
    unsupported_gold = [item for item in details if item["expectedOptionBFailureCategory"] == "unsupported_semantic_intent"]
    reviewed = {"adjudicated_unambiguous", "corrected_unambiguous", "genuinely_ambiguous", "genuinely_unsupported", "catalog_blocked_semantically_valid"}
    reviewed_case_count = sum(cases[case_id].get("goldStatus") in reviewed for case_id in evaluated_ids if case_id in cases)
    field_metrics = {}
    conditional_field_metrics = {}
    for field in sorted({field for case_id in evaluated_ids if case_id in cases for field in cases[case_id].get("expectedFields", [])}):
        denominator = metric_totals[field + ":denominator"]
        correct = metric_totals[field + ":correct"]
        field_metrics[field] = {"correct": correct, "denominator": denominator, "accuracy": correct / denominator if denominator else None}
        legacy_denominator = legacy_metric_totals[field + ":denominator"]
        legacy_correct = legacy_metric_totals[field + ":correct"]
        field_metrics[field]["legacyCorrect"] = legacy_correct
        field_metrics[field]["legacyAccuracy"] = legacy_correct / legacy_denominator if legacy_denominator else None
        conditional_denominator = conditional_metric_totals[field + ":denominator"]
        conditional_correct = conditional_metric_totals[field + ":correct"]
        conditional_field_metrics[field] = {"correct": conditional_correct, "denominator": conditional_denominator,
                                            "accuracy": conditional_correct / conditional_denominator if conditional_denominator else None}
    scorecard = {
        "benchmarkVersion": benchmark["benchmarkVersion"],
        "shadowPathOnly": True,
        "casesInBenchmark": len(cases),
        "casesEvaluated": total,
        "casesNotRun": len(set(cases) - evaluated_ids),
        "casesWithAdjudicatedGold": reviewed_case_count,
        "casesWithDisputedGold": sum(case.get("goldStatus") == "disputed" for case in cases.values()),
        "casesWithOnlyInheritedMeasureLabels": sum(case.get("goldStatus") == "inherited_measure_only_unreviewed" for case in cases.values()),
        "casesAwaitingGoldAdjudication": sum(case.get("goldStatus") in ("gold_adjudication_pending", "inherited_measure_only_unreviewed") for case in cases.values()),
        "unconditionalValidIntent": {"correct": valid_count, "denominator": total, "rate": valid_count / total if total else None},
        "unconditionalFieldMetrics": field_metrics,
        "conditionalFieldMetrics": conditional_field_metrics,
        "metricDenominators": {field: metric_totals[field + ":denominator"] for field in sorted({key.split(":")[0] for key in metric_totals if key.endswith(":denominator")})},
        "failureCategories": {category: option_categories.get(category, 0) for category in FAILURE_CATEGORIES},
        "unsupportedIntentClassification": {
            "correct": sum(item["unsupportedClassificationCorrect"] is True for item in unsupported_gold),
            "denominator": len(unsupported_gold),
            "accuracy": (sum(item["unsupportedClassificationCorrect"] is True for item in unsupported_gold) / len(unsupported_gold)) if unsupported_gold else None,
        },
        "unmatchedStartedAttempts": len(unmatched_by_case),
        "optionBTokenTotals": {
            "input": sum((row.get("optionB") or {}).get("inputTokens") or 0 for row in terminals.values()),
            "output": sum((row.get("optionB") or {}).get("outputTokens") or 0 for row in terminals.values()),
            "reportedCases": sum((row.get("optionB") or {}).get("inputTokens") is not None for row in terminals.values()),
        },
        "optionBLatencyMs": {
            "mean": (sum((row.get("optionB") or {}).get("latencyMs") or 0 for row in terminals.values()) / len(terminals)) if terminals else None,
            "max": max(((row.get("optionB") or {}).get("latencyMs") or 0 for row in terminals.values()), default=None),
        },
        "perCase": details,
    }
    Path(args.output).write_text(json.dumps(scorecard, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({key: scorecard[key] for key in ("casesInBenchmark", "casesWithAdjudicatedGold", "casesWithDisputedGold", "casesAwaitingGoldAdjudication", "unconditionalValidIntent", "unconditionalFieldMetrics", "conditionalFieldMetrics", "failureCategories", "unsupportedIntentClassification", "unmatchedStartedAttempts")}, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
