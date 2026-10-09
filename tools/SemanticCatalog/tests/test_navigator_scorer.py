import importlib.util
import pathlib
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[3]
SCORER_PATH = ROOT / "specs/141-schema-aware-semantic-query-engine/evaluation/run_navigation_evaluation.py"
SPEC = importlib.util.spec_from_file_location("navigator_scorer", SCORER_PATH)
SCORER = importlib.util.module_from_spec(SPEC)
assert SPEC.loader is not None
SPEC.loader.exec_module(SCORER)


class NavigatorScorerTests(unittest.TestCase):
    def test_set_scores_include_precision_recall_and_f1_without_weakening_domain_exactness(self):
        case = {"id": "multi-domain", "domains": ["monthly-activity", "financial-statements"],
                "nodeIds": ["domain-monthly-activity", "domain-financial-statements"]}
        prediction = {"caseId": "multi-domain", "selectedNodeIds": [
            "domain-monthly-activity", "domain-financial-statements", "domain-market-data"]}

        result = SCORER.evaluate([case], {"multi-domain": prediction})
        scored = result["perCase"][0]

        self.assertEqual(1.0, scored["domainAccuracy"]["recall"])
        self.assertAlmostEqual(2 / 3, scored["domainAccuracy"]["precision"])
        self.assertAlmostEqual(0.8, scored["domainAccuracy"]["f1"])
        self.assertEqual(0.0, scored["domainAccuracy"]["accuracy"])
        self.assertEqual(0.0, result["aggregate"]["domainAccuracy"])
        self.assertAlmostEqual(2 / 3, result["aggregate"]["domainPrecision"])
        self.assertEqual("exact-set-match-of-catalog-domain-node-ids", scored["domainAccuracy"]["contract"])

    def test_accepts_exact_unmapped_source_item_161_reference(self):
        reference = {
            "referenceType": "DirectSourceItem",
            "catalogId": "source_item_682beceadddc1458",
            "sourceItemId": 161,
            "metricCode": None,
        }
        case = {
            "id": "fa-value-01",
            "expectedFinancialValueReferences": [reference],
            "expectedFinancialValueOperation": "DirectValueRetrieval",
            "expectedOutcome": "unsupported",
        }
        prediction = {
            "caseId": "fa-value-01",
            "financialValueReferences": [reference],
            "financialValueOperation": "DirectValueRetrieval",
            "independentRequirements": {"financialValueReferences": [reference]},
            "outcome": "unsupported",
            "isComplete": False,
        }

        result = SCORER.evaluate([case], {"fa-value-01": prediction})
        scored = result["perCase"][0]
        self.assertEqual(1, result["scoredCaseCount"])
        self.assertEqual(1.0, scored["financialReferenceAccuracy"])
        self.assertEqual(1.0, scored["sourceItemReferenceAccuracy"])
        self.assertEqual(1.0, scored["financialOperationAccuracy"])

    def test_rejects_metric_substitution_for_source_item_161(self):
        case = {
            "id": "fa-value-01",
            "expectedFinancialValueReferences": [{
                "referenceType": "DirectSourceItem",
                "catalogId": "source_item_682beceadddc1458",
                "metricCode": None,
            }],
            "expectedFinancialValueOperation": "DirectValueRetrieval",
        }
        prediction = {
            "financialValueReferences": [{
                "referenceType": "GovernedMetric",
                "catalogId": "metric_operating_profit",
                "metricCode": "OPERATING_PROFIT",
            }],
            "independentRequirements": {"financialValueReferences": [{
                "referenceType": "DirectSourceItem",
                "catalogId": "source_item_682beceadddc1458",
            }]},
            "financialValueOperation": "DirectValueRetrieval",
        }

        result = SCORER.evaluate([case], {"fa-value-01": prediction})
        scored = result["perCase"][0]
        self.assertEqual(0.0, scored["financialReferenceAccuracy"])
        self.assertEqual(0.0, scored["sourceItemReferenceAccuracy"])
        self.assertIn("financial_reference_navigation_error", scored["failureClassification"])

    def test_scores_structured_is_composing_scope_without_hashing_a_dict(self):
        case = {"id": "standalone-scope", "statementScope": {
            "field": "IsComposing", "value": False, "provenance": "business-owner-confirmed"}}
        prediction = {"caseId": "standalone-scope", "statementScopes": ["standalone"]}

        result = SCORER.evaluate([case], {"standalone-scope": prediction})

        self.assertEqual(1.0, result["perCase"][0]["statementScopeAccuracy"])

    def test_missing_prediction_is_unscored_not_a_quality_failure_or_pass(self):
        result = SCORER.evaluate([{"id": "fa-value-01"}], {})
        self.assertEqual(0, result["scoredCaseCount"])
        self.assertEqual(["fa-value-01"], result["unscoredCaseIds"])
        self.assertIsNone(result["aggregate"]["financialReferenceAccuracy"])


if __name__ == "__main__":
    unittest.main()
