#!/usr/bin/env python3
"""Build the Feature 141 reliability review without changing frozen expectations."""
from __future__ import annotations

import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
SPEC = ROOT / "specs/141-schema-aware-semantic-query-engine"
EVAL = SPEC / "evaluation"
ART = SPEC / "artifacts"
OUT = EVAL / "feature141-navigator-reliability-v4-2026-10-09.md"


def read_json(path: Path):
    return json.loads(path.read_text(encoding="utf-8"))


def read_jsonl(path: Path):
    return [json.loads(line) for line in path.read_text(encoding="utf-8").splitlines() if line.strip()]


UNSUPPORTED_ROOTS = {
    "fa-07": "Genuine ambiguity before planning: the Persian question says ‘growth in sales’ without choosing sales quantity vs amount or MoM vs YoY. The extraction layer encoded this as unsupported instead of asking a typed clarification.",
    "fa-value-04": "Catalog evidence gap: direct cash-balance source identity exists in the baseline oracle, but unit/period semantics were not verified for a safe direct answer. Fail closed; this is not a compiler defect.",
    "fa-value-07": "Unsupported derived statement operation: operating-profit YoY growth needs a governed financial-statement growth measure/formula that the generic compiler does not authorize.",
    "readiness-dev-02": "Upstream classification defect candidate: the question asks for a direct net-profit value, while the extractor selected UnsupportedDerivedFinancialMetric. A directly mapped statement item still needs reviewed unit/query authorization; the old row lacks raw model output to distinguish semantic rejection from missing approval.",
    "readiness-dev-08": "Unsupported derived primary metric: per-share operating-profit growth has no approved derivation/source contract in the generic compiler.",
    "readiness-dev-09": "Unsupported derived primary metric: gross margin requires a governed numerator/denominator and approval not expressed by the current generic compiler.",
    "readiness-dev-10": "Unsupported scope/algebra: industry-wide P/E ranking is not expressed by the current generic financial-statement compiler. Do not route to monthly SQL or invent a cross-company ratio.",
    "readiness-dev-11": "Unsupported source/period algebra: a maximum daily close over a prior-month window is outside the monthly production/sales compiler's source and operation contract.",
    "readiness-dev-23": "Unsupported cross-measure algebra: production-growth and sales-growth alignment is not a governed metric; production and sales measures cannot be combined by inference.",
    "readiness-holdout-v2-08": "Upstream false-unsupported candidate: this is a direct monthly SalesAmount request with an explicit period. The typed normalizer now converts an unsupported label only when the selected catalog reference and DirectValue shape prove direct retrieval; no live post-fix result is available.",
    "readiness-holdout-v2-09": "Catalog evidence gap: cash balance is a direct financial-statement line, but baseline units/source approval were incomplete. A direct query must remain non-executable until that metadata is verified.",
    "readiness-holdout-v2-10": "Unsupported cross-measure algebra: production-vs-sales growth alignment has no approved operation and must not be synthesized.",
}


def escaped(value) -> str:
    return str(value if value is not None else "—").replace("|", "\\|").replace("\n", " ")


