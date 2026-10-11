import hashlib
import importlib.util
import json
import pathlib
import subprocess
import sys
import tempfile
import unittest


ROOT = pathlib.Path(__file__).resolve().parents[3]
FEATURE = ROOT / "specs/141-schema-aware-semantic-query-engine"
CATALOG = FEATURE / "SemanticCatalog"
ARTIFACTS = FEATURE / "artifacts"


def load_json_yaml(path):
    # Catalog output intentionally uses JSON, a valid YAML 1.2 subset.
    return json.loads(path.read_text(encoding="utf-8"))


class CatalogArtifactTests(unittest.TestCase):
    def load_catalog_module(self):
        script = ROOT / "tools/SemanticCatalog/catalog.py"
        spec = importlib.util.spec_from_file_location("semantic_catalog_cli", script)
        module = importlib.util.module_from_spec(spec)
        sys.modules[spec.name] = module
        spec.loader.exec_module(module)
        return module

    def test_canonical_hash_ignores_json_yaml_formatting_and_covers_new_policy_files(self):
        module = self.load_catalog_module()
        with tempfile.TemporaryDirectory() as temporary:
            catalog_root = pathlib.Path(temporary)
            module.CATALOG = catalog_root
            for name in ("catalog.yaml", "catalog-scope.yaml", "manifest.yaml", "navigation.yaml", "source-item-snapshot.json"):
                value = {"formatVersion": 1, "items": []} if name == "source-item-snapshot.json" else ({"formatVersion": 1} if name == "manifest.yaml" else {"label": "café", "value": 1})
                (catalog_root / name).write_text(json.dumps(value, ensure_ascii=False), encoding="utf-8")
            first = module.catalog_state()[1]
            (catalog_root / "catalog.yaml").write_text(json.dumps({"value": 1, "label": "café"}, ensure_ascii=False, indent=2), encoding="utf-8")
            reformatted = module.catalog_state()[1]
            self.assertEqual(first, reformatted)
            policies = catalog_root / "policies"
            policies.mkdir()
            (policies / "query-policy.yaml").write_text('{"id":"policy_v1"}', encoding="utf-8")
            self.assertNotEqual(reformatted, module.catalog_state()[1])

    def test_validator_rejects_tampered_generated_markdown_without_rewriting(self):
        module = self.load_catalog_module()
        with tempfile.TemporaryDirectory() as temporary:
            artifacts = pathlib.Path(temporary)
            module.ARTIFACTS = artifacts
            for name in ("navigation-hierarchy.yaml", "navigation-hierarchy.md"):
                (artifacts / name).write_bytes((ARTIFACTS / name).read_bytes())
            self.assertEqual(module.validate()["catalogHash"], load_json_yaml(artifacts / "navigation-hierarchy.yaml")["catalogHash"])
            markdown = artifacts / "navigation-hierarchy.md"
            original = markdown.read_bytes()
            markdown.write_bytes(original + b"tamper\n")
            with self.assertRaisesRegex(ValueError, "stale.*modified"):
                module.validate()
            self.assertEqual(markdown.read_bytes(), original + b"tamper\n")

    def test_live_snapshot_and_manifest_are_present(self):
        snapshot = load_json_yaml(ARTIFACTS / "schema-snapshot.json")
        manifest = load_json_yaml(CATALOG / "manifest.yaml")
        self.assertTrue(snapshot["LiveVerified"])
        self.assertTrue(manifest["liveVerified"])
        self.assertGreater(manifest["physicalObjectCount"], 0)
        self.assertEqual(manifest["physicalObjectCount"], len(snapshot["PostgreSqlObjects"]))

    def test_table_ids_are_stable_and_unique(self):
        snapshot = load_json_yaml(ARTIFACTS / "schema-snapshot.json")
        ids = []
        for obj in snapshot["PostgreSqlObjects"]:
            expected = "tbl_" + hashlib.sha256(f"{obj['Schema']}.{obj['Name']}".encode()).hexdigest()[:16]
            ids.append(expected)
            path = CATALOG / "tables" / f"{expected}.yaml"
            record = load_json_yaml(path)
            self.assertEqual(record["id"], expected)
            self.assertEqual(record["physical"]["Name"], obj["Name"])
        self.assertEqual(len(ids), len(set(ids)))

    def test_generated_physical_metadata_is_not_runtime_queryable(self):
        manifest = load_json_yaml(CATALOG / "manifest.yaml")
        self.assertFalse(manifest["runtimeQueryingEnabled"])
        for path in (CATALOG / "tables").glob("*.yaml"):
            entry = load_json_yaml(path)
            if entry["semantic"].get("status") in {"discovered", "unapproved"}:
                self.assertFalse(entry["semantic"].get("queryable", False))

    def test_dictionary_preserves_scope_and_requires_review(self):
        dictionary = load_json_yaml(CATALOG / "dictionaries/product-values.yaml")
        self.assertGreater(len(dictionary["entries"]), 0)
        for proposal in dictionary["aliasProposals"] + dictionary["crossCompanyCategoryProposals"]:
            self.assertEqual(proposal["status"], "pending-review")
            self.assertFalse(proposal["executable"])
        scoped = set()
        for entry in dictionary["entries"]:
            scope = entry.get("companyId") or entry.get("externalCompanyId")
            self.assertTrue(scope)
            scoped.add((scope, entry["provider"], entry.get("providerProductId"), entry.get("providerProductCode"), entry.get("productCode"), entry.get("sourceLabel"), entry.get("unit")))
            self.assertEqual(entry["review"]["status"], "pending")
            self.assertFalse(entry["review"]["executable"])
            self.assertEqual(entry["review"]["aliases"], [])
        self.assertEqual(len(scoped), len(dictionary["entries"]))

    def test_source_item_dictionary_preserves_identity_and_owner_approved_effective_unit_without_authorizing_sql(self):
        spec = importlib.util.spec_from_file_location("catalog", ROOT / "tools/SemanticCatalog/catalog.py")
        module = importlib.util.module_from_spec(spec)
        sys.modules[spec.name] = module
        spec.loader.exec_module(module)
        dictionary = module.build_source_item_dictionary()
        self.assertEqual(len(dictionary["entries"]), 296)
        entry = next(item for item in dictionary["entries"]
                     if item["providerName"] == "NoavaranCurrentApi"
                     and item["statementType"] == "IncomeStatement" and item["sourceItemId"] == 161)
        self.assertEqual(entry["sourceItemCatalogId"], "3cafc7d3-b0ab-4341-85ef-ebcbc9d60528")
        self.assertIsNone(entry["titleEn"])
        self.assertEqual(entry["sourceUnit"], "N/A")
        self.assertEqual(entry["sourceUnitStatus"], "unknown")
        self.assertEqual(entry["effectiveUnit"], "IRRPerShare")
        self.assertEqual(entry["unitClassification"], "MonetaryPerShareItem")
        self.assertEqual(entry["normalizedUnit"], "IRRPerShare")
        self.assertTrue(entry["semanticApproval"]["approved"])
        self.assertEqual(entry["effectiveUnitProvenance"], "business-owner-confirmed:Feature141-2026-10-08")
        self.assertEqual(entry["identityApproval"]["status"], "OwnerApproved")
        self.assertEqual(entry["titleApproval"]["status"], "OwnerApproved")
        self.assertTrue(any("\u0600" <= character <= "\u06ff" for character in entry["titleFa"]))
        self.assertNotIn("\u00d9", entry["titleFa"])
        self.assertEqual(entry["canonicalConceptBindings"][0]["canonicalConceptId"], "operatingProfitPerShare")
        self.assertIsNone(entry["mappedMetricCode"])
        self.assertFalse(entry["queryAuthorization"]["queryable"])
        ordinary = next(item for item in dictionary["entries"] if item["sourceItemId"] == 1
                        and item["statementType"] == "BalanceSheet")
        self.assertEqual(ordinary["effectiveUnit"], "MillionIRR")
        self.assertEqual(ordinary["unitClassification"], "MonetaryStatementItem")
        self.assertEqual(ordinary["effectiveUnitStatus"], "owner-policy-classified")
        self.assertEqual(ordinary["sourceUnit"], "N/A")
        self.assertEqual(ordinary["sourceUnitStatus"], "unknown")
        per_share = next(item for item in dictionary["entries"] if item["effectiveUnit"] == "IRRPerShare")
        self.assertEqual(per_share["effectiveUnit"], "IRRPerShare")
        self.assertTrue(per_share["effectiveUnitProvenance"])
        ambiguous = next(item for item in dictionary["entries"] if item["effectiveUnitStatus"] == "requires-semantic-review")
        self.assertIsNone(ambiguous["effectiveUnit"])
        self.assertEqual(ambiguous["unitClassification"], "RequiresSemanticReview")

    def test_all_source_item_rows_are_owner_approved_exactly_once_without_approving_meaning_or_query_access(self):
        module = self.load_catalog_module()
        snapshot = module.read(CATALOG / "source-item-snapshot.json")
        dictionary = module.build_source_item_dictionary()
        self.assertEqual(len(snapshot["items"]), 296)
        self.assertEqual(len(dictionary["entries"]), 296)
        key = lambda item: (item["providerName"], item["statementType"], item["sourceItemId"])
        source = {key(item): item for item in snapshot["items"]}
        entries = {key(item): item for item in dictionary["entries"]}
        self.assertEqual(len(source), 296)
        self.assertEqual(len(entries), 296)
        self.assertEqual(source.keys(), entries.keys())
        for identity, entry in entries.items():
            original = source[identity]
            self.assertEqual(entry["sourceItemCatalogId"], original["sourceItemCatalogId"])
            self.assertEqual(entry["titleFa"], original["titleFa"])
            self.assertEqual(entry["identityApproval"]["status"], "OwnerApproved")
            self.assertEqual(entry["titleApproval"]["status"], "OwnerApproved")
            self.assertFalse(entry["queryAuthorization"]["approved"])
            self.assertFalse(entry["queryAuthorization"]["queryable"])
        self.assertEqual(sum(item["semanticReviewStatus"] == "approved" for item in entries.values()), 1)

    def test_reviewed_direct_source_bindings_preserve_similar_items_and_leave_unreviewed_concepts_incomplete(self):
        module = self.load_catalog_module()
        dictionary = module.build_source_item_dictionary()
        bound = module.resolve_canonical_source_item_bindings(dictionary, "operatingProfitPerShare",
            provider_name="NoavaranCurrentApi", statement_type="IncomeStatement", expected_dimension="CurrencyPerShare")
        self.assertEqual(bound["status"], "bound")
        self.assertEqual(bound["referenceType"], "DirectSourceItem")
        self.assertEqual((bound["statementType"], bound["sourceItemId"], bound["effectiveUnit"]),
                         ("IncomeStatement", 161, "IRRPerShare"))
        self.assertFalse(bound["queryAuthorized"])
        self.assertEqual(module.resolve_canonical_source_item_bindings(dictionary, "operatingProfitPerShare",
            expected_dimension="CurrencyAmount")["status"], "incomplete")
        self.assertEqual(module.resolve_canonical_source_item_bindings(dictionary, "netProfit")["status"], "incomplete")

        hierarchy = module.build_hierarchy()
        node = next(item for item in hierarchy["nodes"] if item["nodeId"] == f"direct-source-item-{bound['catalogId']}")
        reference = node["sourceItemReferences"][0]
        self.assertEqual(reference["identityApproval"]["status"], "OwnerApproved")
        self.assertEqual(reference["titleApproval"]["status"], "OwnerApproved")
        self.assertEqual(reference["canonicalConceptBindings"][0]["canonicalConceptId"], "operatingProfitPerShare")
        self.assertTrue(node["semanticApproval"]["approved"])
        self.assertFalse(node["queryAuthorization"]["queryable"])

        unreviewed = [
            ("IncomeStatement", 143, "NET_PROFIT"),
            ("IncomeStatement", 160, "EPS"),
            ("IncomeStatement", 168, "EPS_CONSOLIDATED"),
            ("IncomeStatement", 317, None),
            ("BalanceSheet", 271, None),
            ("BalanceSheet", 286, None),
        ]
        for statement_type, source_item_id, metric_code in unreviewed:
            entry = next(item for item in dictionary["entries"]
                         if item["statementType"] == statement_type and item["sourceItemId"] == source_item_id)
            self.assertEqual(entry["mappedMetricCode"], metric_code)
            self.assertEqual(entry["semanticReviewStatus"], "pending-review")
            self.assertEqual(entry["canonicalConceptBindings"], [])
        self.assertNotEqual(
            next(item for item in dictionary["entries"] if item["statementType"] == "IncomeStatement" and item["sourceItemId"] == 160)["catalogId"],
            next(item for item in dictionary["entries"] if item["statementType"] == "IncomeStatement" and item["sourceItemId"] == 168)["catalogId"])
        codal_item = next(item for item in dictionary["entries"]
                          if item["statementType"] == "IncomeStatement" and item["sourceItemId"] == 317)
        self.assertIn("کدال", codal_item["titleFa"])
        self.assertEqual(codal_item["canonicalConceptBindings"], [])

    def test_categorical_financial_statement_item_does_not_receive_monetary_unit(self):
        module = self.load_catalog_module()
        dictionary = module.build_source_item_dictionary()
        item = next(entry for entry in dictionary["entries"]
                    if entry["providerName"] == "NoavaranCurrentApi"
                    and entry["statementType"] == "BalanceSheet" and entry["sourceItemId"] == 286)
        self.assertIsNone(item["effectiveUnit"])
        self.assertEqual(item["effectiveUnitStatus"], "requires-semantic-review")
        self.assertEqual(item["unitClassification"], "RequiresSemanticReview")

    def test_source_item_unit_classification_is_bounded_to_statement_dictionary_and_reports_ambiguous_titles(self):
        spec = importlib.util.spec_from_file_location("catalog_units", ROOT / "tools/SemanticCatalog/catalog.py")
        module = importlib.util.module_from_spec(spec)
        sys.modules[spec.name] = module
        spec.loader.exec_module(module)
        dictionary = module.build_source_item_dictionary()
        self.assertEqual(len(dictionary["entries"]), 296)
        self.assertTrue(all(entry["queryAuthorization"]["queryable"] is False for entry in dictionary["entries"]))
        self.assertTrue(all(entry["effectiveUnit"] in {"MillionIRR", "IRRPerShare", None} for entry in dictionary["entries"]))
        self.assertTrue(all(entry["effectiveUnitProvenance"] for entry in dictionary["entries"]))
        report = module.build_source_item_unit_review_report()
        self.assertEqual(report["snapshotItemCount"], 296)
        self.assertEqual(sum(report["classificationCounts"].values()), 296)
        self.assertEqual(report["classificationCounts"]["MonetaryPerShareItem"], len(report["perShareItems"]))
        self.assertGreater(len(report["perShareItems"]), 1)
        self.assertEqual(report["classificationCounts"]["MonetaryPerShareItem"], len(report["perShareItems"]))
        self.assertGreater(len(report["perShareItems"]), 1)
        self.assertGreater(len(report["ambiguousItems"]), 0)
        self.assertTrue(all(item["titleFa"] for item in report["ambiguousItems"]))

    def test_profile_is_bounded_and_reconciliation_exposes_drift(self):
        profile = load_json_yaml(ARTIFACTS / "profiling-report.json")
        report = load_json_yaml(ARTIFACTS / "reconciliation.json")
        self.assertTrue(profile["liveDataProfiled"])
        self.assertLessEqual(profile["sampledLineItems"], 5000)
        self.assertLessEqual(profile["sampledAcceptedReports"], 500)
        self.assertLessEqual(profile["companyIndustryProfile"]["companiesInspected"], 1000)
        self.assertLessEqual(profile["companyIndustryProfile"]["industriesInspected"], 1000)
        self.assertIn("productionQuantity", profile["numericFields"])
        self.assertIn("EF_ONLY_OBJECT", {item["Code"] for item in report["Differences"]})
        self.assertIn("CATALOG_OBJECT_MISSING_FROM_SOURCE", {item["Code"] for item in load_json_yaml(ARTIFACTS / "catalog-drift.json")})

    def test_generated_files_do_not_contain_connection_credentials(self):
        for path in [*CATALOG.rglob("*.yaml"), *ARTIFACTS.glob("*.json")]:
            content = path.read_text(encoding="utf-8").lower()
            self.assertNotIn("password=", content)
            self.assertNotIn("username=", content)
            self.assertNotIn("connectionstrings__financialcopilot", content)

    def test_physical_relationship_and_dimension_references_resolve(self):
        tables = {entry["id"]: entry["physical"] for entry in (load_json_yaml(p) for p in (CATALOG / "tables").glob("*.yaml"))}
        table_by_name = {(obj["Schema"], obj["Name"]): obj for obj in tables.values()}
        for path in (CATALOG / "relationships").glob("*.yaml"):
            relation = load_json_yaml(path)
            source = table_by_name[(relation["from"]["schema"], relation["from"]["table"])]
            target = table_by_name[(relation["to"]["schema"], relation["to"]["table"])]
            self.assertTrue(set(relation["from"]["columns"]).issubset({c["Name"] for c in source["Columns"]}))
            self.assertTrue(set(relation["to"]["columns"]).issubset({c["Name"] for c in target["Columns"]}))
            self.assertFalse(relation["semantic"]["runtimeJoinable"])
        for path in (CATALOG / "dimensions").glob("*.yaml"):
            dimension = load_json_yaml(path)
            table = tables[dimension["physicalTableId"]]
            self.assertTrue(set(dimension["columns"]).issubset({c["Name"] for c in table["Columns"]}))
            self.assertFalse(dimension["runtimeQueryable"])

    def test_owner_confirmed_statement_semantics_are_independent_and_scoped(self):
        scope = load_json_yaml(CATALOG / "catalog-scope.yaml")
        policy = scope["querySelectionPolicy"]
        self.assertEqual(policy["default"], "IsComposing=false (parent-company / separate financial statement)")
        self.assertIn("Never silently substitute", policy["missingDefaultScope"])
        self.assertEqual(set(policy["independentDimensions"]), {"IsAudited", "IsRepresented", "IsComposing"})
        record = load_json_yaml(CATALOG / "tables/tbl_53d51e26e1ca6929.yaml")
        definitions = {item["field"]: item for item in record["semantic"]["businessDefinitions"]}
        self.assertEqual(definitions["IsAudited"]["falseMeans"], "Unaudited")
        self.assertEqual(definitions["IsRepresented"]["falseMeans"], "Not restated")
        self.assertEqual(definitions["IsComposing"]["falseMeans"], "Parent-company / separate financial statement")
        self.assertTrue(all(item["provenance"] == "business-owner-confirmed" for item in definitions.values()))
        self.assertIn("genericPathConflict", scope["codalSelectionPolicyReview"])

    def test_navigation_hierarchy_is_deterministic_and_never_authorizes_querying(self):
        spec = importlib.util.spec_from_file_location("catalog", ROOT / "tools/SemanticCatalog/catalog.py")
        module = importlib.util.module_from_spec(spec)
        sys.modules[spec.name] = module
        spec.loader.exec_module(module)
        first = module.build_hierarchy()
        second = module.build_hierarchy()
        self.assertEqual(first, second)
        output = ARTIFACTS / "navigation-hierarchy.yaml"
        markdown = ARTIFACTS / "navigation-hierarchy.md"
        before = (output.read_bytes(), markdown.read_bytes())
        result = subprocess.run([sys.executable, str(ROOT / "tools/SemanticCatalog/catalog.py"), "hierarchy", "build"], capture_output=True, text=True, check=True)
        self.assertIn(f"Built {len(first['nodes'])} nodes", result.stdout)
        self.assertEqual(before, (output.read_bytes(), markdown.read_bytes()))
        self.assertEqual(first["hierarchyHash"], load_json_yaml(output)["hierarchyHash"])
        nodes = {node["nodeId"]: node for node in first["nodes"]}
        self.assertEqual(len(nodes), len(first["nodes"]))
        for node in nodes.values():
            self.assertTrue(node["queryAuthorization"]["state"] in {"not-authorized", "not-approved"})
            self.assertFalse(node["queryAuthorization"]["queryable"])
            self.assertFalse(node["navigationMetadataApproval"]["approved"])
            self.assertTrue(all(child in nodes for child in node["childNodeIds"]))
        financial_statement_id = "table-" + load_json_yaml(CATALOG / "tables/tbl_53d51e26e1ca6929.yaml")["id"]
        self.assertIn(financial_statement_id, nodes)

    def test_navigation_golden_dataset_is_versioned_and_has_heldout_compositions(self):
        fixture = load_json_yaml(FEATURE / "evaluation/navigation-golden-v1.yaml")
        cases = fixture["cases"]
        spec = importlib.util.spec_from_file_location("catalog", ROOT / "tools/SemanticCatalog/catalog.py")
        module = importlib.util.module_from_spec(spec)
        sys.modules[spec.name] = module
        spec.loader.exec_module(module)
        hierarchy = module.build_hierarchy()
        valid_nodes = {node["nodeId"] for node in hierarchy["nodes"]}
        valid_metrics = {metric["id"] for metric in (load_json_yaml(p) for p in (CATALOG / "metrics").glob("*.yaml"))}
        self.assertEqual(fixture["datasetVersion"], 1)
        self.assertGreaterEqual(len(cases), 13)
        self.assertGreaterEqual(sum("heldout" in case["id"] for case in cases), 3)
        required = {"domains", "nodeIds", "tables", "metrics", "metricIds", "dimensions", "entities", "relationships", "providerPeriodPolicies", "completeness", "existingCapability", "genericSupport"}
        for case in cases:
            self.assertTrue(required.issubset(case))
            self.assertTrue(set(case["nodeIds"]).issubset(valid_nodes))
            self.assertTrue(set(case["metricIds"]).issubset(valid_metrics))
        self.assertEqual(next(c for c in cases if c["id"] == "fa-13-heldout-unsupported")["genericSupport"], "unsupported")

    def test_cli_self_tests_reject_duplicate_ids_and_bad_references(self):
        tool = ROOT / "tools/SemanticCatalog/SemanticCatalog.csproj"
        result = subprocess.run(["dotnet", "run", "--project", str(tool), "--no-build", "--", "--self-test"], capture_output=True, text=True, check=True)
        self.assertIn("duplicate IDs", result.stdout)
        self.assertIn("redaction", result.stdout)

    def test_offline_mode_reports_missing_live_access_without_fabricating_values(self):
        tool = ROOT / "tools/SemanticCatalog/SemanticCatalog.csproj"
        with tempfile.TemporaryDirectory() as temp:
            target = pathlib.Path(temp)
            catalog_root = target / "catalog"
            (catalog_root / "sources").mkdir(parents=True)
            (catalog_root / "catalog-scope.yaml").write_bytes((CATALOG / "catalog-scope.yaml").read_bytes())
            source_record = CATALOG / "sources/src_nadpco_company_fundamental_index_values.yaml"
            (catalog_root / "sources" / source_record.name).write_bytes(source_record.read_bytes())
            subprocess.run(["dotnet", "run", "--project", str(tool), "--configuration", "Release", "--no-build", "--", "--output", str(target / "artifacts"), "--catalog-root", str(catalog_root)], capture_output=True, text=True, check=True)
            snapshot = load_json_yaml(target / "artifacts/schema-snapshot.json")
            dictionary = load_json_yaml(catalog_root / "dictionaries/product-values.yaml")
            scope = load_json_yaml(catalog_root / "catalog-scope.yaml")
            catalog = load_json_yaml(catalog_root / "catalog.yaml")
            self.assertFalse(snapshot["LiveVerified"])
            self.assertGreater(len(snapshot["EfObjects"]), 0)
            self.assertEqual(dictionary["entries"], [])
            self.assertEqual(dictionary["discovery"]["status"], "unavailable")
            self.assertEqual(len(scope["includedCandidates"]), 18)
            self.assertIn("monthlyReportProviderRoutes", scope)
            self.assertEqual(catalog["sources"], ["src_nadpco_company_fundamental_index_values"])
            self.assertEqual(catalog["navigation"], "navigation.yaml")

    def test_curated_scope_is_source_backed_and_non_queryable(self):
        scope = load_json_yaml(CATALOG / "catalog-scope.yaml")
        snapshot = load_json_yaml(ARTIFACTS / "schema-snapshot.json")
        physical_names = {f"{item['Schema']}.{item['Name']}" for item in snapshot["PostgreSqlObjects"]}
        candidates = scope["includedCandidates"]
        self.assertEqual(len(candidates), 18)
        for candidate in candidates:
            self.assertIn(candidate["table"], physical_names)
            self.assertFalse(candidate["queryable"])
            table = load_json_yaml(CATALOG / "tables" / f"{candidate['id']}.yaml")
            self.assertEqual(table["semantic"]["status"], "candidate-pending-review")
            self.assertFalse(table["semantic"]["queryable"])
            self.assertFalse(table["semantic"]["semanticApproval"]["approved"])
            self.assertFalse(table["semantic"]["queryAuthorization"]["runtimeQueryable"])
            self.assertFalse(candidate["semanticApproval"]["approved"])
            self.assertFalse(candidate["queryAuthorization"]["runtimeQueryable"])
            self.assertIn("descriptionEn", table["semantic"])
            self.assertIn("descriptionFa", table["semantic"])
        routes = {row["reportingType"]: row["service"] for row in scope["monthlyReportProviderRoutes"]["routes"]}
        self.assertEqual(routes, {
            1000000: "api/v2/MonthlyActivity/ProductSales",
            1000005: "api/v3/MonthlyActivity/ServiceSales",
            1000008: "api/v2/MonthlyActivity/ProductSales",
        })
        source = load_json_yaml(CATALOG / "sources/src_nadpco_company_fundamental_index_values.yaml")
        catalog = load_json_yaml(CATALOG / "catalog.yaml")
        self.assertEqual(catalog["sources"], [source["id"]])
        self.assertFalse(source["queryable"])
        self.assertEqual(catalog["navigation"], "navigation.yaml")
        self.assertIn("ProviderRawPayloads", source["rawPersistence"])
        self.assertIn("DerivedMetrics", source["normalizedPersistence"])
        self.assertIn("million IRR per source quantity unit", load_json_yaml(CATALOG / "tables/tbl_8ecd0432a4292998.yaml")["semantic"]["units"])
        self.assertIn("public.TradingInstruments", {candidate["table"] for candidate in candidates})
        ti_candidate = next(candidate for candidate in candidates if candidate["table"] == "public.TradingInstruments")
        ti = load_json_yaml(CATALOG / "tables" / f"{ti_candidate['id']}.yaml")
        live_ti = next(item for item in snapshot["PostgreSqlObjects"] if item["Schema"] == "public" and item["Name"] == "TradingInstruments")
        self.assertEqual(ti["physical"]["Columns"], live_ti["Columns"])
        self.assertEqual(ti["physical"]["ForeignKeys"], live_ti["ForeignKeys"])
        expected_market_fks = {
            "IntradayTradeSnapshots", "DailyInstrumentTrades", "IntradayIndexSnapshots", "DailyIndexSnapshots"
        }
        for table_name in expected_market_fks:
            market = next(item for item in snapshot["PostgreSqlObjects"] if item["Schema"] == "public" and item["Name"] == table_name)
            self.assertTrue(any(fk["PrincipalTable"] == "TradingInstruments" and fk["Columns"] == ["TradingInstrumentId"] for fk in market["ForeignKeys"]))
        statements_candidate = next(candidate for candidate in candidates if candidate["table"] == "public.FinancialStatements")
        statements = load_json_yaml(CATALOG / "tables" / f"{statements_candidate['id']}.yaml")["semantic"]
        flags = {definition["field"]: definition for definition in statements["businessDefinitions"]}
        self.assertEqual(flags["IsRepresented"]["trueMeans"], "Restated")
        self.assertEqual(flags["IsRepresented"]["provenance"], "business-owner-confirmed")
        self.assertEqual(flags["IsComposing"]["status"], "confirmed")
        self.assertEqual(flags["IsComposing"]["falseMeans"], "Parent-company / separate financial statement")
        self.assertEqual(scope["financialStatementProviderPolicy"]["precedence"], "No global provider precedence is implemented or asserted.")
        self.assertIn("not independently approved", scope["codalSelectionPolicyReview"]["businessApproval"])
        self.assertIn("Novaran Amin", scope["dataSourceLineage"]["Companies"]["path"])
        self.assertIn("TSETMC", scope["dataSourceLineage"]["TradingInstruments"]["path"])

    def test_curated_markdown_is_deterministically_rendered_from_yaml(self):
        script = ROOT / "tools/SemanticCatalog/curate_catalog.py"
        spec = importlib.util.spec_from_file_location("curate_catalog", script)
        module = importlib.util.module_from_spec(spec)
        sys.modules[spec.name] = module
        spec.loader.exec_module(module)
        before = (FEATURE / "curated-catalog-review.md").read_bytes()
        module.render_markdown(load_json_yaml(CATALOG / "catalog-scope.yaml"))
        after = (FEATURE / "curated-catalog-review.md").read_bytes()
        self.assertEqual(before, after)


if __name__ == "__main__":
    unittest.main()
