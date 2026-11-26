"""Out-of-sample validation of the round-2 approximation on the third reference batch (15 new symbols).

The formula is FROZEN (chosen on the first 9 symbols, see REPORT.md section 9) and is applied unchanged:
    forward sales  = last 12 monthly sales x (last 4 months / same 4 months a year earlier)
    forward profit = forward sales x simple mean of the last 8 quarterly net margins
Market value comes from the TTM P/E the owner read on the same page: market cap = TTM P/E x net profit of the
last four quarters (this reproduces TahlilApp's TTM gauge exactly, section 3), so no price or share count is needed.
Writes results/oos-validation.csv. Usage: py -3 validate_oos.py
"""
import csv, collections, pathlib, statistics
from lib import rows, num, line, REV_LINES

OUT = pathlib.Path(__file__).parent / "results"
# symbol: (forward P/E, forward P/S, TTM P/E) as shown by TahlilApp; date 1405/09/03 (آپ: 1405/08/24).
REF = {"کچاد": (4.65, 1.06, 9.70), "فخوز": (10.50, 0.39, 14.81), "فایرا": (1.95, 0.48, 4.90),
       "فگستر": (1.77, 0.45, 5.70), "آپ": (74.44, 0.34, 90.99), "اروند": (7.86, 1.79, 14.76),
       "کطبس": (4.09, 1.35, 9.01), "شکلر": (3.81, 1.74, 10.49), "شپنا": (5.83, None, 3.53),
       "بمپنا": (10.09, 4.76, 22.77), "اسیاتک": (4.54, 1.05, 5.42), "ستران": (8.03, 1.91, 9.37),
       "سمازن": (4.89, 2.23, 9.35), "حفارس": (94.80, 6.67, 79.77), "حفاری": (5.23, 0.66, 2.40)}
WINDOW_MONTHS, MARGIN_QUARTERS = 4, 8  # frozen


def quarters():
    by = collections.defaultdict(dict)
    for r in rows("10_oos_cw_income_statements.csv"):
        q = by[r["ticker"]].setdefault(r["period_end"], {"period_end": r["period_end"], "fm": int(r["fm"])})
        q[int(r["line_id"])] = (num(r["quarter"]), num(r["ytd"]), num(r["ttm"]))
    out = {t: [by[t][k] for k in sorted(by[t])] for t in by}
    # A quarter without a discrete value (آپ after its fiscal-year change) is filled with YTD minus the previous YTD.
    for t, qs in out.items():
        for prev, cur in zip(qs, qs[1:]):
            for i in [k for k in cur if isinstance(k, int)]:
                d, ytd, ttm = cur[i]
                if d is None and ytd is not None and i in prev and prev[i][1] is not None:
                    cur[i] = (ytd - prev[i][1], ytd, ttm); cur["filled"] = True
    return out


def monthly():
    out = collections.defaultdict(dict)
    for r in rows("12_oos_noavaran_monthly_sales.csv"):
        if r["output_type"] in ("0", ""):
            k = r["period_end"][:7]
            out[r["ticker"]][k] = out[r["ticker"]].get(k, 0) + float(r["sales_amount_million_rial"] or 0) * 1e6
    return out


def main():
    Q, M = quarters(), monthly()
    head = ["ticker", "last_quarter", "last_month", "ttm_pe_ref", "fwd_pe_ref", "fwd_pe_calc", "pe_err_pct",
            "fwd_ps_ref", "fwd_ps_calc", "ps_err_pct", "implied_margin_pct", "margin_8q_mean_pct", "margin_ttm_pct",
            "implied_sales_over_monthly_ttm", "growth_factor_used", "statement_ttm_over_monthly_ttm", "same_side_as_ttm", "note"]
    out = []
    for t, (fpe, fps, tpe) in REF.items():
        q = Q[t]; np_ = [line(x, 66) for x in q]; rev = [line(x, REV_LINES) for x in q]
        m = [M[t][k] for k in sorted(M[t])]; last_month = sorted(M[t])[-1]
        mc = tpe * sum(np_[-4:])
        ttm_m = sum(m[-12:]); g = sum(m[-WINDOW_MONTHS:]) / sum(m[-12 - WINDOW_MONTHS:-12])
        sales = ttm_m * g
        margin = statistics.mean(a / b for a, b in zip(np_[-MARGIN_QUARTERS:], rev[-MARGIN_QUARTERS:]))
        pe = mc / (sales * margin); ps = mc / sales
        out.append([t, q[-1]["period_end"], last_month, tpe, fpe, round(pe, 2), round((pe / fpe - 1) * 100, 1),
                    fps, round(ps, 2), round((ps / fps - 1) * 100, 1) if fps else None,
                    round(fps / fpe * 100, 2) if fps else None, round(margin * 100, 2),
                    round(sum(np_[-4:]) / sum(rev[-4:]) * 100, 2),
                    round(mc / fps / ttm_m, 2) if fps else None, round(g, 2), round(sum(rev[-4:]) / ttm_m, 2),
                    (pe < tpe) == (fpe < tpe),
                    "irregular quarter filled from YTD" if any(x.get("filled") for x in q[-MARGIN_QUARTERS:]) else ""])
    with (OUT / "oos-validation.csv").open("w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh); w.writerow(head); w.writerows(out)
    print(" | ".join(head))
    for r in out: print(" | ".join(str(x) for x in r))
    for label, i in (("P/E", 6), ("P/S", 9)):
        e = [abs(r[i]) for r in out if r[i] is not None]
        print(f"{label}: n={len(e)} MAPE {statistics.mean(e):.1f}% MedAPE {statistics.median(e):.1f}% max {max(e):.1f}% "
              f"within5 {sum(x <= 5 for x in e)} within10 {sum(x <= 10 for x in e)} within20 {sum(x <= 20 for x in e)}")
    print("same side as TTM:", sum(r[-2] for r in out), "/", len(out))


if __name__ == "__main__":
    main()
