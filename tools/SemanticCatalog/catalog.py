#!/usr/bin/env python3
"""Deterministic Feature 141 catalog lifecycle commands (YAML 1.2 JSON subset)."""

import argparse
import hashlib
import json
import unicodedata
import uuid
from pathlib import Path


ROOT = Path(__file__).resolve().parents[2]
FEATURE = ROOT / "specs/141-schema-aware-semantic-query-engine"
CATALOG = FEATURE / "SemanticCatalog"
ARTIFACTS = FEATURE / "artifacts"
SOURCE_ITEM_SNAPSHOT = CATALOG / "source-item-snapshot.json"
def read(path):
    def reject_duplicate_keys(pairs):
        value = {}
        for key, item in pairs:
            if key in value:
                raise ValueError(f"Duplicate JSON object key {key!r} in {path}")
            value[key] = item
        return value
    return json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=reject_duplicate_keys)


def normalize(value):
    if isinstance(value, str):
        return unicodedata.normalize("NFC", value)
    if isinstance(value, list):
        return [normalize(item) for item in value]
    if isinstance(value, dict):
        result = {}
        for key, item in value.items():
            normalized_key = normalize(key)
            if normalized_key in result:
                raise ValueError(f"Duplicate normalized JSON key {normalized_key!r}")
            result[normalized_key] = normalize(item)
        return result
    return value


def canonical(value):
    return json.dumps(normalize(value), ensure_ascii=False, sort_keys=True, separators=(",", ":"))


def catalog_state():
    paths = [CATALOG / name for name in ("catalog.yaml", "catalog-scope.yaml", "manifest.yaml", "navigation.yaml", "source-item-snapshot.json")]
    for folder in ("tables", "relationships", "metrics", "dimensions", "dictionaries", "sources", "policies"):
        paths += sorted((CATALOG / folder).glob("*.yaml"))
    expected_paths = {path.resolve() for path in paths}
    unexpected = sorted(path.relative_to(CATALOG).as_posix() for path in CATALOG.rglob("*.yaml") if path.resolve() not in expected_paths)
    if unexpected:
        raise ValueError(f"Unclassified YAML source files: {', '.join(unexpected)}")
    missing = [p.relative_to(CATALOG).as_posix() for p in paths if not p.is_file()]
    if missing:
        raise ValueError(f"Missing canonical catalog source files: {', '.join(missing)}")
    package = []
    for path in sorted(paths):
        value = read(path)
        if path.name == "manifest.yaml":
            value.pop("navigationHierarchyHash", None)
        package.append(f"{path.relative_to(CATALOG).as_posix()}\0{canonical(value)}")
    material = "feature141-semantic-catalog-v1\n" + "\n".join(package)
    manifest = read(CATALOG / "manifest.yaml")
    return manifest.get("formatVersion", 1), hashlib.sha256(material.encode("utf-8")).hexdigest()


def validate_source_index():
    catalog = read(CATALOG / "catalog.yaml")
    for key in ("tables", "relationships", "metrics", "dimensions", "dictionaries", "sources", "policies"):
        folder = CATALOG / key
        records = [read(path) for path in sorted(folder.glob("*.yaml"))]
        ids = [record.get("id", path.stem) for path, record in zip(sorted(folder.glob("*.yaml")), records)]
        if len(ids) != len(set(ids)):
            raise ValueError(f"Duplicate {key} source ID")
        declared = catalog.get(key, [])
        if len(declared) != len(set(declared)) or set(declared) != set(ids):
            raise ValueError(f"{key} index does not exactly match package files")
    if catalog.get("manifest") != "manifest.yaml" or catalog.get("navigation") != "navigation.yaml":
        raise ValueError("Catalog manifest/navigation source references are invalid")


