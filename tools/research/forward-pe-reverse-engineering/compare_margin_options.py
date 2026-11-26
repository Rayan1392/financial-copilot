"""Margin-rule options for the Sapio forward P/E design proposal, end to end on both reference sets.

Sales rule is fixed (12 monthly sales x YoY growth of the last 4 months). Only the margin rule varies.
Old set: 9 symbols of rounds 1-2 (کگل excluded). New set: 15 symbols of round 3 (شپنا has no forward P/S).
These rules were compared AFTER seeing both sets; the numbers are in-sample. Writes results/margin-options.csv.
"""
import csv, pathlib, statistics
import lib, validate_oos as v

def trim(x, k):
    s = sorted(x); return statistics.mean(s[k:len(s) - k])
RULES = {"A mean of 8 quarterly net margins": lambda m: statistics.mean(m[-8:]),
         "B median of 8": lambda m: statistics.median(m[-8:]),
         "C median of 6": lambda m: statistics.median(m[-6:]),
         "D trimmed mean of 8 (drop 2 highest, 2 lowest)": lambda m: trim(m[-8:], 2),
         "E trimmed mean of 8 (drop 1 highest, 1 lowest)": lambda m: trim(m[-8:], 1)}

def cases():
    Q, P, M = lib.quarters(), lib.prices(), lib.monthly()
    g = {r["ticker"]: float(r["close_ratio"]) for r in lib.rows("03_cw_gauges.csv") if r["gauge"] == "PE"}
    for t in lib.TICKERS:
        if t == "کگل": continue
        npq = [lib.line(x, 66) for x in Q[t]]
        ttm_pe = g[t] * P[t][lib.REF_DATE]["last"] / P[t]["2026-11-21"]["last"]
        yield "old", t, Q[t], M[t], ttm_pe * sum(npq[-4:]), ttm_pe, lib.REF_PE[t], lib.REF_PS[t]
    Q, M = v.quarters(), v.monthly()
    for t, (fpe, fps, tpe) in v.REF.items():
        yield "new", t, Q[t], M[t], tpe * sum(lib.line(x, 66) for x in Q[t][-4:]), tpe, fpe, fps

def main():
    data = list(cases()); out = []
    for name, f in RULES.items():
        err = {"old": [], "new": []}; side = 0; row = {}
        for s, t, q, mm, mc, tpe, fpe, fps in data:
            pairs = [(lib.line(x, 66), lib.line(x, lib.REV_LINES)) for x in q]
            m = [a / b for a, b in pairs if a is not None and b]
            ms = [mm[k] for k in sorted(mm)]
            sales = sum(ms[-12:]) * sum(ms[-4:]) / sum(ms[-16:-12])
            pe = mc / (sales * f(m)); row[t] = round(pe, 2)
            err[s].append(abs(pe / fpe - 1) * 100); side += (pe < tpe) == (fpe < tpe)
        al = err["old"] + err["new"]
        out.append([name] + [round(statistics.mean(e), 1) for e in (err["old"], err["new"], al)] +
                   [round(statistics.median(al), 1), round(max(al), 1), sum(x <= 10 for x in al), sum(x <= 20 for x in al), side, len(al)]
                   + [row[c[1]] for c in data])
    head = ["margin_rule", "mape_old9", "mape_new15", "mape_all24", "medape_all", "max_all", "within10", "within20",
            "same_side_as_ttm", "n"] + [c[1] for c in data]
    with (pathlib.Path(__file__).parent / "results" / "margin-options.csv").open("w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh); w.writerow(head); w.writerow(["TahlilApp"] + [""] * 9 + [c[6] for c in data]); w.writerows(out)
    for r in out: print(" | ".join(str(x) for x in r[:10]))
    # without شپنا
    print("growth factors:", {c[1]: round(sum([c[3][k] for k in sorted(c[3])][-4:]) / sum([c[3][k] for k in sorted(c[3])][-16:-12]), 2) for c in data})

if __name__ == "__main__":
    main()
