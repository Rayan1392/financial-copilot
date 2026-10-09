#!/usr/bin/env python3
"""Read-only Feature 141 first-query readiness check; never connects to PostgreSQL."""

from __future__ import annotations

import argparse
import importlib.util
import json
import os
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
FEATURE = ROOT / "specs/141-schema-aware-semantic-query-engine"
CATALOG = FEATURE / "SemanticCatalog"
ITEM_ID = "source_item_682beceadddc1458"
REL_IDS = ["rel_cd8a77a2717ac71f", "rel_2588cf95d01a651a", "rel_e4fa0fe989156533"]
TABLE_IDS = ["tbl_36036602dbf76362", "tbl_4f2ef2d8dd59b46a", "tbl_53d51e26e1ca6929", "tbl_8646d83cdb350f92"]
REQUIRED_CONFIG = [
    "Enabled", "ExplicitDevelopmentOptIn", "EnvironmentName", "ConnectionString",
    "DesignatedDatabase", "ReadOnlyRole", "CommandTimeoutSeconds", "MaximumRows",
    "MaximumResultBytes", "MaximumConcurrency", "DevelopmentScopeId", "AuthorizationProvenance",
    "ReviewedSourceCatalogIds", "ReviewedRelationshipCatalogIds", "ReviewedPhysicalObjectCatalogIds",
    "ReviewedTenantCompanyBindings", "ReviewedOperations",
]
SECTION = "Feature141:QueryExecution"


def read_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def has_value(value):
    if value is None:
        return False
    if isinstance(value, list):
        return len(value) > 0
    return str(value).strip() != ""


def config_presence():
    # Inspect environment variable names only. Do not read environment values or query a
    # secret store: the preflight is useful before credentials have been authorized.
    environment_names = set(os.environ)
    names = set()
    for key in REQUIRED_CONFIG:
        prefix = f"Feature141__QueryExecution__{key}"
        if prefix in environment_names or any(name.startswith(prefix + "__") for name in environment_names):
            names.add(key)
    return names


def validate_catalog():
    result = subprocess.run(
        [sys.executable, str(ROOT / "tools/SemanticCatalog/catalog.py"), "validate"],
        cwd=ROOT, capture_output=True, text=True, check=False,
    )
    summary = (result.stdout + result.stderr).strip()
    return result.returncode == 0, summary[-1200:]


