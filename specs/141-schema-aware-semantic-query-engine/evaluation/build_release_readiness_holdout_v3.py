#!/usr/bin/env python3
"""Freeze an independent post-change holdout before any inference is run."""
from __future__ import annotations

import copy
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
EVAL = ROOT / "specs/141-schema-aware-semantic-query-engine/evaluation"

QUESTIONS = [
    ("readiness-holdout-v3-01", "در گزارش مرداد ۱۴۰۵، کدام شرکت‌ها گندله تولید مثبت داشته‌اند؟", "readiness-holdout-v2-01", "periodic-product-discovery"),
    ("readiness-holdout-v3-02", "کدام شرکت‌های صنعت معدن در تازه‌ترین گزارش، گندله تولید کرده‌اند؟", "readiness-holdout-v2-02", "industry-product-discovery"),
    ("readiness-holdout-v3-03", "در آخرین گزارش ماهانه، کدام تولیدکنندگان گندله بیش از ۳۰ تن فروش داشته‌اند؟", "readiness-holdout-v2-03", "sales-threshold-population"),
    ("readiness-holdout-v3-04", "شرکت‌های تولیدکننده گندله را بر پایه مجموع تولید سه ماه اخیر از بیشترین به کمترین مرتب کن.", "readiness-holdout-v2-04", "population-production-ranking"),
    ("readiness-holdout-v3-05", "در سه ماه اخیر کدام محصولات کگهر بیشترین رشد مقدار تولید ماهانه را داشته‌اند؟", "readiness-holdout-v2-05", "product-ranking-growth"),
    ("readiness-holdout-v3-06", "تولید گندله کگل در شهریور ۱۴۰۵ نسبت به شهریور سال قبل چند درصد تغییر داشته است؟", "readiness-holdout-v2-06", "year-over-year"),
    ("readiness-holdout-v3-07", "مبلغ فروش ریالی کگل را برای شش گزارش ماهانه آخر به ترتیب ماه نشان بده.", "readiness-holdout-v2-07", "monthly-sales-time-series"),
    ("readiness-holdout-v3-08", "مبلغ فروش ریالی کگل در مرداد ۱۴۰۵ چقدر ثبت شده است؟", "readiness-holdout-v2-08", "monthly-sales-amount"),
    ("readiness-holdout-v3-09", "وجه نقد پایان سال مالی ۱۴۰۴ کگهر چقدر گزارش شده است؟", "readiness-holdout-v2-09", "financial-statement-direct-value"),
    ("readiness-holdout-v3-10", "آیا رشد تولید و مقدار فروش کگهر در دوره اخیر یکسان بوده است؟", "readiness-holdout-v2-10", "cross-measure-ambiguity"),
]


def main() -> None:
    dev = json.loads((EVAL / "release-readiness-dev-v1.json").read_text(encoding="utf-8"))["cases"]
    holdout_v1 = json.loads((EVAL / "release-readiness-holdout-v1.json").read_text(encoding="utf-8"))["cases"]
    holdout_v2 = json.loads((EVAL / "release-readiness-holdout-v2.json").read_text(encoding="utf-8"))["cases"]
    source_cases = dev + holdout_v1 + holdout_v2
    prior_questions = {case["question"] for case in source_cases}
    by_id = {case["id"]: case for case in source_cases}
    result = []
    for case_id, question, template_id, category in QUESTIONS:
        if question in prior_questions or any(item["question"] == question for item in result):
            raise ValueError(f"holdout question is not novel: {case_id}")
        case = copy.deepcopy(by_id[template_id])
        case.update(id=case_id, question=question, split="untouched-holdout-v3", readinessCategory=category)
        result.append(case)

    payload = {
        "datasetVersion": "feature141-post-change-untouched-holdout-v3",
        "split": "untouched-holdout-v3",
        "createdBeforeInference": True,
        "expectedIntentSource": "independent annotations copied from the pre-existing frozen semantic oracle by matching task algebra; no v3 model predictions have been inspected",
        "caseCount": len(result),
        "cases": result,
    }
    path = EVAL / "release-readiness-holdout-v3.json"
    path.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Created frozen, novel holdout with {len(result)} questions before inference: {path}")


if __name__ == "__main__":
    main()
