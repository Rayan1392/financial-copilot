#!/usr/bin/env python3
"""Freeze a separately authored Phase 1.6 first-pass holdout."""

import argparse
import hashlib
import json
from pathlib import Path

from build_canonical_intent_benchmark_v3 import SCORED_MEASURE_FIELDS, materialize_measures


def build(source_path):
    source_bytes = source_path.read_bytes()
    source = json.loads(source_bytes)
    cases = []
    seen = set()
    for item in source["cases"]:
        if item["id"] in seen:
            raise ValueError(f"Duplicate holdout ID: {item['id']}")
        seen.add(item["id"])
        expected = materialize_measures(item.get("measures", []))
        expected.update(item.get("expected", {}))
        expected_fields = [] if item["goldStatus"] == "genuinely_unsupported" else list(SCORED_MEASURE_FIELDS)
        if item["goldStatus"] != "genuinely_unsupported":
            expected_fields.extend(item.get("expected", {}).keys())
            if expected.get("measureResolution"):
                expected_fields.append("measureResolution")
        cases.append({
            "id": item["id"],
            "question": item["question"],
            "sourceQuestionSha256": hashlib.sha256(item["question"].encode("utf-8")).hexdigest(),
            "split": "phase1_6_independent_holdout_v2",
            "historicalEvaluationStatus": "never_evaluated_before_freeze",
            "goldStatus": item["goldStatus"],
            "expected": expected,
            "expectedFields": expected_fields,
            "expectedOptionBFailureCategory": item.get("expectedOptionBFailureCategory"),
        })
    unsupported = sum(row["goldStatus"] == "genuinely_unsupported" for row in cases)
    ambiguous = sum(row["goldStatus"] == "genuinely_ambiguous" for row in cases)
    adjudicated = sum(row["goldStatus"] == "adjudicated_unambiguous" for row in cases)
    if len(cases) != 12 or unsupported != 1 or ambiguous != 1 or adjudicated != 10:
        raise ValueError("Phase 1.6 holdout must contain 10 unambiguous, 1 ambiguous, and 1 unsupported case.")
    return {
        "benchmarkVersion": 2,
        "purpose": "Independent first-pass Option B Phase 1.6 financial semantic precision evaluation.",
        "createdUtc": source["createdUtc"],
        "sourceManifest": source_path.name,
        "sourceSha256": hashlib.sha256(source_bytes).hexdigest().upper(),
        "authorship": source["authorship"],
        "sealPolicy": source["sealPolicy"],
        "caseCount": len(cases),
        "adjudicatedCaseCount": adjudicated,
        "ambiguousCaseCount": ambiguous,
        "unsupportedCaseCount": unsupported,
        "cases": cases,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--source", default="canonical-intent-phase1_6-independent-holdout.source.json")
    parser.add_argument("--output", default="canonical-intent-phase1_6-independent-holdout-v2.json")
    args = parser.parse_args()
    here = Path(__file__).resolve().parent
    source_path = Path(args.source)
    output_path = Path(args.output)
    if not source_path.is_absolute():
        source_path = here / source_path
    if not output_path.is_absolute():
        output_path = here / output_path
    output_path.write_text(json.dumps(build(source_path), ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Frozen Phase 1.6 holdout: {output_path}")


if __name__ == "__main__":
    main()