def build_source_item_dictionary():
    snapshot = read(SOURCE_ITEM_SNAPSHOT)
    policy = read(CATALOG / "policies/financial-value-retrieval.yaml")
    unit_policy = policy["statementItemUnits"]
    owner_approved = {
        (item["providerName"], item["statementType"], item["sourceItemId"]): item
        for item in unit_policy.get("reviewedItems", [])
    }
    if snapshot.get("formatVersion") != 1 or not isinstance(snapshot.get("items"), list):
        raise ValueError("Unsupported or malformed financial statement source-item snapshot")
    entries, keys, physical_ids = [], set(), set()
    for source in snapshot["items"]:
        provider, statement_type, source_item_id = source.get("providerName"), source.get("statementType"), source.get("sourceItemId")
        if not provider or not statement_type or not isinstance(source_item_id, int):
            raise ValueError("Source-item snapshot record is missing provider, statement type, or numeric source ID")
        key = (provider, statement_type, source_item_id)
        if key in keys:
            raise ValueError(f"Duplicate provider-scoped source-item key: {key}")
        keys.add(key)
        catalog_uuid = source.get("sourceItemCatalogId")
        try:
            catalog_uuid = str(uuid.UUID(catalog_uuid))
        except (ValueError, TypeError, AttributeError):
            raise ValueError(f"Invalid SourceItemCatalogId for source-item key {key}")
        if catalog_uuid in physical_ids:
            raise ValueError("SourceItemCatalogId must be unique")
        physical_ids.add(catalog_uuid)
        stable = "source_item_" + hashlib.sha256(f"{provider}\0{statement_type}\0{source_item_id}".encode("utf-8")).hexdigest()[:16]
        source_unit = source.get("unit")
        metric_code = source.get("metricCode") or None
        key = (provider, statement_type, source_item_id)
        reviewed = owner_approved.get(key)
        effective_unit, unit_status, unit_provenance = classify_statement_item_unit(source, unit_policy)
        if reviewed and reviewed.get("effectiveUnit"):
            effective_unit = reviewed["effectiveUnit"]
            unit_status = reviewed.get("unitStatus", "owner-approved")
            unit_provenance = reviewed.get("unitProvenance", "business-owner-confirmed")
        semantic_approval = reviewed.get("semanticApproval") if reviewed else None
        classification = "RequiresSemanticReview" if effective_unit is None else (
            "MonetaryPerShareItem" if effective_unit == "IRRPerShare" else "MonetaryStatementItem")
        entries.append({
            "catalogId": stable,
            "referenceType": "DirectSourceItem",
            "sourceItemCatalogId": catalog_uuid,
            "providerName": provider,
            "statementType": statement_type,
            "sourceItemId": source_item_id,
            "titleFa": source.get("titleFa"),
            "titleEn": source.get("titleEn"),
            "aliases": [],
            "aliasReviewStatus": "no-reviewed-aliases",
            "sourceUnit": source_unit,
            "sourceUnitStatus": "unknown" if source_unit is None or source_unit.strip().upper() == "N/A" else "provider-reported",
            "normalizedUnit": effective_unit,
            "effectiveUnit": effective_unit,
            "effectiveUnitStatus": unit_status,
            "effectiveUnitProvenance": unit_provenance,
            "unitClassification": classification,
            "mappedMetricCode": metric_code,
            "identityStatus": "verified-persisted-source-key",
            "identityApproval": {
                "status": "OwnerApproved",
                "authority": "ProductOwner",
                "scope": ["providerName", "statementType", "sourceItemId", "sourceItemCatalogId"],
                "provenance": "product-owner-approved:Feature141-source-item-registry-2026-10-10",
            },
            "titleApproval": {
                "status": "OwnerApproved",
                "authority": "ProductOwner",
                "scope": ["titleFa"],
                "provenance": "product-owner-approved:Feature141-source-item-registry-2026-10-10",
            },
            "semanticReviewStatus": "approved" if semantic_approval and semantic_approval.get("approved") else "pending-review",
            "semanticApproval": semantic_approval or {"approved": False, "status": "pending-review"},
            "canonicalConceptBindings": reviewed.get("canonicalConceptBindings", [])
                if semantic_approval and semantic_approval.get("approved") else [],
            "queryAuthorization": {
                "state": "approved-direct-source-identity",
                "approved": True,
                "queryable": True,
                "provenance": "product-owner-approved:Feature141-full-chat-activation-direct-source-access",
                "scope": ["providerName", "statementType", "sourceItemId", "sourceItemCatalogId"],
                "reason": "Permits parameterized retrieval by the verified registered source identity only; it does not approve canonical semantic equivalence.",
            },
            "sourceProvenance": {
                "entity": "public.FinancialStatementSourceItems",
                "identityKey": ["ProviderName", "StatementType", "SourceItemId"],
                "mappingEntity": "public.FinancialStatementSourceItemMetricMappings" if metric_code else None,
                "verified": True,
            },
        })
    entries.sort(key=lambda item: (item["providerName"], item["statementType"], item["sourceItemId"], item["catalogId"]))
    return {
        "formatVersion": 1,
        "id": "financial-statement-source-items",
        "kind": "FinancialStatementSourceItemDictionary",
        "source": "read-only persisted source-item catalog snapshot; no observation values",
        "approvalRequired": True,
        "queryAuthorization": {
            "approved": True,
            "queryable": True,
            "provenance": "product-owner-approved:Feature141-full-chat-activation-direct-source-access",
            "scope": "verified registered source identities only",
        },
        "entries": entries,
    }


