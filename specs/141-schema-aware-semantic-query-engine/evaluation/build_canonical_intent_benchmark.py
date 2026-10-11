#!/usr/bin/env python3
"""Build an immutable-source, correction-overlay benchmark for Feature 141 Option B."""

import argparse
import json
from pathlib import Path


METRIC_CONCEPTS = {
    "NET_PROFIT": "netProfit",
    "MONTHLY_SALES_GROWTH_YOY": "salesGrowth",
    "MONTHLY_PRODUCTION_QUANTITY": "productionQuantity",
    "MONTHLY_PRODUCTION_GROWTH_YOY": "productionGrowth",
    "MONTHLY_SALES": "salesAmount",
    "PE_TTM": "priceEarningsRatio",
    "LATEST_PRICE": "latestPrice",
}


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8"))


def build(root):
    evaluation = root / "specs/141-schema-aware-semantic-query-engine/evaluation"
    source_names = [
        ("release-readiness-dev-v1.json", "original50-development"),
        ("release-readiness-holdout-v2.json", "original50-historical-holdout-v2"),
        ("release-readiness-holdout-v3.json", "historical-holdout-v3"),
    ]
    manifest = read_json(evaluation / "canonical-intent-corrections-v2.json")
    corrections = {row["caseId"]: row for row in manifest["corrections"]}
    cases = []
    seen = set()
    for source_name, split in source_names:
        dataset = read_json(evaluation / source_name)
        for source_case in dataset["cases"]:
            case_id = source_case["id"]
            if case_id in seen:
                raise ValueError(f"Duplicate source case id: {case_id}")
            seen.add(case_id)
            correction = corrections.get(case_id)
            if correction:
                if correction["source"] != source_name:
                    raise ValueError(f"Correction source mismatch for {case_id}: {correction['source']} != {source_name}")
                expected = correction["expected"]
                expected_fields = correction["expectedFields"]
                gold_status = correction["status"]
                correction_reason = correction["reason"]
            else:
                source_metrics = source_case.get("metrics") or []
                concepts = [METRIC_CONCEPTS[item] for item in source_metrics if item in METRIC_CONCEPTS]
                # Existing labels are preserved as evidence, not silently promoted to adjudicated gold.
                expected = {}
                expected_fields = []
                gold_status = "inherited_measure_only_unreviewed" if concepts else "gold_adjudication_pending"
                correction_reason = None

            generic_support = str(source_case.get("genericSupport", "")).casefold()
            completeness = str(source_case.get("completeness", "")).casefold()
            if gold_status == "catalog_blocked_semantically_valid":
                catalog_status = "blocked_by_catalog_or_authorization"
            elif any(token in generic_support + " " + completeness for token in ("authorization pending", "query authorization pending", "coverage pending")):
                catalog_status = "catalog_or_authorization_blocked"
            elif "unsupported" in generic_support or "unsupported" in completeness:
                catalog_status = "unsupported_or_coverage_unverified"
            else:
                catalog_status = "not_adjudicated"

            cases.append({
                "id": case_id,
                "question": source_case["question"],
                "sourceDataset": source_name,
                "sourceDatasetVersion": dataset.get("datasetVersion"),
                "split": split,
                "historicalEvaluationStatus": "previously_used",
                "legacyExpectedMetrics": source_case.get("metrics", []),
                "goldStatus": gold_status,
                "catalogStatus": catalog_status,
                "expected": expected,
                "expectedFields": expected_fields,
                "correctionReason": correction_reason,
            })

    unknown_corrections = set(corrections) - seen
    if unknown_corrections:
        raise ValueError(f"Corrections refer to absent source cases: {sorted(unknown_corrections)}")
    return {
        "benchmarkVersion": 2,
        "purpose": "Canonical semantic intent; no physical schema, catalog reference, SQL, or execution outcome is part of gold intent.",
        "createdUtc": "2026-10-10",
        "sourceDatasets": [name for name, _ in source_names],
        "correctionManifest": "canonical-intent-corrections-v2.json",
        "historicalHoldoutsRemainHistorical": True,
        "caseCount": len(cases),
        "cases": cases,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", help="Output JSON path; defaults to canonical-intent-benchmark-v2.json")
    args = parser.parse_args()
    root = Path(__file__).resolve().parents[3]
    output = Path(args.output).resolve() if args.output else Path(__file__).with_name("canonical-intent-benchmark-v2.json")
    benchmark = build(root)
    output.parent.mkdir(parents=True, exist_ok=True)
    output.write_text(json.dumps(benchmark, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {benchmark['caseCount']} cases to {output}")
    print(f"Corrections applied: {len(json.loads((Path(__file__).with_name('canonical-intent-corrections-v2.json')).read_text(encoding='utf-8'))['corrections'])}")


if __name__ == "__main__":
    main()
