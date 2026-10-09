#!/usr/bin/env python3
"""Build fixed Feature 141 readiness dev and holdout question sets."""
from __future__ import annotations

import copy
import json
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3]
EVAL = ROOT / "specs" / "141-schema-aware-semantic-query-engine" / "evaluation"
SOURCE = EVAL / "navigation-golden-v1.yaml"
DEV = EVAL / "release-readiness-dev-v1.json"
HOLDOUT = EVAL / "release-readiness-holdout-v1.json"

# New question wording is declared before any predictions are collected. The held-out
# entries are emitted to a separate file and are not read by the dev scoring command.
DEV_VARIANTS = [
    ("fa-04", "financial-statements", "آخرین گزارش جداگانه فولاد چه میزان سود خالص ثبت کرده است؟"),
    ("fa-04", "financial-statements", "سود خالص شرکت فولاد در آخرین صورت مالی چقدر گزارش شده؟"),
    ("fa-05", "financial-statements", "در گزارش تلفیقی فولاد، رقم سود خالص چقدر است؟"),
    ("fa-06", "financial-statements", "سود خالص جداگانه و تلفیقی فولاد را کنار هم مقایسه کن."),
    ("fa-value-01", "financial-statements", "آخرین مقدار گزارش‌شده سود عملیاتی هر سهم کگل چیست؟"),
    ("fa-value-04", "financial-statements", "موجودی وجه نقد کگهر در پایان سال ۱۴۰۴ چه مبلغی بود؟"),
    ("fa-value-05", "financial-statements", "هزینه استهلاک فولاد را از آخرین صورت سود و زیان پیدا کن."),
    ("fa-value-07", "financial-statements", "سود عملیاتی هر سهم کگل نسبت به سال قبل چند درصد تغییر کرد؟"),
    ("fa-value-08", "financial-statements", "حاشیه سود ناخالص کگهر در تازه‌ترین گزارش چند درصد است؟"),
    ("fa-08", "financial-analytics", "نسبت قیمت به درآمد شرکت‌های صنعت فولاد را در یک جدول مقایسه کن."),
    ("fa-09", "market-data", "بیشترین قیمت پایانی کگل در ماه گذشته چه بوده است؟"),
    ("fa-01", "population-discovery", "در سال اخیر کدام شرکت‌ها بر اساس گزارش مثبت، گندله تولید کرده‌اند؟"),
    ("fa-01", "population-discovery", "شرکت‌های دارای تولید مثبت گندله سنگ‌آهن در شهریور ۱۴۰۵ را پیدا کن."),
    ("fa-01", "monthly-production", "مقدار تولید گندله کگهر در ماه شهریور ۱۴۰۵ چقدر ثبت شده؟"),
    ("fa-02", "monthly-production", "رشد تولید محصولات کگل در مقایسه با دوره مشابه پارسال چه‌قدر بوده؟"),
    ("fa-03", "monthly-sales", "مبالغ فروش ماهانه کگل را برای شش گزارش آخر به ترتیب زمانی بیاور."),
    ("fa-03", "monthly-sales", "فروش ریالی کگل در شهریور ۱۴۰۵ به چه میزان رسید؟"),
    ("fa-01", "monthly-sales", "در آخرین گزارش، مقدار فروش گندله کگهر چند تن بوده است؟"),
    ("fa-07", "company-industry", "کدام شرکت‌های صنعت فولاد رشد فروش ماهانه بیشتری داشته‌اند؟"),
    ("fa-07", "company-industry", "شرکت‌های یک صنعت را با رشد فروش ماهانه مرتب کن."),
    ("fa-01", "company-industry", "کدام شرکت‌های صنعت معدن در شهریور گندله تولید مثبت داشته‌اند؟"),
    ("fa-07", "ambiguity", "شرکت‌های تولیدکننده گندله را از نظر فروش مقایسه کن؛ منظورم فروش کل شرکت است یا خود محصول؟"),
    ("fa-07", "ambiguity", "آیا رشد تولید کگهر با رشد فروش آن هم‌جهت بوده است؟"),
]

