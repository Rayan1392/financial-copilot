"""Export estimator test fixtures for the approved change `valuation-gauge-forward-ratio` (Feature 159).

Writes results/estimator-fixtures.json: for each reference symbol the exact estimator inputs (last 16 monthly sales,
last 8 quarters, current ratios, industry) and the expected output of the APPROVED rule (user-story requirement 1 and 3):
    forecast sales = sum(last 12 months) x sum(last 4 months) / sum(same 4 months one year earlier)
    forward margin = median of the last 8 quarterly net margins
    forward P/S    = current P/S x revenue of the last 4 quarters / forecast sales
    forward P/E    = current P/E x net profit of the last 4 quarters / (forecast sales x forward margin)
Expected values are this script's own results, not TahlilApp's numbers (those are kept as `tahlilapp_*` for information).
Money is in rials. Usage: py -3 export_estimator_fixtures.py
"""
import collections, datetime, json, pathlib, statistics
import lib
from validate_oos import REF as NEW_REF

AS_OF = "2026-11-26"
MIN_G, MAX_G, MAX_AGE_DAYS, EXCLUDED = 0.5, 4.0, 75, {"31"}
MONTH_GAP, QUARTER_GAP = 35, 100  # largest allowed distance in days between consecutive period ends
D = datetime.date.fromisoformat

def months(file):
    out = collections.defaultdict(dict)
    for r in lib.rows(file):
        if r["output_type"] in ("0", ""):
            k = r["period_end"][:10]
            out[r["ticker"]][k] = out[r["ticker"]].get(k, 0) + float(r["sales_amount_million_rial"] or 0) * 1e6
    return out

def quarters(file):
    by = collections.defaultdict(dict)
    for r in lib.rows(file):
        by[r["ticker"]].setdefault(r["period_end"], {})[int(r["line_id"])] = lib.num(r["quarter"])
    out = {}
    for t, qs in by.items():
        out[t] = [(k, next((q[i] for i in lib.REV_LINES if q.get(i) is not None), None), q.get(66)) for k, q in sorted(qs.items())]
    return out

def tail_consecutive(items, n, gap):
    """Last n items if their period ends are consecutive (each gap <= `gap` days), else None."""
    tail = items[-n:]
    if len(tail) < n or any((D(b[0]) - D(a[0])).days > gap for a, b in zip(tail, tail[1:])): return None
    return tail

def estimate(m, q, cur_pe, cur_ps, industry):
    res = {"forward_ps": None, "forward_pe": None, "ps_reason": "none", "pe_reason": "none"}
    def both(reason): res.update(ps_reason=reason, pe_reason=reason); return res
    if industry in EXCLUDED: return both("excluded_industry")
    mt = tail_consecutive(m, 16, MONTH_GAP)
    if mt is None: return both("insufficient_monthly_sales")
    if (D(AS_OF) - D(mt[-1][0])).days > MAX_AGE_DAYS: return both("stale_monthly_sales")
    s = [x[1] for x in mt]; base = sum(s[:4])
    if base <= 0: return both("no_comparison_sales")
    g = sum(s[-4:]) / base
    res["growth_factor"] = round(g, 6)
    if not MIN_G <= g <= MAX_G: return both("growth_out_of_range")
    qv = [x for x in q if x[1] is not None and x[1] > 0 and x[2] is not None]
    qt = tail_consecutive(qv, 8, QUARTER_GAP)
    if qt is None or qt[-1][0] != q[-1][0]: return both("insufficient_quarters")
    forecast = sum(s[-12:]) * g; rev4 = sum(x[1] for x in qt[-4:]); np4 = sum(x[2] for x in qt[-4:])
    margin = statistics.median(x[2] / x[1] for x in qt)
    res.update(forecast_sales=round(forecast), margin=round(margin, 6))
    if cur_ps is None or cur_ps <= 0: res["ps_reason"] = "no_current_value"
    else: res["forward_ps"] = round(cur_ps * rev4 / forecast, 2)
    if cur_pe is None or cur_pe <= 0 or np4 <= 0: res["pe_reason"] = "no_positive_earnings"
    elif margin <= 0: res["pe_reason"] = "non_positive_margin"
    else: res["forward_pe"] = round(cur_pe * np4 / (forecast * margin), 2)
    return res

def main():
    ind = {r["ticker"]: r["industry_external_id"] for r in lib.rows("13_company_industries.csv")}
    P = lib.prices(); g = {(r["ticker"], r["gauge"]): float(r["close_ratio"]) for r in lib.rows("03_cw_gauges.csv")}
    sets = [("round-1-2", months("04_noavaran_monthly_sales.csv"), quarters("01_cw_income_statements.csv"), lib.TICKERS),
            ("round-3", months("12_oos_noavaran_monthly_sales.csv"), quarters("10_oos_cw_income_statements.csv"), list(NEW_REF))]
    cases = []
    for name, M, Q, tickers in sets:
        for t in tickers:
            m = sorted(M[t].items())[-16:]; q = Q[t][-8:]
            np4 = sum(x[2] or 0 for x in q[-4:]); rev4 = sum(x[1] or 0 for x in q[-4:])
            if name == "round-1-2":
                k = P[t][lib.REF_DATE]["last"] / P[t]["2026-11-21"]["last"]  # gauge of 11-21 moved to the reference date
                cur_pe, cur_ps = g[(t, "PE")] * k, g[(t, "PS")] * k
                ref_pe, ref_ps = lib.REF_PE[t], lib.REF_PS[t]
            else:
                ref_pe, ref_ps, cur_pe = NEW_REF[t]
                cur_ps = cur_pe * np4 / rev4  # same market value as the P/E gauge the owner read
            cases.append({"symbol": t, "set": name, "industry_external_id": ind[t],
                          "current_pe": round(cur_pe, 6), "current_ps": round(cur_ps, 6),
                          "monthly_sales": [{"month_end": a, "sales": b} for a, b in m],
                          "quarters": [{"period_end": a, "revenue": b, "net_profit": c} for a, b, c in q],
                          "expected": estimate(m, q, cur_pe, cur_ps, ind[t]),
                          "tahlilapp_forward_pe": ref_pe, "tahlilapp_forward_ps": ref_ps})
    doc = {"as_of": AS_OF, "thresholds": {"min_growth_factor": MIN_G, "max_growth_factor": MAX_G,
           "max_monthly_report_age_days": MAX_AGE_DAYS, "excluded_industry_external_ids": sorted(EXCLUDED),
           "max_month_gap_days": MONTH_GAP, "max_quarter_gap_days": QUARTER_GAP}, "cases": cases}
    out = pathlib.Path(__file__).parent / "results" / "estimator-fixtures.json"
    out.write_text(json.dumps(doc, ensure_ascii=False, indent=1), encoding="utf-8")
    for c in cases:
        e = c["expected"]; print(c["symbol"], e.get("growth_factor"), e.get("margin"), e["forward_pe"], e["pe_reason"], e["forward_ps"], e["ps_reason"], "| ref", c["tahlilapp_forward_pe"], c["tahlilapp_forward_ps"])

if __name__ == "__main__":
    main()
