#!/usr/bin/env python3
"""Materialize the frozen, source-only Phase 1.5 independent semantic holdout."""

import argparse
import hashlib
import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from build_canonical_intent_benchmark_v3 import SCORED_MEASURE_FIELDS, materialize_measures


def build(source_path):
    source = json.loads(source_path.read_text(encoding="utf-8"))
    cases = []
    ids = set()
    for row in source["cases"]:
        if row["id"] in ids:
            raise ValueError(f"Duplicate holdout ID: {row['id']}")
        ids.add(row["id"])
        expected = materialize_measures(row.get("measures", []))
        expected.update(row.get("expected", {}))
        fields = []
        if row.get("goldStatus") not in {"genuinely_unsupported"}:
            fields = list(SCORED_MEASURE_FIELDS) + list(row.get("expected", {}).keys())
            if expected.get("measureResolution"):
                fields.append("measureResolution")
        cases.append({
            "id": row["id"],
            "question": row["question"],
            "sourceQuestionSha256": hashlib.sha256(row["question"].encode("utf-8")).hexdigest(),
            "split": "independent_holdout_v1",
            "historicalEvaluationStatus": "never_evaluated_before_freeze",
            "goldStatus": row["goldStatus"],
            "expected": expected,
            "expectedFields": fields,
            "expectedOptionBFailureCategory": row.get("expectedOptionBFailureCategory"),
        })
    if len(cases) != 12:
        raise ValueError(f"Expected exactly 12 frozen holdout cases, got {len(cases)}")
    return {
        "benchmarkVersion": 1,
        "purpose": "Untouched first-pass canonical semantic intent evaluation; gold and questions frozen before model predictions.",
        "createdUtc": source["createdUtc"],
        "sourceManifest": source_path.name,
        "authorship": source["authorship"],
        "sealPolicy": source["sealPolicy"],
        "caseCount": len(cases),
        "adjudicatedCaseCount": sum(row["goldStatus"] == "adjudicated_unambiguous" for row in cases),
        "ambiguousCaseCount": sum(row["goldStatus"] == "genuinely_ambiguous" for row in cases),
        "unsupportedCaseCount": sum(row["goldStatus"] == "genuinely_unsupported" for row in cases),
        "cases": cases,
    }


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", help="Defaults to canonical-intent-independent-holdout-v1.json")
    args = parser.parse_args()
    source = Path(__file__).with_name("canonical-intent-independent-holdout-v1.source.json")
    output = Path(args.output).resolve() if args.output else Path(__file__).with_name("canonical-intent-independent-holdout-v1.json")
    benchmark = build(source)
    output.write_text(json.dumps(benchmark, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote {benchmark['caseCount']} cases; {benchmark['adjudicatedCaseCount']} unambiguous, {benchmark['ambiguousCaseCount']} ambiguous, {benchmark['unsupportedCaseCount']} unsupported: {output}")


if __name__ == "__main__":
    main()