def classify_statement_item_unit(source, policy):
    """Apply versioned, deterministic title rules only to statement line items."""
    title = " ".join(str(source.get(name) or "") for name in ("titleFa", "titleEn")).casefold()
    compact = "".join(title.split())
    per_share_markers = policy["classificationRules"]["perShareMarkers"]
    review_markers = policy["classificationRules"]["requiresReviewMarkers"]
    if any("".join(marker.casefold().split()) in compact for marker in per_share_markers):
        return "IRRPerShare", "owner-policy-classified", "financial-statement-unit-policy-v1:per-share-title-rule"
    if any("".join(marker.casefold().split()) in compact for marker in review_markers):
        return None, "requires-semantic-review", "financial-statement-unit-policy-v1:nonmonetary-or-ambiguous-title"
    return "MillionIRR", "owner-policy-classified", "financial-statement-unit-policy-v1:ordinary-statement-amount"


def resolve_canonical_source_item_bindings(dictionary, canonical_concept_id, provider_name=None, statement_type=None,
                                          expected_dimension=None):
    """Resolve only explicit reviewed concept bindings; identity approval alone is insufficient."""
    candidates = [entry for entry in dictionary["entries"]
        if entry.get("semanticReviewStatus") == "approved"
        and entry.get("identityApproval", {}).get("status") == "OwnerApproved"
        and entry.get("titleApproval", {}).get("status") == "OwnerApproved"
        and entry.get("providerName") == (provider_name or entry.get("providerName"))
        and entry.get("statementType") == (statement_type or entry.get("statementType"))
        and any(binding.get("canonicalConceptId") == canonical_concept_id
                and binding.get("status") == "approved"
                and (expected_dimension is None or binding.get("dimension") == expected_dimension)
                for binding in entry.get("canonicalConceptBindings", []))]
    if not candidates:
        return {"status": "incomplete", "reason": "no-reviewed-canonical-source-item-binding", "candidates": []}
    if len(candidates) != 1:
        return {"status": "ambiguous", "reason": "multiple-reviewed-source-item-bindings", "candidates": candidates}
    entry = candidates[0]
    if entry.get("effectiveUnit") is None or entry.get("effectiveUnitStatus") == "requires-semantic-review":
        return {"status": "incomplete", "reason": "source-item-unit-unresolved", "candidates": candidates}
    return {"status": "bound", "reason": None, "referenceType": "DirectSourceItem",
        "catalogId": entry["catalogId"], "sourceItemCatalogId": entry["sourceItemCatalogId"],
        "providerName": entry["providerName"], "statementType": entry["statementType"],
        "sourceItemId": entry["sourceItemId"], "effectiveUnit": entry["effectiveUnit"],
        "queryAuthorized": entry["queryAuthorization"]["queryable"]}