def source_hash():
    spec = importlib.util.spec_from_file_location("feature141_catalog", ROOT / "tools/SemanticCatalog/catalog.py")
    module = importlib.util.module_from_spec(spec)
    assert spec and spec.loader
    spec.loader.exec_module(module)
    return module.catalog_state()[1]


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--json", action="store_true", help="emit machine-readable report")
    args = parser.parse_args()

    catalog_ok, catalog_output = validate_catalog()
    manifest = read_json(CATALOG / "manifest.yaml")
    dictionary = read_json(CATALOG / "dictionaries/financial-statement-source-items.yaml")
    source = next((entry for entry in dictionary["entries"] if entry.get("catalogId") == ITEM_ID), None)
    source_semantic = bool(source and source.get("semanticReviewStatus") == "approved")
    source_auth = bool(source and source.get("queryAuthorization", {}).get("approved") is True
                       and source.get("queryAuthorization", {}).get("queryable") is True)
    table_status = {}
    for table_id in TABLE_IDS:
        table = read_json(CATALOG / f"tables/{table_id}.yaml")
        semantic = table.get("semantic", {})
        qa = semantic.get("queryAuthorization", {})
        table_status[table_id] = {
            "semanticApproved": semantic.get("semanticApproval", {}).get("approved") is True,
            "runtimeQueryable": qa.get("runtimeQueryable") is True,
            "approvedObjectCount": len(qa.get("approvedObjects", [])),
        }
    relationships = {}
    for rel_id in REL_IDS:
        rel = read_json(CATALOG / f"relationships/{rel_id}.yaml")
        semantic = rel.get("semantic", {})
        qa = semantic.get("queryAuthorization", {})
        relationships[rel_id] = {
            "semanticApproved": semantic.get("approved") is True,
            "runtimeJoinable": semantic.get("runtimeJoinable") is True,
            "queryable": qa.get("queryable") is True and qa.get("approved") is True,
        }

    present = config_presence()
    database_configured = "ConnectionString" in present and "DesignatedDatabase" in present and "ReadOnlyRole" in present
    pair_configured = "ReviewedTenantCompanyBindings" in present
    all_semantic = source_semantic and all(x["semanticApproved"] for x in table_status.values()) and all(x["semanticApproved"] for x in relationships.values())
    all_authorized = source_auth and all(x["runtimeQueryable"] and x["approvedObjectCount"] > 0 for x in table_status.values()) and all(x["queryable"] for x in relationships.values())
    # This deliberately does not connect, inspect grants, resolve a company UUID, or run a financial query.
    report = {
        "command": "python tools/SemanticCatalog/preflight_feature141.py",
        "databaseCallsMade": False,
        "financialQueriesRun": False,
        "catalog": {"validated": catalog_ok, "canonicalHash": source_hash(),
                    "hierarchyHash": manifest.get("navigationHierarchyHash"), "validationOutput": catalog_output},
        "targetSourceItem": {"catalogId": ITEM_ID, "found": source is not None,
                              "semanticApproval": "approved" if source_semantic else "pending",
                              "queryAuthorization": "approved" if source_auth else "not-authorized"},
        "requiredTables": table_status,
        "requiredRelationships": relationships,
        "databaseConnectionConfigured": database_configured,
        "secureStoreConfiguration": "uninspected; no credential values or secret-store entries read",
        "restrictedRolePrivileges": "unverified-not-queried",
        "tenantCompanyBindingConfigured": pair_configured,
        "companyResolver": {"ready": True, "source": "persisted Companies table via FinancialIngestionDbContext",
                            "newCompaniesDiscoverable": True, "tenantScopedByResolver": False,
                            "executionBoundary": "exact ReviewedTenantCompanyBindings plus resolved Company.Id filter"},
        "statementSelection": {"policy": "Latest reporting period end; preserve period start/type/scope",
                               "ties": "equal requested values deduplicate with all identities; conflicts Ambiguous; missing observations Incomplete",
                               "liveAmbiguityStatus": "unknown-not-queried"},
        "overallExecutionEligible": bool(catalog_ok and all_semantic and all_authorized and database_configured and pair_configured),
        "configurationKeysPresent": sorted(present),
        "configurationKeysRequired": [f"{SECTION}:{key}" for key in REQUIRED_CONFIG],
        "configurationKeyPresenceOnly": True,
    }
    if args.json:
        print(json.dumps(report, ensure_ascii=False, indent=2))
    else:
        print("Feature 141 Development preflight (read-only; no database connection/query)")
        print(f"Catalog validation: {'PASS' if catalog_ok else 'FAIL'}")
        print(f"Canonical hash: {report['catalog']['canonicalHash']}")
        print(f"Hierarchy hash: {report['catalog']['hierarchyHash']}")
        print(f"Item 161 semantic/query approval: {report['targetSourceItem']['semanticApproval']} / {report['targetSourceItem']['queryAuthorization']}")
        print(f"Dedicated DB config in environment: {'key names present' if database_configured else 'missing/incomplete'}")
        print("User Secrets/secure store: uninspected; no credentials read")
        print("Restricted-role grants: unverified (no DB call)")
        print(f"Exact tenant/company binding config: {'present' if pair_configured else 'missing'}")
        print("Company resolver: Companies DB-backed; dynamic additions supported; resolver itself is not tenant-scoped")
        print("Latest-period tie status: unknown (no data query); ties return Ambiguous")
        print(f"Execution eligible: {'YES' if report['overallExecutionEligible'] else 'NO'}")
    return 0 if catalog_ok else 2


if __name__ == "__main__":
    raise SystemExit(main())