def main() -> None:
    dev = read_json(EVAL / "release-readiness-dev-v1.json")
    holdout = read_json(EVAL / "release-readiness-holdout-v2.json")
    dev_rows = read_jsonl(ART / "release-readiness-dev-live-v3-final-2026-10-09.jsonl")
    holdout_rows = read_jsonl(ART / "release-readiness-holdout-v2-live-2026-10-09.jsonl")
    datasets = {case["id"]: case for case in dev["cases"] + holdout["cases"]}
    rows = {row["caseId"]: row for row in dev_rows + holdout_rows}

    failures = [row for row in dev_rows + holdout_rows if row.get("status") != "completed"]
    lines = [
        "# Feature 141 Navigator Reliability Review — 2026-10-09",
        "",
        "## Readiness decision",
        "",
        "**Not production ready.** Candidate-constrained schemas, deterministic catalog membership checks, a versioned operation registry, typed timeout outcomes, and outline integrity caching are implemented. Release build, all 2,022 unit tests, catalog validation, and the restricted PostgreSQL regression passed. The required post-fix 50-question live LLM re-evaluation did not complete: the isolated `fa-04` live smoke produced no row and was stopped after remaining active beyond its case budget. The fresh holdout therefore remains unscored. No post-fix completion, semantic-accuracy, numeric-error, latency, token, or cost improvement is claimed.",
        "",
        "## Baseline: frozen original 50",
        "",
        "The original development and holdout datasets remain unchanged and are joined to the preserved pre-change captures. All 50 baseline attempts remain in the denominator.",
        "",
        "| Cohort | Attempts | Navigator response row (not a full answer) | Exact measure-set accuracy | Operation accuracy | Period-policy accuracy | SQL executed |",
        "|---|---:|---:|---:|---:|---:|---:|",
        "| Development | 40 | 16/40 (40.0%) | 22.2% | 0.0% | 7.7% | 0 |",
        "| Untouched holdout v2 | 10 | 7/10 (70.0%) | 33.3% | 0.0% | 0.0% | 0 |",
        "| Combined | 50 | 23/50 (46.0%) | Not pooled in the prior scorer | Not pooled | Not pooled | 0 |",
        "",
        "Baseline unconditional usage was 317,743 input + 36,305 output tokens / 60 calls / 450,674 ms in development and 94,766 input + 11,740 output tokens / 20 calls / 154,008 ms in holdout v2. Provider-reported cost was unavailable. Because these runs executed no SQL, incorrect numeric answers were 0 observed, but numeric correctness is otherwise not evaluable. Unsupported-vs-safe-refusal correctness was not independently scored in those artifacts. Baseline rows do not include raw failed extractor JSON or exact invalid IDs; do not infer the literal bad ID from `requirements_contain_unknown_catalog_id`.",
        "",
        "## Preserved earlier live-LLM + PostgreSQL fixture examples (pre-change)",
        "",
        "These examples come from the earlier 15-case restricted-fixture run and are not post-change evidence or Development financial data. They are included to retain inspectable Persian execution evidence; fixture values and source identities are recorded in the JSONL and the previous Persian report.",
        "",
        "| Persian question | Observed fixture answer | Earlier status |",
        "|---|---|---|",
        "| مقدار تولید گندله کگهر در شهریور ۱۴۰۵ چقدر بوده؟ | ۴۷ تن؛ report `fixture-kgehr-shahrivar-1405` | PASS |",
        "| تولید کنسانتره کگل نسبت به ماه قبل چند درصد رشد کرده؟ | دوره جاری ۱۲ تن، baseline ۶ تن؛ رشد ثبت‌شده ۱۰۰٪ | PASS |",
        "| فروش ریالی کگل در شهریور ۱۴۰۵ چقدر بوده؟ | ۸۵۰ میلیون ریال؛ report `fixture-kgel-shahrivar-1405` | PASS |",
        "| مقدار فروش گندله کگهر در آخرین گزارش ماهانه چقدر است؟ | ۴۰ تن؛ report `fixture-kgehr-shahrivar-1405` | PASS |",
        "| فروش ماهانه کگل و کگهر را با هم مقایسه کن. | کگل ۸۵۰ و کگهر ۷۰۰ میلیون ریال، همان reporting month; each report ID is in the fixture JSONL | PASS |",
        "",
        "Earlier non-successes from that same 15-case run: `monthly-09-cross-company-product-sales` (partial reviewed pellet alias coverage; ranking withheld), `monthly-11-industry-sales-growth-ranking` (typed ambiguity: industry and growth measure/period underspecified), `monthly-12-sales-vs-operating-profit-growth` (unsupported mixed algebra), `monthly-13-production-vs-sales-growth` (unsupported cross-measure relation), and `producer-16-total-company-sales-ranking` (partial producer alias coverage; exhaustive ranking withheld). A separate dynamic producer query matched its four-company fixture set exactly but reported incomplete catalog coverage. See [the earlier Persian report](persian-end-to-end-examples.md) and [the 15-case JSONL](../artifacts/monthly-production-sales-live-llm-postgresql-fifteen-cases-readiness-v2-2026-10-09.jsonl).",
        "",
        "### Post-change metrics (unconditional) — not measured",
        "",
        "| Metric | Original 50 | Fresh holdout v3 |",
        "|---|---:|---:|",
        "| Cases with completed post-change evaluation rows | Not measured; full run not completed | Not run |",
        "| Correct end-to-end answers / incorrect numeric answers | Not measured; no post-change SQL run | Not run |",
        "| Correct safe failures / unsupported-query count | Not measured | Not run |",
        "| Semantic selection / operation / period / unit accuracy | Not measured | Not run |",
        "| Dynamic company-set precision / recall | Not measured in the new run; prior fixture exact-set metrics remain separately reported | Not run |",
        "| Input/output tokens / provider cost / latency | Not measured; the smoke emitted no row | Not run |",
        "",
        "## Per-case diagnosis of all 27 baseline non-completions",
        "",
        "For the 12 `requirements_contain_unknown_catalog_id` rows, the exact case, question, and failure stage are captured, but the original JSON output and invalid identifier were not persisted. The observed stage rules out a PostgreSQL/compiler failure and the version-mismatch error was not reported. The surviving evidence cannot distinguish a hallucinated identifier from a field/reference-type mismatch against the outline. The fix now enum-constrains every selected ID to the current outline/context candidates and records the field/value on any deterministic membership failure.",
        "",
        "| Case | Original Persian question | Baseline failure/stage | Root-cause classification and evidence limit |",
        "|---|---|---|---|",
    ]

    for row in failures:
        case_id = row["caseId"]
        case = datasets.get(case_id, {})
        code = row.get("failureCode") or row.get("status")
        phase = row.get("phase") or "not recorded"
        if code == "requirements_contain_unknown_catalog_id":
            root = "Invalid catalog reference rejected during independent requirements extraction. The frozen artifact has no raw JSON/identifier, so hallucination vs reference-type/candidate mismatch cannot be further disambiguated. No SQL ran."
        elif code == "requirements_contain_unsupported_financial_value_operation":
            root = UNSUPPORTED_ROOTS.get(case_id, "Unsupported operation label at requirements extraction; raw model output was not retained.")
        elif row.get("status") == "blocked":
            root = "Provider/request stall: no response row or finish reason was captured. The baseline evidence cannot distinguish a network/provider stall from a stalled caller. No SQL result exists."
        elif code == "TaskCanceledException":
            root = "Cancellation/timeout with no preserved cancellation source or provider finish reason. It is not a semantic failure and no SQL ran. The updated Navigator now returns a typed timeout/cancellation stage, but live behavior remains unverified."
        else:
            root = escaped(row.get("failureDetail") or row.get("failureCategory") or "baseline failure; see source artifact")
        lines.append(f"| `{escaped(case_id)}` | {escaped(case.get('question'))} | `{escaped(code)}` / `{escaped(phase)}` | {root} |")

    lines.extend([
        "",
        "## Implemented generic changes",
        "",
        "- Requirements and navigation schemas now use candidate enums derived from the exact bounded outline or expanded context. Empty candidate sets allow only empty arrays. Deterministic validation remains as a second boundary and captures the invalid field and value.",
        "- Navigation `selectedNodeIds` and `expansionNodeIds` are constrained to actual current node IDs (including allowed direct-source parent/node anchors), not metric/dimension IDs. Existing fan-out, depth, node, call, and token budgets remain in force.",
        "- Added `Feature141OperationRegistry` version 1 and a narrow deterministic normalizer: a mislabeled unsupported operation is normalized only when there is exactly one approved typed catalog reference, a direct-value/time-series shape, and no comparison/derived intent. Ambiguous growth and unsupported derived measures are not rewritten as direct values.",
        "- Operation registry definitions now describe compiler support without duplicating/replacing downstream structural, authorization, unit, and coverage diagnostics. Existing planner/compiler checks remain authoritative for arguments and execution eligibility.",
        "- Provider calls are invoked away from the evaluator caller thread and bounded with `WaitAsync`; cancellation is represented as `SchemaNavigationTimeoutException` with code, stage, and reason. The evaluator records separate requirement/navigation/expansion/retry counts and stage latency fields. A successful provider finish reason is explicitly marked unavailable when not returned by the usage contract.",
        "- Canonical catalog integrity is validated once per immutable catalog identity per process. Requests still compare their requested catalog version/hash with the loaded hierarchy; the package is versioned and must be republished/restarted when catalog sources change.",
        "- Outline scoring reuses normalized question tokens and caches parsed metric semantics within the singleton hierarchy provider.",
        "",
        "## Post-change verification and live-evaluation status",
        "",
        "| Verification | Result | Scope/limit |",
        "|---|---|---|",
        "| Release solution build | PASS, 0 warnings / 0 errors | `dotnet build src/backend/FinancialCopilot.sln -c Release --no-restore` |",
        "| Release unit tests | PASS, 2,022/2,022 | Includes candidate-enum, deterministic invented-ID rejection, operation-registry, and non-cooperative-provider timeout regression |",
        "| Canonical catalog validation | PASS | Source hash `91bd31549418bdbd870b878079efa2808f9122d02cb7725aab5d4bab8595d1a2`; no approval changes |",
        "| Restricted PostgreSQL Feature 141 test | PASS, 1 test / 7 s | Isolated temporary PostgreSQL cluster; compiled monthly retrieval/time-series, product discovery, ranking/comparison assertions and independent fixture facts are covered by this integration test. The live LLM matrix flag was not enabled. |",
        "| Outline-only diagnostic, `fa-04` | PASS | 15,589 characters (16,000 bound), 12 hierarchy nodes, 8 direct-source candidates; this mode sends no LLM request and executes no SQL |",
        "| Live `fa-04` smoke after changes | BLOCKED | No evaluation row returned; process stayed active beyond its request budget and was manually stopped. Do not count as a successful or typed timeout result. |",
        "| Original 50 post-change | NOT RUN TO COMPLETION | Full benchmark was not launched after the live smoke failed to return. Completion/accuracy/incorrect-numeric/latency/token/cost deltas are unmeasured. |",
        "| Fresh untouched holdout v3 | CREATED BEFORE INFERENCE; NOT RUN | Ten novel Persian prompts with frozen expected semantics; no prediction was inspected. Keep v2 preserved. |",
        "",
        "## Product governance and security",
        "",
        "The reviewed product catalog and owner-approved `گندله` = `گندله سنگ‌آهن` equivalence are unchanged. The 4,934 pending product identities and 224 repeated-label/unit groups remain review candidates; none were auto-approved. No pellet producer registry or product-to-company code mapping was added. Dynamic producer discovery remains based on authorized persisted positive production observations with coverage status and source periods. Generic Development/production routing remains disabled, no production financial data was queried, and the PostgreSQL run used a test-owned isolated server.",
        "",
        "## Required next gate",
        "",
        "The live-provider post-change stall is the blocking release-readiness issue. Add per-stage progress capture around outline construction, schema serialization, provider transport, response validation, and context expansion; determine the live call stage; then rerun the exact frozen original 40 development + 10 v2 holdout cases and a separately frozen fresh holdout. Every attempt must emit a row even on timeout/cancellation. Do not claim the proposed 90% completion/semantic gates until those unconditional live metrics and an LLM-to-restricted-PostgreSQL subset are available.",
        "",
        "## Traceable artifacts",
        "",
        "- Original development questions: [release-readiness-dev-v1.json](release-readiness-dev-v1.json)",
        "- Original untouched holdout v2: [release-readiness-holdout-v2.json](release-readiness-holdout-v2.json)",
        "- Fresh untouched holdout v3 (not yet inferred): [release-readiness-holdout-v3.json](release-readiness-holdout-v3.json)",
        "- Development baseline raw rows: [release-readiness-dev-live-v3-final-2026-10-09.jsonl](../artifacts/release-readiness-dev-live-v3-final-2026-10-09.jsonl)",
        "- Holdout baseline raw rows: [release-readiness-holdout-v2-live-2026-10-09.jsonl](../artifacts/release-readiness-holdout-v2-live-2026-10-09.jsonl)",
        "- Baseline detailed report: [feature141-release-readiness-2026-10-09.md](feature141-release-readiness-2026-10-09.md)",
        "- Product coverage review: [product-catalog-coverage-review-2026-10-09.md](../product-catalog-coverage-review-2026-10-09.md)",
        "- Reviewer decisions: [product-equivalence-candidates-v1.csv](../ProductCatalog/reviewer-decisions/product-equivalence-candidates-v1.csv)",
        "- PostgreSQL harness: [run-isolated-postgresql-tests.ps1](../../../../tools/Feature141/run-isolated-postgresql-tests.ps1)",
        "",
    ])

    OUT.write_text("\n".join(lines), encoding="utf-8")
    print(f"Wrote {OUT.relative_to(ROOT)} with {len(failures)} baseline non-completions.")


if __name__ == "__main__":
    main()
