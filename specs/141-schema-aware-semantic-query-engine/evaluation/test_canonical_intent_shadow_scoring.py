import importlib.util
import json
import unittest
from pathlib import Path


HERE = Path(__file__).resolve().parent
SCORE_SPEC = importlib.util.spec_from_file_location("canonical_intent_shadow_score", HERE / "score_canonical_intent_shadow.py")
SCORE = importlib.util.module_from_spec(SCORE_SPEC)
SCORE_SPEC.loader.exec_module(SCORE)
BUILD_SPEC = importlib.util.spec_from_file_location("canonical_intent_benchmark_v3_build", HERE / "build_canonical_intent_benchmark_v3.py")
BUILD = importlib.util.module_from_spec(BUILD_SPEC)
BUILD_SPEC.loader.exec_module(BUILD)
HOLDOUT_SPEC = importlib.util.spec_from_file_location("canonical_intent_holdout_build", HERE / "build_canonical_intent_independent_holdout.py")
HOLDOUT = importlib.util.module_from_spec(HOLDOUT_SPEC)
HOLDOUT_SPEC.loader.exec_module(HOLDOUT)
PHASE16_HOLDOUT_SPEC = importlib.util.spec_from_file_location("canonical_intent_phase16_holdout_build", HERE / "build_canonical_intent_phase1_6_holdout.py")
PHASE16_HOLDOUT = importlib.util.module_from_spec(PHASE16_HOLDOUT_SPEC)
PHASE16_HOLDOUT_SPEC.loader.exec_module(PHASE16_HOLDOUT)


