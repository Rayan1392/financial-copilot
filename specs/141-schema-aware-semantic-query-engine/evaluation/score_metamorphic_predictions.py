#!/usr/bin/env python3
"""Score live metamorphic monthly-query predictions without making model calls."""

from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any


def load_predictions(path: Path) -> dict[str, dict[str, Any]]:
    rows: dict[str, dict[str, Any]] = {}
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        if line.strip():
            row = json.loads(line)
            rows[row["caseId"]] = row
    return rows


def signature(row: dict[str, Any]) -> tuple[Any, ...]:
    return tuple(json.dumps(row.get(key), ensure_ascii=False, sort_keys=True)
                 for key in ("selectedMeasureIds", "financialValueOperation", "periodPolicies",
                             "comparisonScopes", "presentationRequirements", "entityReferences"))


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--dataset", required=True, type=Path)
    parser.add_argument("--predictions", required=True, type=Path)
    parser.add_argument("--output", type=Path)
    args = parser.parse_args()
    cases = json.loads(args.dataset.read_text(encoding="utf-8-sig"))["cases"]
    predictions = load_predictions(args.predictions)
    per_case = []
    groups: dict[str, list[dict[str, Any]]] = {}
    completed = 0
    exact_measure = 0
    for case in cases:
        row = predictions.get(case["id"])
        status = row.get("status", "missing_prediction") if row else "missing_prediction"
        actual = sorted(row.get("selectedMeasureIds", row.get("metricIds", []))) if row else []
        expected = sorted(case.get("expectedMetrics", []))
        correct = status == "completed" and actual == expected
        completed += status == "completed"
        exact_measure += correct
        per_case.append({"caseId": case["id"], "status": status, "expectedMeasureIds": expected,
                         "selectedMeasureIds": actual, "exactMeasureSet": correct,
                         "failureCode": row.get("failureCode") if row else "missing_prediction"})
        if case.get("group"):
            groups.setdefault(case["group"], []).append({"case": case, "row": row})

    group_results = []
    for group, items in sorted(groups.items()):
        relation = items[0]["case"].get("relation")
        observed = [item["row"] for item in items]
        complete = all(row is not None and row.get("status") == "completed" for row in observed)
        if relation == "equivalent":
            matched = complete and len({signature(row) for row in observed}) == 1
            check = "full_selected-plan-signature-equality"
        else:
            field = items[0]["case"].get("contrastField", "measure-set")
            if not complete or len(observed) < 2:
                matched = False
            elif field == "entity-count":
                counts = [len(row.get("entityReferences") or []) for row in observed]
                matched = len(set(counts)) == len(counts)
            elif field == "presentation":
                values = [json.dumps(row.get("presentationRequirements"), sort_keys=True) for row in observed]
                matched = len(set(values)) == len(values)
            else:
                values = [tuple(sorted(row.get("selectedMeasureIds", row.get("metricIds", [])))) for row in observed]
                matched = len(set(values)) == len(values)
            check = field
        group_results.append({"group": group, "relation": relation, "caseIds": [item["case"]["id"] for item in items],
                              "allCompleted": complete, "metamorphicCheck": check, "passed": bool(matched)})

    result = {"datasetCaseCount": len(cases), "attemptedCaseCount": len(predictions),
              "completedCaseCount": completed, "failedOrMissingCaseCount": len(cases) - completed,
              "unconditionalCompletionRate": completed / len(cases) if cases else 0,
              "exactMeasureCases": exact_measure,
              "unconditionalExactMeasureRate": exact_measure / len(cases) if cases else 0,
              "metamorphicGroupCount": len(group_results),
              "metamorphicGroupsPassed": sum(item["passed"] for item in group_results),
              "perCase": per_case, "metamorphicGroups": group_results}
    rendered = json.dumps(result, ensure_ascii=False, indent=2) + "\n"
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(rendered, encoding="utf-8")
    else:
        print(rendered, end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
