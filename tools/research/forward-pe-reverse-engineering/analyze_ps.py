"""Second reference batch (forward P/S and forward P/E of the same date for ten symbols).

Part 1: forward-sales candidates vs TahlilApp forward P/S. Part 2: implied forward net margin
(forward P/S / forward P/E, price and shares cancel) vs historical margin rules.
Writes results/forward-ps-comparison.csv and results/forward-margin-comparison.csv.
"""
import csv, math, pathlib, statistics
from lib import *

OUT = pathlib.Path(__file__).parent / "results"
Q, P, M = quarters(), prices(), monthly()
GAUGE_PE = {r["ticker"]: float(r["close_ratio"]) for r in rows("03_cw_gauges.csv") if r["gauge"] == "PE"}


def fit(v):
    n = len(v); xb = (n - 1) / 2; yb = sum(v) / n
    s = sum((i - xb) * (y - yb) for i, y in enumerate(v)) / sum((i - xb) ** 2 for i in range(n))
    return lambda k: yb + s * (n - 1 + k - xb)


def sales_candidates(t):
    m = [M[t][k] for k in sorted(M[t])]; rev = [line(x, REV_LINES) for x in Q[t]]; ttm = sum(m[-12:])
    c = {"فروش ۱۲ ماه اخیر (کنترل)": ttm, "فروش چهار فصل اخیر (کنترل، همان P/S فعلی)": sum(rev[-4:]),
         "12 × آخرین ماه": 12 * m[-1], "12 × میانگین ۳ ماه": 4 * sum(m[-3:]), "12 × میانگین ۶ ماه": 2 * sum(m[-6:]),
         "4 × فروش آخرین فصل": 4 * rev[-1], "فروش چهار فصل × رشد فصل به فصل مشابه": sum(rev[-4:]) * rev[-1] / rev[-5],
         "فروش چهار فصل² ÷ چهار فصل قبل": sum(rev[-4:]) ** 2 / sum(rev[-8:-4]),
         "برون‌یابی خطی ۱۲ ماه": sum(fit(m[-12:])(k) for k in range(1, 13)),
         "برون‌یابی نمایی ۱۲ ماه": sum(math.exp(fit([math.log(x) for x in m[-12:]])(k)) for k in range(1, 13)),
         "برون‌یابی نمایی ۱۹ ماه": sum(math.exp(fit([math.log(x) for x in m])(k)) for k in range(1, 13))}
    for n in range(1, 8):
        c[f"فروش ۱۲ ماه × رشد سالانهٔ {n} ماه اخیر"] = ttm * sum(m[-n:]) / sum(m[-12 - n:-12])
    return c


def summarize(calc, ref):
    ape = [abs(calc[t] / ref[t] - 1) * 100 for t in TICKERS]
    return [round(statistics.mean(abs(calc[t] - ref[t]) for t in TICKERS), 3), round(statistics.mean(ape), 1),
            round(statistics.median(ape), 1), round(max(ape), 1), sum(round(calc[t], 2) == ref[t] for t in TICKERS),
            sum(x <= 5 for x in ape), sum(x <= 10 for x in ape)]


def main():
    mc = {}
    for t in TICKERS:
        shares = GAUGE_PE[t] * sum(line(x, 66) for x in Q[t][-4:]) / P[t]["2026-11-21"]["last"]
        mc[t] = P[t][REF_DATE]["last"] * shares
    table = []
    names = list(sales_candidates(TICKERS[0]))
    per = {t: sales_candidates(t) for t in TICKERS}
    for n in names:
        calc = {t: mc[t] / per[t][n] for t in TICKERS}
        table.append([n] + [round(calc[t], 2) for t in TICKERS] + summarize(calc, REF_PS))
    table.sort(key=lambda r: r[-6])
    head = ["formula"] + TICKERS + ["MAE", "MAPE", "MedAPE", "MaxAPE", "exact_after_rounding", "within_5pct", "within_10pct"]
    with (OUT / "forward-ps-comparison.csv").open("w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh); w.writerow(head); w.writerow(["TahlilApp reference"] + [REF_PS[t] for t in TICKERS]); w.writerows(table)
    print("Forward P/S (price: last 09/03)"); print(" | ".join(head))
    for r in table: print(" | ".join(str(x) for x in r))
    # ---- implied margin
    implied = {t: REF_PS[t] / REF_PE[t] for t in TICKERS}
    band = {t: ((REF_PS[t] - .005) / (REF_PE[t] + .005), (REF_PS[t] + .005) / (REF_PE[t] - .005)) for t in TICKERS}
    rules = {}
    for t in TICKERS:
        np_ = [line(x, 66) for x in Q[t]]; rev = [line(x, REV_LINES) for x in Q[t]]; nq = Q[t][-1]["fm"] // 3
        qm = [a / b for a, b in zip(np_, rev)]
        r = {"حاشیهٔ آخرین فصل": qm[-1], "حاشیهٔ تجمعی سال مالی": sum(np_[-nq:]) / sum(rev[-nq:]), "حاشیهٔ TTM": sum(np_[-4:]) / sum(rev[-4:]),
             "میانگین حاشیهٔ ۴ فصل": statistics.mean(qm[-4:]), "میانهٔ حاشیهٔ ۴ فصل": statistics.median(qm[-4:]),
             "حاشیهٔ تجمیعی ۷ فصل": sum(np_[-7:]) / sum(rev[-7:]), "حاشیهٔ تجمیعی ۸ فصل": sum(np_[-8:]) / sum(rev[-8:]),
             "میانگین حاشیهٔ ۸ فصل": statistics.mean(qm[-8:])}
        for k, v in r.items(): rules.setdefault(k, {})[t] = v
    with (OUT / "forward-margin-comparison.csv").open("w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh); w.writerow(["rule"] + TICKERS + ["inside_rounding_band", "median_rel_error_pct"])
        w.writerow(["TahlilApp implied (forward P/S / forward P/E)"] + [round(implied[t] * 100, 2) for t in TICKERS])
        print("\nImplied forward net margin %:", {t: round(implied[t] * 100, 2) for t in TICKERS})
        for k, v in rules.items():
            inside = sum(band[t][0] <= v[t] <= band[t][1] for t in TICKERS)
            err = statistics.median(abs(v[t] / implied[t] - 1) * 100 for t in TICKERS)
            w.writerow([k] + [round(v[t] * 100, 2) for t in TICKERS] + [inside, round(err, 1)])
            print(f"  {k}: " + " ".join(f"{v[t]*100:.1f}" for t in TICKERS) + f" | inside band {inside}/10, median error {err:.1f}%")


if __name__ == "__main__":
    main()