class CanonicalIntentShadowScoringTests(unittest.TestCase):
    def test_failure_taxonomy_keeps_output_schema_semantics_policy_and_authorization_separate(self):
        examples = [
            ({"status": "failed", "failureCategory": "malformed_model_output", "diagnostic": "structured_output_invalid"}, "malformed_or_truncated_model_output"),
            ({"status": "failed", "failureCategory": "structured_schema_validation_failure"}, "structured_schema_validation_failure"),
            ({"status": "failed", "failureCategory": "invalid_semantic_intent"}, "semantic_validation_failure"),
            ({"status": "failed", "failureCategory": "semantic_policy_rejection"}, "semantic_policy_rejection"),
            ({"status": "failed", "failureCategory": "query_authorization_rejection"}, "query_authorization_rejection"),
            ({"status": "failed", "failureCategory": "provider_timeout"}, "provider_timeout_or_cancellation"),
        ]
        for result, expected in examples:
            with self.subTest(expected=expected):
                self.assertEqual(expected, SCORE.classify(result))

    def test_diagnostic_taxonomy_distinguishes_provider_from_request_response_and_semantic_failures(self):
        examples = [
            ({"status": "failed", "failureCategory": "request_serialization_failure"}, "request_serialization_failure"),
            ({"status": "failed", "failureCategory": "response_deserialization_failure"}, "response_deserialization_failure"),
            ({"status": "failed", "failureCategory": "invalid_request_schema"}, "invalid_request_schema"),
            ({"status": "failed", "failureCategory": "provider_authentication_failure"}, "provider_authentication_failure"),
            ({"status": "failed", "failureCategory": "provider_quota_failure"}, "provider_quota_failure"),
            ({"status": "failed", "failureCategory": "provider_transport_failure"}, "provider_transport_failure"),
            ({"status": "failed", "failureCategory": "invalid_semantic_intent"}, "semantic_validation_failure"),
            ({"status": "failed", "failureCategory": "unsupported_semantic_intent"}, "unsupported_semantic_intent"),
        ]
        for result, expected in examples:
            with self.subTest(expected=expected):
                self.assertEqual(expected, SCORE.classify(result))

    def test_v3_benchmark_combines_fifty_original_cases_and_historical_holdout_without_mutation(self):
        root = HERE.parents[2]
        benchmark = BUILD.build(root)

        self.assertEqual(60, benchmark["caseCount"])
        self.assertEqual("previously_used", benchmark["cases"][50]["historicalEvaluationStatus"])
        cases = {row["id"]: row for row in benchmark["cases"]}
        self.assertEqual(["salesQuantity"], cases["readiness-dev-18"]["expected"]["measureConcepts"])
        self.assertEqual(["productionQuantity", "salesQuantity"], cases["readiness-holdout-v3-10"]["expected"]["measureConcepts"])
        self.assertEqual("genuinely_ambiguous", cases["readiness-holdout-v3-10"]["goldStatus"])
        self.assertEqual([], cases["readiness-dev-01"]["expectedFields"])
        self.assertEqual(30, benchmark["adjudicatedCaseCount"])
        self.assertEqual("disputed", cases["fa-value-02"]["goldStatus"])

    def test_independent_holdout_is_frozen_and_has_separate_ambiguous_unsupported_denominators(self):
        source = HERE / "canonical-intent-independent-holdout-v1.source.json"
        benchmark = HOLDOUT.build(source)
        self.assertEqual(12, benchmark["caseCount"])
        self.assertEqual(9, benchmark["adjudicatedCaseCount"])
        self.assertEqual(1, benchmark["ambiguousCaseCount"])
        self.assertEqual(2, benchmark["unsupportedCaseCount"])
        cases = {row["id"]: row for row in benchmark["cases"]}
        self.assertIn("measureResolution", cases["independent-07"]["expectedFields"])
        self.assertEqual([], cases["independent-08"]["expectedFields"])
        self.assertEqual("billionIRR", cases["independent-11"]["expected"]["requestedUnits"][0]["value"])

    def test_phase16_holdout_is_separately_authored_and_frozen_before_first_pass(self):
        source = HERE / "canonical-intent-phase1_6-independent-holdout.source.json"
        benchmark = PHASE16_HOLDOUT.build(source)

        self.assertEqual(12, benchmark["caseCount"])
        self.assertEqual(10, benchmark["adjudicatedCaseCount"])
        self.assertEqual(1, benchmark["ambiguousCaseCount"])
        self.assertEqual(1, benchmark["unsupportedCaseCount"])
        self.assertEqual("phase1_6_independent_holdout_v2", benchmark["cases"][0]["split"])
        self.assertEqual("never_evaluated_before_freeze", benchmark["cases"][0]["historicalEvaluationStatus"])
        self.assertIn("p16-06", {case["id"] for case in benchmark["cases"]})
        historic_paths = [HERE / "canonical-intent-benchmark-v3.json", HERE / "canonical-intent-independent-holdout-v1.json"]
        historic_questions = {case["question"] for path in historic_paths for case in json.loads(path.read_text(encoding="utf-8"))["cases"]}
        self.assertFalse(historic_questions.intersection(case["question"] for case in benchmark["cases"]))

    def test_unrun_benchmark_cases_are_not_scored_as_failed(self):
        benchmark = json.loads((HERE / "canonical-intent-benchmark-v2.json").read_text(encoding="utf-8"))
        terminal = {"attempt-1": {"caseId": "fa-02"}}
        evaluated, unmatched = SCORE.evaluated_case_ids([], terminal)
        self.assertEqual({"fa-02"}, evaluated)
        self.assertEqual(set(), unmatched)
        self.assertEqual(60, benchmark["caseCount"])

        evaluated, unmatched = SCORE.evaluated_case_ids([{"eventType": "attempt_started", "caseId": "fa-03", "attemptId": "attempt-2"}], terminal)
        self.assertEqual({"fa-02", "fa-03"}, evaluated)
        self.assertEqual({"fa-03"}, unmatched)
        self.assertIn("unmatched_attempt_reconciled_as_canceled", SCORE.FAILURE_CATEGORIES)

    def test_numeric_enum_values_from_early_shadow_rows_are_normalized(self):
        intent = {
            "operation": {"kind": 0},
            "comparison": {"mode": 2},
            "period": {"kind": 5, "granularity": 1, "numberOfPeriods": 3},
            "entityScope": {"kind": 1},
            "output": {"kind": 3},
            "measures": [{"unit": 5}],
            "filters": {"kind": "measureThreshold", "threshold": {"operator": 3, "unit": 6, "value": 5}},
        }

        self.assertEqual("retrieve", SCORE.actual_value("operation", intent))
        self.assertEqual("yearOverYear", SCORE.actual_value("comparison", intent))
        self.assertEqual("relativePeriods:month:3", SCORE.actual_value("period", intent))
        self.assertEqual("unboundCompanyPopulation", SCORE.actual_value("entityScope", intent))
        self.assertEqual("rankedList", SCORE.actual_value("output", intent))
        self.assertEqual("irrPerShare", SCORE.actual_value("unit", intent))
        self.assertEqual([(None, "lessThan", 5, "percent")], SCORE.normalize_thresholds(intent))

    def test_display_units_dimension_and_effective_source_evidence_are_scored_independently(self):
        intent = {
            "measures": [{
                "conceptId": "salesAmount",
                "dimension": "currencyAmount",
                "requestedUnit": "percent",
                "effectiveSourceUnit": None,
                "effectiveSourceUnitEvidence": "unresolved",
            }]
        }
        expected = {
            "unitDimensions": [{"conceptId": "salesAmount", "value": "currencyAmount"}],
            "requestedUnits": [{"conceptId": "salesAmount", "value": "percent"}],
            "effectiveSourceUnits": [{"conceptId": "salesAmount", "value": None, "evidence": "unresolved"}],
        }

        for field in ("unitDimensions", "requestedUnits", "effectiveSourceUnits"):
            with self.subTest(field=field):
                self.assertEqual(SCORE.expected_value(field, expected), SCORE.actual_value(field, intent))

        intent["measures"][0]["effectiveSourceUnitEvidence"] = "ownerApproved"
        self.assertNotEqual(SCORE.expected_value("effectiveSourceUnits", expected),
                            SCORE.actual_value("effectiveSourceUnits", intent))

    def test_period_scoring_ignores_only_common_persian_label_wrappers_and_digit_forms(self):
        self.assertEqual("explicitFiscalYear:1404", SCORE.normalize_period({
            "kind": "explicitFiscalYear", "userPeriodLabel": "سال مالی ۱۴۰۴"
        }))
        self.assertEqual("explicitMonth:خرداد 1405", SCORE.normalize_period({
            "kind": "explicitMonth", "userPeriodLabel": "پایان خرداد ۱۴۰۵"
        }))
        self.assertEqual("explicitmonth:خرداد 1405", SCORE.expected_value("period", {
            "period": "explicitMonth:خرداد ۱۴۰۵"
        }))

    def test_category_scoring_ignores_generic_industry_prefix(self):
        intent = {"categories": [{"userLabel": "صنعت معدن"}]}
        expected = {"categoryLabels": ["معدن"]}

        self.assertEqual(SCORE.expected_value("categoryLabels", expected),
                         SCORE.actual_value("categoryLabels", intent))


if __name__ == "__main__":
    unittest.main()