def build_source_item_unit_review_report():
    snapshot = read(SOURCE_ITEM_SNAPSHOT)
    policy = read(CATALOG / "policies/financial-value-retrieval.yaml")["statementItemUnits"]
    review, per_share = [], []
    counts = {"MonetaryStatementItem": 0, "MonetaryPerShareItem": 0, "RequiresSemanticReview": 0}
    for item in snapshot["items"]:
        effective, status, provenance = classify_statement_item_unit(item, policy)
        reviewed = next((x for x in policy.get("reviewedItems", []) if
            (x["providerName"], x["statementType"], x["sourceItemId"]) ==
            (item["providerName"], item["statementType"], item["sourceItemId"])), None)
        if reviewed and reviewed.get("effectiveUnit"):
            effective = reviewed["effectiveUnit"]
        category = "RequiresSemanticReview" if effective is None else ("MonetaryPerShareItem" if effective == "IRRPerShare" else "MonetaryStatementItem")
        counts[category] += 1
        if category == "RequiresSemanticReview":
            review.append({"providerName": item["providerName"], "statementType": item["statementType"],
                "sourceItemId": item["sourceItemId"], "sourceItemCatalogId": item["sourceItemCatalogId"],
                "titleFa": item.get("titleFa"), "titleEn": item.get("titleEn"),
                "rawProviderUnit": item.get("unit"), "reason": provenance})
        elif category == "MonetaryPerShareItem":
            per_share.append({"providerName": item["providerName"], "statementType": item["statementType"],
                "sourceItemId": item["sourceItemId"], "sourceItemCatalogId": item["sourceItemCatalogId"],
                "titleFa": item.get("titleFa"), "titleEn": item.get("titleEn"),
                "rawProviderUnit": item.get("unit"), "effectiveUnit": effective, "reason": provenance})
    return {"formatVersion": 1, "policyVersion": policy["version"], "snapshotItemCount": len(snapshot["items"]),
        "classificationCounts": counts,
        "perShareItems": sorted(per_share, key=lambda x: (x["providerName"], x["statementType"], x["sourceItemId"])),
        "ambiguousItems": sorted(review, key=lambda x: (x["providerName"], x["statementType"], x["sourceItemId"]))}


