"""Shared loaders for the forward P/E research (stdlib only)."""
import csv, collections, pathlib, sys
sys.stdout.reconfigure(encoding="utf-8")
DATA = pathlib.Path(__file__).parent / "data"
TICKERS = ["اخشان", "فاذر", "ارفع", "اخابر", "شگل", "کگهر", "خزامیا", "فملی", "فولاد", "کگل"]
REF_PE = {"اخشان": 6.17, "فاذر": 100.08, "ارفع": 2.92, "اخابر": 13.09, "شگل": 5.57, "کگهر": 5.46, "خزامیا": 3.29,
          "فملی": 3.64, "فولاد": 3.82, "کگل": 8.76}
# Forward P/S read from TahlilApp on the same date (second batch of reference data, 2026-11-26).
REF_PS = {"اخشان": 1.84, "فاذر": 5.01, "ارفع": 0.48, "اخابر": 2.30, "شگل": 0.50, "کگهر": 1.22, "خزامیا": 0.19,
          "فملی": 1.86, "فولاد": 0.86, "کگل": 1.26}
REF_DATE = "2026-11-24"  # 1405/09/03

def rows(name):
    return list(csv.DictReader((DATA / name).open(encoding="utf-8")))

def num(x):
    return None if x in ("", None) else float(x)

def quarters():
    """ticker -> list of quarters (oldest first); each quarter: dict line_id -> (quarter, ytd, ttm) plus meta."""
    by = collections.defaultdict(dict)
    for r in rows("01_cw_income_statements.csv"):
        q = by[r["ticker"]].setdefault(r["period_end"], {"period_end": r["period_end"], "fy": int(r["fy"]), "fm": int(r["fm"])})
        q[int(r["line_id"])] = (num(r["quarter"]), num(r["ytd"]), num(r["ttm"]))
    return {t: [by[t][k] for k in sorted(by[t])] for t in by}

def screener():
    out = collections.defaultdict(dict)
    for r in rows("02_cw_screener_values.csv"):
        out[r["ticker"]][r["field_key"]] = float(r["value"])
    return out

def prices():
    out = collections.defaultdict(dict)
    for r in rows("05_tsetmc_prices.csv"):
        out[r["ticker"]][r["date"]] = {"close": float(r["closing_price"]), "last": float(r["last_price"])}
    return out

def monthly():
    out = collections.defaultdict(dict)
    for r in rows("04_noavaran_monthly_sales.csv"):
        if r["output_type"] in ("0", ""):
            out[r["ticker"]][r["period_end"][:7]] = out[r["ticker"]].get(r["period_end"][:7], 0) + float(r["sales_amount_million_rial"] or 0) * 1e6
    return out

REV_LINES = (44, 46, 124, 412, 599)
def line(q, ids, col=0):
    for i in (ids if isinstance(ids, tuple) else (ids,)):
        if i in q and q[i][col] is not None:
            return q[i][col]
    return None
