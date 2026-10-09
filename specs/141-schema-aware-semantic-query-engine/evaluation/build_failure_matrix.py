"""Create a readable per-case root-cause matrix from immutable eval inputs."""
from __future__ import annotations

import argparse
import json
from pathlib import Path


def load_json_lines(path: Path) -> dict[str, dict]:
    return {row["caseId"]: row for line in path.read_text(encoding="utf-8").splitlines()
            if line.strip() for row in [json.loads(line)]}


def refs(row: dict) -> dict[str, list[str]]:
    financial = row.get("financialValueReferences") or []
    financial_ids = [f"{item.get('referenceType')}:{item.get('catalogId')}"
                     for item in financial if item.get("catalogId")]
    return {
        "N": list(row.get("selectedNodeIds") or row.get("nodeIds") or row.get("requiredNodeIds") or []),
        "M": list(row.get("metricIds") or row.get("metrics") or []),
        "D": list(row.get("dimensionIds") or row.get("dimensions") or []),
        "R": list(row.get("relationshipIds") or row.get("relationships") or []),
        "F": financial_ids,
    }


def compact(values: list[str]) -> str:
    if not values:
        return "—"
    shown = values[:6]
    suffix = f", … (+{len(values) - len(shown)})" if len(values) > len(shown) else ""
    return ", ".join(shown) + suffix


def root_cause(case: dict, prediction: dict | None, missing: dict[str, list[str]]) -> tuple[str, str]:
    if not prediction or prediction.get("status") != "completed":
        code = (prediction or {}).get("failureCode") or "no prediction"
        return f"Budget/provider failure ({code})", "Reduce repeated prompt/context payload; retain typed failure diagnostics and retry bounds."
    diagnostics = " ".join(prediction.get("diagnostics") or []).lower()
    independent = prediction.get("independentRequirements") or {}
    if "unknown_node" in diagnostics or "output_not_object" in diagnostics or "missing_property" in diagnostics:
        return "LLM structured-output validation", "Keep strict ID validation and shorten the compact output contract/prompt."
    if missing["F"]:
        return "Financial concept/reference resolution", "Use provider and statement constrained title/alias ranking; preserve reference type and identity."
    if missing["M"]:
        if any(metric not in (independent.get("measureIds") or []) for metric in missing["M"]):
            return "Independent requirements extraction", "Resolve Persian terminology against reviewed catalog aliases before navigation."
        return "Hierarchy selection or expansion", "Seed the most specific nodes for independent references and expand their cataloged ancestors/relations."
    if missing["D"] or missing["R"] or missing["N"]:
        return "Multi-branch hierarchy recall", "Prioritize required shared dimensions and cross-domain references under the bounded fan-out."
    if not prediction.get("retrievalComplete", False):
        return "Incomplete requirements or catalog metadata", "Report the exact missing/ambiguous requirement separately from governance."
    if prediction.get("semanticReviewApproved") is False or prediction.get("queryAuthorized") is False:
        return "Governance only; navigation succeeded", "Keep approval and query authorization as separate gates; do not count as retrieval failure."
    return "No labeled navigation miss", "No correction indicated by this case."


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--dataset", required=True, type=Path)
    parser.add_argument("--predictions", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    dataset = json.loads(args.dataset.read_text(encoding="utf-8"))
    predictions = load_json_lines(args.predictions)
    lines = [
        "# Feature 141 Baseline Per-Case Failure Matrix (2026-10-08)", "",
        "This matrix compares the immutable pre-optimization Golden predictions with the catalog-derived labels. Navigation quality is assessed separately from semantic approval and query authorization. `F` uses typed `ReferenceType:CatalogId` values.", "",
        "| Case | Expected references (N/M/D/R/F) | Actual references (N/M/D/R/F) | Missing | Root cause | General correction |",
        "|---|---|---|---|---|---|",
    ]
    for case in dataset["cases"]:
        case_id = case.get("id") or case.get("caseId")
        prediction = predictions.get(case_id)
        expected = refs(case)
        actual = refs(prediction or {})
        missing = {
            key: ([value for value in expected[key] if value not in actual[key]]
                  if key != "R" or all(value.startswith("rel_") for value in expected[key]) else [])
            for key in expected
        }
        category, correction = root_cause(case, prediction, missing)
        if prediction and prediction.get("retrievalComplete") and prediction.get("semanticReviewApproved") is False:
            category += "; governance pending"
        expected_text = "<br>".join(f"{key}: {compact(expected[key])}" for key in expected)
        actual_text = "<br>".join(f"{key}: {compact(actual[key])}" for key in actual)
        missing_text = "<br>".join(f"{key}: {compact(values)}" for key, values in missing.items() if values) or "—"
        lines.append(f"| `{case_id}` | {expected_text} | {actual_text} | {missing_text} | {category} | {correction} |")
    lines += ["", "## Interpretation", "",
              "The original run's two `request_input_token_budget_exceeded` cases are budget failures. Cases with `retrievalComplete=true` but pending approvals are governance outcomes and are not retrieval failures. The root-cause field prioritizes concrete reference gaps; the remaining completeness and governance diagnostics should be read alongside each prediction row.", ""]
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text("\n".join(lines), encoding="utf-8")


if __name__ == "__main__":
    main()
