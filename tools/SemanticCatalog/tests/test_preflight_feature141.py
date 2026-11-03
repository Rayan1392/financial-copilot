import json
import subprocess
import sys
import unittest
from pathlib import Path


ROOT = Path(__file__).resolve().parents[3]
SCRIPT = ROOT / "tools/SemanticCatalog/preflight_feature141.py"


class Feature141PreflightTests(unittest.TestCase):
    def test_preflight_validates_catalog_without_database_access(self):
        result = subprocess.run(
            [sys.executable, str(SCRIPT), "--json"], cwd=ROOT,
            capture_output=True, text=True, check=True,
        )
        report = json.loads(result.stdout)
        self.assertTrue(report["catalog"]["validated"])
        self.assertEqual(64, len(report["catalog"]["canonicalHash"]))
        self.assertFalse(report["databaseCallsMade"])
        self.assertFalse(report["financialQueriesRun"])
        self.assertEqual("approved", report["targetSourceItem"]["semanticApproval"])
        self.assertEqual("not-authorized", report["targetSourceItem"]["queryAuthorization"])
        self.assertFalse(report["overallExecutionEligible"])
        self.assertIn("ReviewedTenantCompanyBindings", report["configurationKeysRequired"][-2])

    def test_preflight_output_does_not_include_configuration_values(self):
        result = subprocess.run(
            [sys.executable, str(SCRIPT), "--json"], cwd=ROOT,
            capture_output=True, text=True, check=True,
        )
        self.assertNotIn("ConnectionString\" :", result.stdout)
        self.assertTrue(json.loads(result.stdout)["configurationKeyPresenceOnly"])


if __name__ == "__main__":
    unittest.main()