def sync_source_item_dictionary():
    dictionary = build_source_item_dictionary()
    path = CATALOG / "dictionaries/financial-statement-source-items.yaml"
    path.write_text(json.dumps(dictionary, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    catalog = read(CATALOG / "catalog.yaml")
    catalog["dictionaries"] = sorted(set(catalog.get("dictionaries", [])) | {dictionary["id"]})
    (CATALOG / "catalog.yaml").write_text(json.dumps(catalog, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
    return dictionary


def build_hierarchy():
    validate_source_index()
    source_dictionary = build_source_item_dictionary()
    dictionary_path = CATALOG / "dictionaries/financial-statement-source-items.yaml"
    if not dictionary_path.is_file() or read(dictionary_path) != source_dictionary:
        raise ValueError("Generated source-item dictionary is stale; run catalog hierarchy build to regenerate it")
    navigation = read(CATALOG / "navigation.yaml")
    if navigation.get("formatVersion") != 1:
        raise ValueError("Unsupported navigation definition version")
    domains = navigation["domains"]
    table_paths = sorted((CATALOG / "tables").glob("*.yaml"))
    tables = {read(p)["physical"]["Name"]: read(p) for p in table_paths}
    table_ids = {name: record["id"] for name, record in tables.items()}
    metrics = {read(p)["id"]: read(p) for p in sorted((CATALOG / "metrics").glob("*.yaml"))}
    dimensions = {read(p)["id"]: read(p) for p in sorted((CATALOG / "dimensions").glob("*.yaml"))}
    relationships = {read(p)["id"]: read(p) for p in sorted((CATALOG / "relationships").glob("*.yaml"))}
    version, digest = catalog_state()
    nodes = []

    def add(node_id, parent, title_en, title_fa, description, children=None, catalog_ids=None,
            dimension_refs=None, metric_refs=None, relation_refs=None, concepts=None,
            provider_restrictions=None, unit_period=None, semantic_approval=None, auth=None,
            direct_source_item_refs=None, provider_indicator_refs=None, source_item_references=None):
        node = {
            "nodeId": node_id, "parentNodeId": parent,
            "title": {"en": title_en, "fa": title_fa}, "description": description,
            "conceptsAndSynonyms": sorted(set(concepts or [])), "childNodeIds": children or [],
            "catalogReferences": {"tableIds": catalog_ids or [], "metricIds": metric_refs or [], "dimensionIds": dimension_refs or [], "relationshipIds": relation_refs or [],
                                   "directSourceItemIds": direct_source_item_refs or [], "providerFinancialIndicatorIds": provider_indicator_refs or []},
            "providerRestrictions": provider_restrictions or [], "unitAndPeriodSemantics": unit_period or [],
            "navigationMetadataApproval": {"state": "pending-review", "approved": False, "provenance": "curated Feature 141 navigation mapping; no reviewer approval asserted"},
            "semanticApproval": semantic_approval or {"state": "reference-only", "approved": False},
            "queryAuthorization": auth or {"state": "not-authorized", "queryable": False, "reason": "Navigation inclusion does not grant runtime query access."},
        }
        if source_item_references:
            node["sourceItemReferences"] = source_item_references
        nodes.append(node)

    root = navigation["root"]
    source_root_id = "financial-statement-source-items"
    roots = [domain["nodeId"] for domain in domains] + [navigation["sharedDimensions"]["nodeId"], source_root_id]
    add(root["nodeId"], None, root["titleEn"], root["titleFa"], root["description"], roots, concepts=root["concepts"])
    for domain in domains:
        node_id, names = domain["nodeId"], domain["tables"]
        child_ids = [f"table-{table_ids[n]}" for n in names if n in table_ids]
        add(node_id, root["nodeId"], domain["titleEn"], domain["titleFa"], domain["description"], child_ids,
            concepts=domain.get("concepts", []), metric_refs=domain.get("metricIds", []))
        for name in names:
            if name not in tables:
                continue
            table = tables[name]
            semantic = table.get("semantic", {})
            semantic_state = semantic.get("approvalStatus", semantic.get("status", "pending-review"))
            # Navigation approval is intentionally independent from semantic and runtime approvals.
            node_semantic_approval = {"state": semantic_state, "approved": False,
                                      "provenance": semantic.get("semanticApproval", {}).get("provenance", "canonical catalog review state")}
            node_auth = {"state": "not-authorized", "queryable": False,
                         "catalogQueryAuthorization": "approved" if semantic.get("queryable") else "not-approved",
                         "reason": "Canonical table query authorization is unchanged."}
            rel_ids = sorted(rid for rid, rel in relationships.items()
                             if rel.get("from", {}).get("table") == name or rel.get("to", {}).get("table") == name)
            metric_refs = sorted(set(navigation.get("tableMetricIds", {}).get(name, [])) & set(metrics))
            table_columns = {column["Name"] for column in table.get("physical", {}).get("Columns", [])}
            direct_dimensions = {did for did, dim in dimensions.items() if dim.get("physicalTableId") == table["id"]}
            if name in {"MonthlyReports", "MonthlyReportLineItems", "CompanyMonthlyActivityTrendSnapshots"}:
                direct_dimensions.add("dim_monthly_period")
            if name == "MonthlyReportLineItems": direct_dimensions.add("dim_product_identity")
            if name != "Companies" and ("CompanyId" in table_columns or "ExternalCompanyId" in table_columns or "Companies" in " ".join(semantic.get("relationships", []))):
                direct_dimensions.add("dim_company")
            if name != "Industries" and "Industries" in " ".join(semantic.get("relationships", [])):
                direct_dimensions.add("dim_industry")
            period = [semantic.get("periodSemantics", "")] if semantic.get("periodSemantics") else []
            units = [semantic.get("units", "")] if semantic.get("units") else []
            title = navigation.get("tableTitles", {}).get(name, {"en": name, "fa": name})
            add(f"table-{table['id']}", node_id, title["en"], title["fa"], semantic.get("descriptionEn") or semantic.get("purpose", name),
                catalog_ids=[table["id"]], dimension_refs=sorted(direct_dimensions), metric_refs=metric_refs,
                relation_refs=rel_ids, concepts=[name, *navigation.get("tableConcepts", {}).get(name, []), *semantic.get("examples", [])],
                provider_restrictions=[semantic.get("providerPolicy", "")], unit_period=units + period,
                semantic_approval=node_semantic_approval, auth=node_auth)
            nodes[-1]["relationshipGovernance"] = [
                {"relationshipId": rid, "semanticApproval": relationships[rid].get("semantic", {}).get("status", "unknown"),
                 "runtimeJoinable": relationships[rid].get("semantic", {}).get("runtimeJoinable", False),
                 "provenance": relationships[rid].get("provenance", {}).get("source", "unknown")}
                for rid in rel_ids
            ]

    shared = navigation["sharedDimensions"]
    external_sources = navigation.get("externalSources", [])
    source_table_ids = {read(path).get("id", path.stem) for path in (CATALOG / "sources").glob("*.yaml")}
    source_item_ids = {entry["catalogId"] for entry in source_dictionary["entries"]}
    shared_children = [f"dimension-{did}" for did in sorted(dimensions)] + [source["nodeId"] for source in external_sources]
    add(shared["nodeId"], root["nodeId"], shared["titleEn"], shared["titleFa"], shared["description"], shared_children, concepts=shared.get("concepts", []))
    for did, dim in sorted(dimensions.items()):
        presentation = navigation.get("dimensionPresentation", {}).get(did, {})
        add(f"dimension-{did}", shared["nodeId"], presentation.get("titleEn", did), presentation.get("titleFa", did),
            presentation.get("description", "Shared dimension referenced by domain nodes."), dimension_refs=[did], concepts=presentation.get("concepts", [did]))
    for source in external_sources:
        add(source["nodeId"], shared["nodeId"], source["titleEn"], source["titleFa"], source["description"], catalog_ids=[source["catalogId"]],
            concepts=source.get("concepts", []), provider_restrictions=[source.get("providerRestriction", "")],
            auth={"state": "not-authorized", "queryable": False, "reason": "External endpoint is not a PostgreSQL table."})

    source_entries = source_dictionary["entries"]
    statement_types = sorted({item["statementType"] for item in source_entries})
    statement_group_ids = {statement_type: f"source-items-{statement_type.lower()}" for statement_type in statement_types}
    add(source_root_id, root["nodeId"], "Financial Statement Source Items", "اقلام صورت‌های مالی",
        "Hierarchical discovery of provider-scoped persisted statement items. Direct reads require verified source identity and separate query authorization; pending semantic review does not authorize canonical equivalence.",
        [statement_group_ids[item] for item in statement_types], concepts=["financial statement source item", "direct source item", "source item"])
    for statement_type in statement_types:
        entries = [item for item in source_entries if item["statementType"] == statement_type]
        child_ids = [f"direct-source-item-{item['catalogId']}" for item in entries]
        add(statement_group_ids[statement_type], source_root_id, statement_type, statement_type,
            "Provider-scoped source items for this statement type.", child_ids,
            direct_source_item_refs=[item["catalogId"] for item in entries],
            concepts=[statement_type, "financial statement source item"])
        for item in entries:
            title_fa = item["titleFa"] or item["catalogId"]
            title_en = item["titleEn"] or item["catalogId"]
            concepts = [item["catalogId"], item["providerName"], item["statementType"], str(item["sourceItemId"]), *item["aliases"]]
            concepts.extend(value for value in (item["titleFa"], item["titleEn"]) if value)
            reference = {key: item[key] for key in (
                "catalogId", "sourceItemCatalogId", "providerName", "statementType", "sourceItemId",
                "titleFa", "titleEn", "sourceUnit", "sourceUnitStatus", "normalizedUnit", "effectiveUnit",
                "effectiveUnitStatus", "effectiveUnitProvenance", "unitClassification", "mappedMetricCode",
                "identityStatus", "identityApproval", "titleApproval", "semanticReviewStatus", "semanticApproval",
                "canonicalConceptBindings", "queryAuthorization")}
            add(f"direct-source-item-{item['catalogId']}", statement_group_ids[item["statementType"]],
                title_en, title_fa, "Persisted provider source-item definition; no derived formula is asserted.",
                direct_source_item_refs=[item["catalogId"]], concepts=concepts,
                semantic_approval={"state": item["semanticReviewStatus"],
                    "approved": bool(item.get("semanticApproval", {}).get("approved")),
                    "provenance": item.get("semanticApproval", {}).get("provenance", "persisted source-item catalog record")},
                auth=item["queryAuthorization"], source_item_references=[reference])

    # Parent branches advertise the union of their child catalog references and explicit metric families.
    for domain in domains:
        node = next(n for n in nodes if n["nodeId"] == domain["nodeId"])
        child_nodes = [next(n for n in nodes if n["nodeId"] == child) for child in node["childNodeIds"]]
        for kind in ("tableIds", "dimensionIds", "relationshipIds"):
            node["catalogReferences"][kind] = sorted({ref for child in child_nodes for ref in child["catalogReferences"][kind]})
        group_metrics = set(domain.get("metricIds", []))
        group_metrics.update(ref for child in child_nodes for ref in child["catalogReferences"]["metricIds"])
        node["catalogReferences"]["metricIds"] = sorted(mid for mid in group_metrics if mid in metrics)
        node["providerRestrictions"] = sorted({item for child in child_nodes for item in child["providerRestrictions"] if item})
        node["unitAndPeriodSemantics"] = sorted({item for child in child_nodes for item in child["unitAndPeriodSemantics"] if item})

    by_id = {node["nodeId"]: node for node in nodes}
    if len(by_id) != len(nodes):
        raise ValueError("Duplicate navigation node ID")
    for node in nodes:
        if any(cid not in by_id for cid in node["childNodeIds"]):
            raise ValueError(f"Dangling child reference in {node['nodeId']}")
        refs = node["catalogReferences"]
        for tid in refs["tableIds"]:
            if tid not in table_ids.values():
                # External provider records are represented by source IDs, not table IDs.
                if tid not in source_table_ids:
                    raise ValueError(f"Unknown catalog table reference {tid}")
        for mid in refs["metricIds"]:
            if mid not in metrics: raise ValueError(f"Unknown metric reference {mid}")
        for did in refs["dimensionIds"]:
            if did not in dimensions: raise ValueError(f"Unknown dimension reference {did}")
        for rid in refs["relationshipIds"]:
            if rid not in relationships: raise ValueError(f"Unknown relationship reference {rid}")
        for source_id in refs.get("directSourceItemIds", []):
            if source_id not in source_item_ids: raise ValueError(f"Unknown direct source-item reference {source_id}")
        if refs.get("providerFinancialIndicatorIds", []):
            raise ValueError("Provider financial-indicator references require an independently verified indicator catalog")
    # Parent traversal also proves there are no cycles and every node is reachable.
    reached = set()
    def visit(node_id, ancestors):
        if node_id in ancestors: raise ValueError(f"Navigation cycle at {node_id}")
        if node_id in reached: return
        reached.add(node_id)
        for child in by_id[node_id]["childNodeIds"]: visit(child, ancestors | {node_id})
    visit(root["nodeId"], set())
    if len(reached) != len(nodes): raise ValueError("Unreachable navigation nodes")
    hierarchy = {"formatVersion": 1, "catalogVersion": f"{version}", "catalogHash": digest,
                 "generatedFrom": "canonical SemanticCatalog YAML", "nodes": nodes}
    hierarchy["hierarchyHash"] = hashlib.sha256(canonical(hierarchy).encode("utf-8")).hexdigest()
    return hierarchy


def render_markdown(result):
    rows = ["# Feature 141 Navigation Hierarchy", "", f"Catalog version: `{result['catalogVersion']}`  ",
            f"Catalog hash: `{result['catalogHash']}`  ", f"Hierarchy hash: `{result['hierarchyHash']}`", "",
            "Generated from canonical SemanticCatalog YAML. Navigation metadata approval, semantic approval, and query authorization are distinct. Nodes remain review-pending and runtime queries are unauthorized.",
            "", "| Node ID | Parent | English | Persian | Tables | Navigation review | Query access |", "|---|---|---|---|---|---|---|"]
    for node in result["nodes"]:
        refs = ", ".join(node["catalogReferences"]["tableIds"]) or "—"
        rows.append(f"| `{node['nodeId']}` | `{node['parentNodeId'] or '—'}` | {node['title']['en']} | {node['title']['fa']} | {refs} | {node['navigationMetadataApproval']['state']} | {node['queryAuthorization']['state']} |")
    return "\n".join(rows) + "\n"


def validate():
    expected_dictionary = build_source_item_dictionary()
    dictionary_path = CATALOG / "dictionaries/financial-statement-source-items.yaml"
    if not dictionary_path.is_file() or read(dictionary_path) != expected_dictionary:
        raise ValueError("Generated source-item dictionary is missing or stale")
    expected = build_hierarchy()
    manifest = read(CATALOG / "manifest.yaml")
    if manifest.get("navigationHierarchyHash") != expected["hierarchyHash"]:
        raise ValueError("Manifest navigationHierarchyHash does not match generated hierarchy")
    expected_json = json.dumps(expected, ensure_ascii=False, indent=2) + "\n"
    for path, content in ((ARTIFACTS / "navigation-hierarchy.yaml", expected_json),
                          (ARTIFACTS / "navigation-hierarchy.md", render_markdown(expected))):
        if not path.is_file() or path.read_text(encoding="utf-8") != content:
            raise ValueError(f"Generated artifact is missing, stale, or modified: {path.name}")
    return expected


def main():
    parser = argparse.ArgumentParser(prog="catalog")
    sub = parser.add_subparsers(dest="area", required=True)
    hierarchy = sub.add_parser("hierarchy")
    hierarchy.add_subparsers(dest="action", required=True).add_parser("build")
    source_items = sub.add_parser("source-items")
    source_items_actions = source_items.add_subparsers(dest="action", required=True)
    source_items_actions.add_parser("build")
    source_items_actions.add_parser("review-report")
    sub.add_parser("validate", help="validate source package and generated artifacts without writing")
    args = parser.parse_args()
    if args.area == "hierarchy" and args.action == "build":
        sync_source_item_dictionary()
        result = build_hierarchy()
        manifest_path = CATALOG / "manifest.yaml"
        manifest = read(manifest_path)
        manifest["navigationHierarchyHash"] = result["hierarchyHash"]
        manifest_path.write_text(json.dumps(manifest, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
        out = ARTIFACTS / "navigation-hierarchy.yaml"
        markdown = ARTIFACTS / "navigation-hierarchy.md"
        out.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
        rows = ["# Feature 141 Navigation Hierarchy", "", f"Catalog version: `{result['catalogVersion']}`  ", f"Catalog hash: `{result['catalogHash']}`  ", f"Hierarchy hash: `{result['hierarchyHash']}`", "", "Generated from canonical SemanticCatalog YAML. Navigation metadata review, semantic meaning approval, and query authorization are distinct. Exact registered source identities may be queried where separately authorized; pending semantic review does not authorize canonical equivalence.", "", "| Node ID | Parent | English | Persian | Tables | Navigation review | Query access |", "|---|---|---|---|---|---|---|"]
        for node in result["nodes"]:
            refs = ", ".join(node["catalogReferences"]["tableIds"]) or "—"
            rows.append(f"| `{node['nodeId']}` | `{node['parentNodeId'] or '—'}` | {node['title']['en']} | {node['title']['fa']} | {refs} | {node['navigationMetadataApproval']['state']} | {node['queryAuthorization']['state']} |")
        markdown.write_text(render_markdown(result), encoding="utf-8", newline="\n")
        print(f"Built {len(result['nodes'])} nodes ({result['catalogHash']})")
    elif args.area == "source-items" and args.action == "build":
        result = sync_source_item_dictionary()
        print(f"Built {len(result['entries'])} provider-scoped source-item entries; hierarchy regeneration is still required.")
    elif args.area == "source-items" and args.action == "review-report":
        result = build_source_item_unit_review_report()
        report = FEATURE / "source-item-unit-review-report.json"
        report.write_text(json.dumps(result, ensure_ascii=False, indent=2) + "\n", encoding="utf-8", newline="\n")
        rows = ["# Feature 141 Source Item Unit Review", "", f"Policy version: `{result['policyVersion']}`",
            f"Source item definitions inspected: {result['snapshotItemCount']}", "",
            "| Classification | Count |", "|---|---:|"]
        rows.extend(f"| {key} | {value} |" for key, value in result["classificationCounts"].items())
        rows.extend(["", "## Per-share items", "", "| Provider | Statement | Source item ID | Source catalog ID | Title | Effective unit | Rule |", "|---|---|---:|---|---|---|---|"])
        rows.extend(f"| {item['providerName']} | {item['statementType']} | {item['sourceItemId']} | `{item['sourceItemCatalogId']}` | {item['titleFa']} | {item['effectiveUnit']} | {item['reason']} |"
            for item in result["perShareItems"])
        rows.extend(["", "## Requires semantic review", "", "These items remain without an effective unit until reviewed.", "",
            "| Provider | Statement | Source item ID | Source catalog ID | Title | Raw unit | Reason |", "|---|---|---:|---|---|---|---|"])
        rows.extend(f"| {item['providerName']} | {item['statementType']} | {item['sourceItemId']} | `{item['sourceItemCatalogId']}` | {item['titleFa']} | {item['rawProviderUnit']} | {item['reason']} |"
            for item in result["ambiguousItems"])
        (FEATURE / "source-item-unit-review-report.md").write_text("\n".join(rows) + "\n", encoding="utf-8", newline="\n")
        print(f"Reviewed {result['snapshotItemCount']} source items; {len(result['ambiguousItems'])} require semantic review ({report.name}).")
    elif args.area == "validate":
        result = validate()
        print(f"Valid catalog package and generated artifacts ({result['catalogHash']}; {result['hierarchyHash']})")


if __name__ == "__main__":
    main()
