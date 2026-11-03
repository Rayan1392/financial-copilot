#!/usr/bin/env python3
"""Build the v3 canonical semantic-gold overlay without changing historical sources."""

import argparse
import hashlib
import json
from pathlib import Path


DIMENSIONS = {
    "productionQuantity": "physicalQuantity",
    "salesQuantity": "physicalQuantity",
    "salesAmount": "currencyAmount",
    "salesRate": "currencyPerUnit",
    "operatingProfit": "currencyAmount",
    "operatingProfitPerShare": "currencyPerShare",
    "netProfit": "currencyAmount",
    "earningsPerShare": "currencyPerShare",
    "cashBalance": "currencyAmount",
    "depreciationExpense": "currencyAmount",
    "netProfitMargin": "percentage",
    "grossProfitMargin": "percentage",
    "priceEarningsRatio": "ratio",
    "latestPrice": "currencyAmount",
}

SCORED_MEASURE_FIELDS = (
    "measureConcepts", "measureQualifiers", "unitDimensions",
    "requestedUnits", "effectiveSourceUnits",
)


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8"))


def materialize_measures(measures):
    expected = {
        "measureConcepts": [],
        "measureResolution": [],
        "measureQualifiers": [],
        "unitDimensions": [],
        "requestedUnits": [],
        "effectiveSourceUnits": [],
    }
    for item in measures:
        concept = item.get("conceptId")
        if concept is None:
            expected["measureResolution"].append({
                "userPhrase": item["userPhrase"],
                "status": item["resolutionStatus"],
                "candidateConceptIds": item.get("candidateConceptIds", []),
            })
            continue
        if concept not in DIMENSIONS:
            raise ValueError(f"No governed unit dimension for canonical concept: {concept}")
        expected["measureConcepts"].append(concept)
        expected["measureQualifiers"].append({
            "conceptId": concept,
            "valueForm": item.get("valueForm", "reportedValue"),
            "reportingBasis": item.get("reportingBasis", "unspecified"),
            "statementScope": item.get("statementScope", "unspecified"),
            "valueGrain": item.get("valueGrain", "unspecified"),
        })
        expected["unitDimensions"].append({"conceptId": concept, "value": DIMENSIONS[concept]})
        expected["requestedUnits"].append({"conceptId": concept, "value": item.get("requestedUnit", "unspecified")})
        expected["effectiveSourceUnits"].append({"conceptId": concept, "value": None, "evidence": "unresolved"})
    return expected


def build(root):
    evaluation = root / "specs/141-schema-aware-semantic-query-engine/evaluation"
    source_names = [
        ("release-readiness-dev-v1.json", "original50-development"),
        ("release-readiness-holdout-v2.json", "original50-historical-holdout-v2"),
        ("release-readiness-holdout-v3.json", "historical-holdout-v3"),
    ]
    manifest = read_json(evaluation / "canonical-intent-adjudications-v3.json")
    adjudications = {item["caseId"]: item for item in manifest["cases"]}
    cases = []
    source_by_id = {}
    for source_name, split in source_names:
        dataset = read_json(evaluation / source_name)
        for source_case in dataset["cases"]:
            case_id = source_case["id"]
            if case_id in source_by_id:
                raise ValueError(f"Duplicate source case ID: {case_id}")
            source_by_id[case_id] = (source_name, source_case, dataset, split)

    unknown_ids = set(adjudications) - set(source_by_id)
    if unknown_ids:
        raise ValueError(f"Adjudications refer to absent source IDs: {sorted(unknown_ids)}")

    for case_id, (source_name, source_case, dataset, split) in source_by_id.items():
        adjudication = adjudications.get(case_id)
        expected = {}
        expected_fields = []
        if adjudication:
            if adjudication["source"] != source_name:
                raise ValueError(f"Adjudication source mismatch for {case_id}")
            if adjudication.get("status") != "disputed":
                expected = materialize_measures(adjudication.get("measures", []))
                expected.update(adjudication.get("expected", {}))
                expected_fields = list(SCORED_MEASURE_FIELDS) + list(adjudication.get("expected", {}).keys())
                if expected.get("measureResolution"):
                    expected_fields.append("measureResolution")

        question_hash = hashlib.sha256(source_case["question"].encode("utf-8")).hexdigest()
        cases.append({
            "id": case_id,
            "question": source_case["question"],
            "sourceDataset": source_name,
            "sourceDatasetVersion": dataset.get("datasetVersion"),
            "sourceQuestionSha256": question_hash,
            "split": split,
            "historicalEvaluationStatus": "previously_used",
            "legacyExpectedMetrics": source_case.get("metrics", []),
            "goldStatus": adjudication["status"] if adjudication else "gold_adjudication_pending",
            "catalogStatus": "not_adjudicated",
            "expected": expected,
            "expectedFields": expected_fields,
            "correctionReason": adjudication.get("rationale") if adjudication else None,
            "expectedOptionBFailureCategory": adjudication.get("expectedOptionBFailureCategory") if adjudication else None,
            "reviewProvenance": {
                "reviewer": manifest["reviewer"],
                "predictionBlindReview": adjudication.get("predictionBlindReview") if adjudication else None,
                "sourceDataset": source_name,
                "sourceQuestionSha256": question_hash,
            } if adjudication else None,
        })

    if len(adjudications) != len(manifest["cases"]):
        raise ValueError("Duplicate adjudication case ID")
    exact_gold = sum(item["goldStatus"] in {"adjudicated_unambiguous", "genuinely_ambiguous", "genuinely_unsupported", "catalog_blocked_semantically_valid"} for item in cases)
    if exact_gold < 30:
        raise ValueError(f"Expected at least 30 adjudicated cases; found {exact_gold}")
    if sum(item["goldStatus"] == "disputed" for item in cases) < 1:
        raise ValueError("Expected disputed gold to remain explicitly represented")

    return {
        "benchmarkVersion": 3,
        "purpose": "Canonical business concepts, per-measure qualifiers, requested/source units, operation, period, and entity semantics; no physical schema or query result is gold.",
        "createdUtc": manifest["createdUtc"],
        "sourceDatasets": [name for name, _ in source_names],
        "adjudicationManifest": "canonical-intent-adjudications-v3.json",
        "reviewer": manifest["reviewer"],
        "reviewMethod": manifest["reviewMethod"],
        "historicalHoldoutsRemainHistorical": True,
        "caseCount": len(cases),
        "adjudicatedCaseCount": exact_gold,
        "disputedCaseCount": sum(item["goldStatus"] == "disputed" for item in cases),
        "cases": cases,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", help="Defaults to canonical-intent-benchmark-v3.json")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[3]
    output = Path(args.output).resolve() if args.output else Path(__file__).with_name("canonical-intent-benchmark-v3.json")
    benchmark = build(root)
    output.write_text(json.dumps(benchmark, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {benchmark['caseCount']} cases; {benchmark['adjudicatedCaseCount']} adjudicated, {benchmark['disputedCaseCount']} disputed: {output}")


if __name__ == "__main__":
    main()
