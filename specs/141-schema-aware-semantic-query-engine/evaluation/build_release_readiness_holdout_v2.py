#!/usr/bin/env python3
"""Create and freeze a fresh final-build holdout before it is sent to the LLM."""
import copy
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
EVAL = ROOT / "specs/141-schema-aware-semantic-query-engine/evaluation"
DEV = json.loads((EVAL / "release-readiness-dev-v1.json").read_text(encoding="utf-8"))["cases"]
PREVIOUS_HOLDOUT = json.loads((EVAL / "release-readiness-holdout-v1.json").read_text(encoding="utf-8"))["cases"]
ALL_QUESTIONS = {x["question"] for x in DEV + PREVIOUS_HOLDOUT}

CASES = [
    ("readiness-holdout-v2-01", "کدام شرکت‌ها در گزارش شهریور ۱۴۰۵ تولید مثبت گندله سنگ‌آهن داشته‌اند؟", "readiness-dev-21", "population-discovery"),
    ("readiness-holdout-v2-02", "در صنعت معدن، کدام شرکت‌ها در آخرین گزارش ماهانه گندله تولید کرده‌اند؟", "readiness-dev-21", "industry-discovery"),
    ("readiness-holdout-v2-03", "کدام تولیدکنندگان در آخرین گزارش، بیش از ۳۰ تن گندله فروخته‌اند؟", "readiness-dev-22", "sales-threshold-population"),
    ("readiness-holdout-v2-04", "تولیدکنندگان گندله را بر پایه فروش کل شرکت در شهریور ۱۴۰۵ از بیشترین به کمترین مرتب کن.", "readiness-dev-19", "population-ranking"),
    ("readiness-holdout-v2-05", "کدام محصولات کگهر در سه ماه اخیر بیشترین رشد تولید ماهانه را داشته‌اند؟", "fa-04", "product-ranking-growth"),
    ("readiness-holdout-v2-06", "تولید کگل در شهریور ۱۴۰۵ نسبت به ماه مشابه سال قبل چند درصد تغییر کرد؟", "readiness-dev-15", "year-over-year"),
    ("readiness-holdout-v2-07", "فروش ریالی کگل را برای شش گزارش ماهانه اخیر به ترتیب زمان نشان بده.", "fa-03", "monthly-sales-time-series"),
    ("readiness-holdout-v2-08", "فروش ریالی کگل در شهریور ۱۴۰۵ چه مبلغی ثبت شد؟", "readiness-dev-17", "monthly-sales-amount"),
    ("readiness-holdout-v2-09", "مانده وجه نقد کگهر در پایان سال مالی ۱۴۰۴ چقدر گزارش شده است؟", "fa-value-04", "financial-statements"),
    ("readiness-holdout-v2-10", "آیا رشد تولید و رشد فروش کگهر در آخرین دوره هم‌جهت بوده‌اند؟", "readiness-dev-23", "ambiguous-cross-measure"),
]


def main():
    by_id = {x["id"]: x for x in DEV + PREVIOUS_HOLDOUT}
    cases = []
    for case_id, question, template_id, category in CASES:
        if question in ALL_QUESTIONS:
            raise ValueError(f"holdout question duplicates an earlier dataset: {case_id}")
        row = copy.deepcopy(by_id[template_id])
        row["id"] = case_id
        row["question"] = question
        row["readinessCategory"] = category
        row["split"] = "holdout-v2"
        cases.append(row)
    payload = {"datasetVersion": "feature141-final-build-holdout-v2",
               "split": "untouched-holdout-v2", "createdBeforeInference": True,
               "caseCount": len(cases), "cases": cases}
    path = EVAL / "release-readiness-holdout-v2.json"
    path.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Created frozen holdout with {len(cases)} distinct questions before inference: {path}")


if __name__ == "__main__":
    main()