HOLDOUT_VARIANTS = [
    ("fa-01", "population-discovery", "بدون تعیین شرکت، فهرست تولیدکنندگان دارای تولید مثبت گندله را برای شهریور ۱۴۰۵ نشان بده."),
    ("fa-01", "population-discovery", "در آخرین بازه‌ای که داده معتبر داریم، چه شرکت‌هایی گندله سنگ‌آهن تولید کرده‌اند؟"),
    ("fa-01", "population-discovery", "شرکت‌های صنعت فولاد که در شهریور ۱۴۰۵ تولید گندله داشته‌اند کدام‌اند؟"),
    ("fa-03", "population-discovery", "کدام تولیدکنندگان گندله در آخرین ماه گزارش‌شده فروش محصول بالاتر از ۳۰ تن داشتند؟"),
    ("fa-03", "population-discovery", "تولیدکنندگان گندله را بر اساس فروش کل شرکت در شهریور ۱۴۰۵ رتبه‌بندی کن."),
    ("fa-03", "population-discovery", "فروش خود محصول گندله برای شرکت‌های تولیدکننده را در ماه مشترک رتبه‌بندی کن."),
    ("fa-value-04", "financial-statements", "وجه نقد و معادل نقد کگهر در صورت مالی پایان سال ۱۴۰۴ چقدر بود؟"),
    ("fa-04", "financial-statements", "آخرین سود خالص فولاد را فقط از صورت جداگانه گزارش کن."),
    ("fa-08", "financial-analytics", "سهام صنعت فولاد را از نظر نسبت P/E مقایسه کن."),
    ("fa-07", "ambiguity", "بهترین شرکت بازار کدام است؟"),
]


def main() -> None:
    # The source is JSON-compatible YAML and is read in UTF-8.
    source = json.loads(SOURCE.read_text(encoding="utf-8"))
    templates = {case["id"]: case for case in source["cases"]}
    original_dev = [case for case in source["cases"] if "heldout" not in case["id"] and case["id"] != "fa-01"]
    cases: list[dict] = []
    cases.extend(copy.deepcopy(case) for case in original_dev)
    for number, (template_id, category, question) in enumerate(DEV_VARIANTS, 1):
        case = copy.deepcopy(templates[template_id])
        case["id"] = f"readiness-dev-{number:02d}"
        case["question"] = question
        case["readinessCategory"] = category
        case["split"] = "development"
        cases.append(case)
    # Existing examples are preserved verbatim apart from stable split metadata.
    for case in cases:
        case.setdefault("split", "development")
        case.setdefault("readinessCategory", ",".join(case.get("domains", [])))
    if len(cases) != 40:
        raise ValueError(f"Expected exactly 40 dev cases; found {len(cases)}")

    holdout: list[dict] = []
    for number, (template_id, category, question) in enumerate(HOLDOUT_VARIANTS, 1):
        case = copy.deepcopy(templates[template_id])
        case["id"] = f"readiness-holdout-{number:02d}"
        case["question"] = question
        case["readinessCategory"] = category
        case["split"] = "holdout"
        holdout.append(case)
    if len(holdout) != 10:
        raise ValueError(f"Expected exactly 10 holdout cases; found {len(holdout)}")

    all_questions = [case["question"] for case in cases + holdout]
    if len(set(all_questions)) != 50:
        raise ValueError("Question strings must be distinct across the fixed 40/10 split")

    for path, split, values in ((DEV, "development", cases), (HOLDOUT, "holdout", holdout)):
        payload = {
            "datasetVersion": "feature141-release-readiness-v1",
            "split": split,
            "createdBeforeInference": True,
            "caseCount": len(values),
            "cases": values,
        }
        path.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Wrote fixed corpus: {len(cases)} development + {len(holdout)} untouched holdout questions.")


if __name__ == "__main__":
    main()
