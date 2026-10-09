import json
import pathlib
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[3]
FEATURE = ROOT / "specs/141-schema-aware-semantic-query-engine"
CATALOG = FEATURE / "SemanticCatalog"
FIXTURE = FEATURE / "artifacts/monthly-production-sales-evaluation.json"


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8"))


class Feature141MonthlyEvaluationTests(unittest.TestCase):
    def test_thirteen_cases_resolve_to_monthly_catalog_metadata_and_stay_unauthorized(self):
        fixture = read_json(FIXTURE)
        navigation = read_json(CATALOG / "navigation.yaml")
        monthly_domain = next(x for x in navigation["domains"] if x["nodeId"] == "domain-monthly-activity")
        cases = fixture["cases"]

        self.assertEqual(13, len(cases))
        self.assertEqual(13, len({x["id"] for x in cases}))
        self.assertFalse(fixture["runtimeExecutionEnabled"])
        self.assertFalse(fixture["actualPostgresQueried"])

        for case in cases:
            with self.subTest(case=case["id"]):
                monthly_tables = set(case["expectedTables"]) & {
                    "MonthlyReports", "MonthlyReportLineItems", "CompanyMonthlyActivityTrendSnapshots"
                }
                monthly_metrics = {x for x in case["expectedMetrics"] if x.startswith("metric_monthly_")}
                self.assertTrue(monthly_tables)
                self.assertTrue(monthly_tables.issubset(monthly_domain["tables"]))
                self.assertTrue(monthly_metrics.issubset(monthly_domain["metricIds"]))
                self.assertTrue(case["compilerStatus"].startswith("unsupported:"))

        for table_name in {name for case in cases for name in case["expectedTables"]
                           if name in {"MonthlyReports", "MonthlyReportLineItems", "CompanyMonthlyActivityTrendSnapshots"}}:
            table_id = next(x for x in read_json(CATALOG / "catalog.yaml")["tables"]
                            if read_json(CATALOG / "tables" / f"{x}.yaml")["physical"]["Name"] == table_name)
            table = read_json(CATALOG / "tables" / f"{table_id}.yaml")
            self.assertFalse(table["semantic"]["queryable"])
            self.assertFalse(table["semantic"]["queryAuthorization"]["runtimeQueryable"])

    def test_fixture_covers_ten_postgres_scenarios_without_claiming_execution(self):
        scenarios = read_json(FIXTURE)["essentialScenarios"]
        self.assertEqual(10, len(scenarios))
        self.assertEqual(10, len({x["id"] for x in scenarios}))
        self.assertTrue(all(x["status"] == "fixture-specified-only" and x["database"] == "none"
                            for x in scenarios))


if __name__ == "__main__":
    unittest.main()
