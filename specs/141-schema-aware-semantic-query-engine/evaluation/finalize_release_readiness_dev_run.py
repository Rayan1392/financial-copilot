#!/usr/bin/env python3
"""Join one Release-build dev run captured in bounded batches, preserving blocked calls."""
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
SPEC = ROOT / "specs/141-schema-aware-semantic-query-engine"
DATASET = SPEC / "evaluation/release-readiness-dev-v1.json"
ART = SPEC / "artifacts"
OUTPUT = ART / "release-readiness-dev-live-v3-final-2026-10-09.jsonl"
SOURCES = [
    ART / "release-readiness-dev-live-v3-2026-10-09.jsonl",
    ART / "release-readiness-dev-live-v3c-2026-10-09.jsonl",
    ART / "release-readiness-dev-live-v3d-2026-10-09.jsonl",
]
STALLED_CASES = {"fa-value-05", "readiness-dev-07"}


def main():
    cases = json.loads(DATASET.read_text(encoding="utf-8"))["cases"]
    rows = {}
    for source in SOURCES:
        for line in source.read_text(encoding="utf-8").splitlines():
            if not line.strip():
                continue
            row = json.loads(line)
            case_id = row["caseId"]
            if case_id in rows:
                raise ValueError(f"duplicate captured case: {case_id}")
            rows[case_id] = row
    by_id = {case["id"]: case for case in cases}
    for case_id in sorted(STALLED_CASES):
        if case_id in rows:
            continue
        rows[case_id] = {
            "schemaVersion": 1,
            "caseId": case_id,
            "question": by_id[case_id]["question"],
            "status": "blocked",
            "phase": "live_llm_request",
            "failureCode": "no_response_row_after_stalled_request",
            "diagnostic": "The live Release-build evaluation did not return a row for this case. The request process was stopped after it stopped making progress; no LLM output, intent, plan, SQL, or answer was received. This is recorded as BLOCKED, not as a provider exception or a model result.",
            "provider": "OpenAI",
            "model": "gpt-5.6-luna",
            "secretValuesLogged": False,
        }
    missing = set(by_id) - set(rows)
    extra = set(rows) - set(by_id)
    if missing or extra:
        raise ValueError(f"dataset/prediction mismatch: missing={sorted(missing)}; extra={sorted(extra)}")
    OUTPUT.write_text("\n".join(json.dumps(rows[case["id"]], ensure_ascii=False) for case in cases) + "\n",
                      encoding="utf-8")
    print(f"Wrote all {len(rows)} development outcomes to {OUTPUT}; blocked rows={len(STALLED_CASES)}.")


if __name__ == "__main__":
    main()
