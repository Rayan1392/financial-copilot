"""Systematic test of candidate forward P/E formulas (models A-G) against the TahlilApp reference values.

Inputs are the CSV files written by extract.py and fetch_prices.py. No formula has a fitted parameter:
every candidate is one fixed rule applied unchanged to all seven symbols.

Usage: py -3 analyze.py            -> prints the ranking, writes results/*.csv and results/model-matrix.md
"""
import collections, csv, math, pathlib, statistics
from lib import *

OUT = pathlib.Path(__file__).parent / "results"
OUT.mkdir(exist_ok=True)
Q, SCR, PX, MON = quarters(), screener(), prices(), monthly()
PRODUCTS = collections.defaultdict(lambda: collections.defaultdict(dict))
for r in rows("09_noavaran_monthly_products.csv"):
    PRODUCTS[r["ticker"]][r["product_code"]][r["period_end"][:7]] = (num(r["sales_quantity"]) or 0.0, num(r["sales_rate"]) or 0.0)

GAUGE_PE = {r["ticker"]: float(r["close_ratio"]) for r in rows("03_cw_gauges.csv") if r["gauge"] == "PE"}
LAST_STATEMENT = "2026-09-22"      # 1405/06/31, the latest quarter published before the reference date
LAST_MONTH = "2026-10"             # Mehr 1405, the latest monthly report published before the reference date
PRICE_BASES = {"last 09/03": ("2026-11-24", "last"), "close 09/03": ("2026-11-24", "close"), "last 09/02": ("2026-11-23", "last")}
PRIMARY = "last 09/03"

NP, OP, GP, FIN, TAX, EBT, CAP = 66, 56, 52, 57, 63, 62, 86


def ctx(t):
    """All building blocks for one symbol, as known on the reference date."""
    qs = [q for q in Q[t] if q["period_end"] <= LAST_STATEMENT]
    ms = sorted(k for k in MON[t] if k <= LAST_MONTH)
    c = {"q": qs, "months": ms, "m": [MON[t][k] for k in ms]}
    c["np"] = [line(q, NP) for q in qs]; c["rev"] = [line(q, REV_LINES) for q in qs]
    c["op"] = [line(q, OP) for q in qs]
    c["fm"] = qs[-1]["fm"]                       # months of the fiscal year covered by the latest statement
    c["nq"] = c["fm"] // 3                       # quarters elapsed in the fiscal year
    c["shares"] = line(qs[-1], CAP) / 1000.0     # capital / 1000 Rial par value
    if t in GAUGE_PE:                            # TahlilApp's own TTM P/E gives the share count it really uses
        c["shares"] = GAUGE_PE[t] * sum(c["np"][-4:]) / PX[t]["2026-11-21"]["last"]
    c["ytd_np"] = sum(c["np"][-c["nq"]:]); c["ytd_rev"] = sum(c["rev"][-c["nq"]:])
    c["ttm_np"] = sum(c["np"][-4:]); c["ttm_rev"] = sum(c["rev"][-4:])
    c["fy_np"] = sum(c["np"][-c["nq"] - 4:-c["nq"]]); c["fy_rev"] = sum(c["rev"][-c["nq"] - 4:-c["nq"]])
    c["ly_ytd_np"] = sum(c["np"][-c["nq"] - 4:-4]); c["ly_ytd_rev"] = sum(c["rev"][-c["nq"] - 4:-4])
    c["months_after"] = 1                         # Mehr is the only month reported after the latest statement
    c["months_left"] = 12 - c["fm"] - c["months_after"]
    return c


# ---- revenue forecasts (annual) -------------------------------------------------------------------------
def rev_methods(c):
    m = c["m"]; L = m[-1]
    w3 = (3 * m[-1] + 2 * m[-2] + m[-3]) / 6
    r = {
        "12×آخرین ماه": 12 * L,
        "12×میانگین ۲ ماه": 6 * sum(m[-2:]),
        "12×میانگین ۳ ماه": 4 * sum(m[-3:]),
        "12×میانگین ۶ ماه": 2 * sum(m[-6:]),
        "جمع ۱۲ ماه اخیر": sum(m[-12:]),
        "12×میانگین وزنی ۳ ماه": 12 * w3,
        "ماه مشابه پارسال×رشد (فصلی)": sum(m[-12:]) * L / m[-13],
        "4×فروش آخرین فصل": 4 * c["rev"][-1],
        "سالانه‌سازی فروش تجمعی سال مالی": c["ytd_rev"] * 12 / c["fm"],
    }
    fy_months = m[-(c["fm"] + c["months_after"]):]            # months of the current fiscal year so far
    r["FY0: ماه‌های واقعی + باقی‌مانده×آخرین ماه"] = sum(fy_months) + c["months_left"] * L
    r["FY0: ماه‌های واقعی + باقی‌مانده×میانگین ۳ ماه"] = sum(fy_months) + c["months_left"] * sum(m[-3:]) / 3
    return r


def margins(c):
    np_, rev = c["np"], c["rev"]
    qm = [n / r for n, r in zip(np_, rev)]
    return {
        "حاشیه آخرین فصل": qm[-1],
        "حاشیه تجمعی سال مالی": c["ytd_np"] / c["ytd_rev"],
        "حاشیه TTM": c["ttm_np"] / c["ttm_rev"],
        "حاشیه سال مالی قبل": c["fy_np"] / c["fy_rev"],
        "میانگین حاشیه ۲ فصل": sum(qm[-2:]) / 2,
        "میانگین حاشیه ۳ فصل": sum(qm[-3:]) / 3,
        "میانگین حاشیه ۴ فصل": sum(qm[-4:]) / 4,
        "میانگین وزنی حاشیه ۴ فصل": (4 * qm[-1] + 3 * qm[-2] + 2 * qm[-3] + qm[-4]) / 10,
        "حاشیه فصل مشابه پارسال": qm[-5],
    }


def model_f_revenue(t, months, rate_window):
    """Model F: sum over products of (average sales quantity of the last n months) x (latest or average selling rate) x 12."""
    total = 0.0; used = 0
    keys = [k for k in sorted(MON[t]) if k <= LAST_MONTH][-months:]
    for code, by_month in PRODUCTS[t].items():
        qty = [by_month.get(k, (0.0, 0.0))[0] for k in keys]
        rk = [k for k in keys[-rate_window:] if by_month.get(k, (0, 0))[1] > 0 and by_month[k][0] > 0]
        if not rk or sum(qty) <= 0:
            continue
        rate = sum(by_month[k][0] * by_month[k][1] for k in rk) / sum(by_month[k][0] for k in rk)   # quantity-weighted
        total += sum(qty) / months * rate * 12 * 1e6; used += 1          # rate x quantity is in million Rial
    return total if used else None


def candidates(t):
    c = ctx(t); np_, rev, m = c["np"], c["rev"], c["m"]; s = SCR[t]; out = {}
    def add(group, name, horizon, value, complexity):
        out[f"{group} | {name}"] = (group, name, horizon, value, complexity)
    # --- Control: trailing values
    add("کنترل", "سود TTM (همان P/E فعلی)", "TTM", c["ttm_np"], 1)
    add("کنترل", "سود آخرین سال مالی کامل", "FY-1", c["fy_np"], 1)
    # --- A: annualized realized profit
    add("A", "سود تجمعی سال مالی × 12/ماه", "FY0", c["ytd_np"] * 12 / c["fm"], 1)
    add("A", "آخرین فصل × 4", "سالانه‌شده", np_[-1] * 4, 1)
    add("A", "میانگین ۲ فصل اخیر × 4", "سالانه‌شده", sum(np_[-2:]) * 2, 1)
    add("A", "میانگین ۳ فصل اخیر × 4", "سالانه‌شده", sum(np_[-3:]) * 4 / 3, 1)
    # --- B: last fiscal year x growth
    def growth(a, b):
        return a / b if b and b > 0 and a > 0 else None
    for name, g in (("رشد سود آخرین فصل به فصل مشابه", growth(np_[-1], np_[-5])),
                    ("رشد سود تجمعی به دوره مشابه", growth(c["ytd_np"], c["ly_ytd_np"])),
                    ("رشد سود TTM", growth(c["ttm_np"], sum(np_[-8:-4]))),
                    ("میانگین رشد ۴ فصل اخیر", (lambda gs: sum(gs) / len(gs) if len(gs) == 4 else None)(
                        [x for x in (growth(np_[-i], np_[-i - 4]) for i in range(1, 5)) if x]))):
        add("B", f"سود سال قبل × {name}", "FY0", c["fy_np"] * g if g else None, 2)
    g = growth(c["ytd_np"], c["ly_ytd_np"])
    add("B", "سود تجمعی + (باقی‌مانده سال قبل × رشد تجمعی)", "FY0", c["ytd_np"] + (c["fy_np"] - c["ly_ytd_np"]) * g if g else None, 2)
    # --- C: revenue forecast x margin (full grid), D: realized YTD profit + forecast of the remaining months
    R, MG = rev_methods(c), margins(c)
    for rn, rv in R.items():
        for mn, mv in MG.items():
            add("C", f"{rn} × {mn}", "FY0" if rn.startswith("FY0") or "تجمعی" in rn else "NTM", rv * mv, 2)
    for fn, f in (("آخرین ماه", m[-1]), ("میانگین ۳ ماه", sum(m[-3:]) / 3), ("میانگین ۶ ماه", sum(m[-6:]) / 6),
                  ("میانگین ماهانه سال مالی", sum(m[-(c["fm"] + 1):]) / (c["fm"] + 1))):
        for mn, mv in MG.items():
            add("D", f"سود تجمعی + (مهر + ماه‌های باقی‌مانده×{fn}) × {mn}", "FY0",
                c["ytd_np"] + (m[-1] + c["months_left"] * f) * mv, 3)
    # --- E: income statement build-up on 12 x last month revenue
    for base_name, base in (("12×آخرین ماه", 12 * m[-1]), ("12×میانگین ۳ ماه", 4 * sum(m[-3:]))):
        for win_name, n in (("آخرین فصل", 1), ("TTM", 4)):
            op_margin = sum(c["op"][-n:]) / sum(rev[-n:])
            ebt = [line(q, (EBT, 60)) for q in c["q"][-4:]]
            ebt = [e if e is not None else n for e, n in zip(ebt, np_[-4:])]      # no pre-tax line: treat as untaxed
            non_op = sum(ebt) - sum(c["op"][-4:])                         # TTM non-operating items incl. finance cost
            eff_tax = min(max(1 - c["ttm_np"] / sum(ebt), 0.0), 0.5) if sum(ebt) > 0 else 0.0
            add("E", f"{base_name} × حاشیه عملیاتی {win_name} + غیرعملیاتی TTM − مالیات مؤثر", "NTM",
                (base * op_margin + non_op) * (1 - eff_tax), 4)
    # --- F: product quantity x rate
    for qn, qmonths in (("میانگین مقدار ۳ ماه", 3), ("میانگین مقدار ۶ ماه", 6), ("مقدار آخرین ماه", 1)):
        fr = model_f_revenue(t, qmonths, 1)
        for mn in ("حاشیه آخرین فصل", "حاشیه TTM"):
            add("F", f"{qn} × آخرین نرخ × 12 × {mn}", "NTM", fr * MG[mn] if fr else None, 3)
    fr = model_f_revenue(t, 3, 3)
    add("F", "میانگین مقدار ۳ ماه × میانگین وزنی نرخ ۳ ماه × 12 × حاشیه TTM", "NTM", fr * MG["حاشیه TTM"] if fr else None, 3)
    # --- G: hybrids, TTM profit scaled by sales growth
    L = m[-1]; avg12 = sum(m[-13:-1]) / 12
    add("G", "سود TTM × (آخرین ماه / ماه مشابه پارسال)", "NTM", c["ttm_np"] * L / m[-13], 2)
    add("G", "سود TTM × (آخرین ماه / میانگین ۱۲ ماه قبل)", "NTM", c["ttm_np"] * L / avg12, 2)
    add("G", "سود TTM × (12×آخرین ماه / فروش TTM)", "NTM", c["ttm_np"] * 12 * L / c["ttm_rev"], 2)
    add("G", "سود TTM × رشد فروش آخرین فصل به فصل مشابه", "NTM", c["ttm_np"] * rev[-1] / rev[-5], 2)
    add("G", "سود TTM × (فروش آخرین فصل / میانگین ۴ فصل قبل)", "NTM", c["ttm_np"] * rev[-1] / (sum(rev[-5:-1]) / 4), 2)
    add("G", "سود TTM × رشد فروش TTM", "NTM", c["ttm_np"] * c["ttm_rev"] / sum(rev[-8:-4]), 2)
    add("G", "میانگین (آخرین فصل×4 ، TTM)", "ترکیبی", (np_[-1] * 4 + c["ttm_np"]) / 2, 2)
    add("G", "میانگین (سالانه‌شده تجمعی ، 12×آخرین ماه×حاشیه TTM)", "ترکیبی",
        (c["ytd_np"] * 12 / c["fm"] + 12 * L * MG["حاشیه TTM"]) / 2, 3)
    add("G", "آخرین فصل×4 × (3×آخرین ماه / فروش آخرین فصل)", "NTM", np_[-1] * 4 * 3 * L / rev[-1], 2)
    add("G", "سود عملیاتی TTM (بدون اقلام غیرعملیاتی)", "TTM", sum(c["op"][-4:]), 1)
    add("G", "سود عملیاتی آخرین فصل × 4", "سالانه‌شده", c["op"][-1] * 4, 1)
    return c, out


def metrics(calc, ref):
    """calc/ref: dict ticker -> P/E (calc may be None). Returns the aggregate measures of section 10."""
    pairs = [(calc[t], ref[t]) for t in ref if calc.get(t) is not None and calc[t] > 0]
    if not pairs:
        return None
    ae = [abs(a - b) for a, b in pairs]; ape = [abs(a - b) / b * 100 for a, b in pairs]
    return {"coverage": len(pairs), "mae": statistics.mean(ae), "mape": statistics.mean(ape),
            "rmse": math.sqrt(statistics.mean(x * x for x in ae)), "medape": statistics.median(ape), "max_ape": max(ape),
            "max_ae": max(ae), "exact": sum(round(a, 2) == round(b, 2) for a, b in pairs),
            "within5": sum(x <= 5 for x in ape), "within10": sum(x <= 10 for x in ape)}


def main():
    per = {t: candidates(t) for t in TICKERS}
    names = list(per[TICKERS[0]][1])
    # ---- A. input summary and implied forward EPS / profit
    with (OUT / "implied-forward-eps.csv").open("w", newline="", encoding="utf-8") as fh:
        w = csv.writer(fh)
        w.writerow(["ticker", "ref_forward_pe", "price_basis", "price", "shares", "implied_eps", "implied_eps_low", "implied_eps_high",
                    "implied_net_profit_bn_rial", "ttm_net_profit_bn_rial", "implied_over_ttm", "tahlilapp_ttm_pe_recomputed"])
        for t in TICKERS:
            c = per[t][0]
            for basis, (d, k) in PRICE_BASES.items():
                p = PX[t][d][k]; ref = REF_PE[t]
                w.writerow([t, ref, basis, p, c["shares"], round(p / ref, 2), round(p / (ref + 0.005), 2), round(p / (ref - 0.005), 2),
                            round(p / ref * c["shares"] / 1e9, 1), round(c["ttm_np"] / 1e9, 1),
                            round(p / ref * c["shares"] / c["ttm_np"], 4), round(p * c["shares"] / c["ttm_np"], 2)])
    # ---- B/C. matrix and ranking, per price basis
    summary = []
    for basis, (d, k) in PRICE_BASES.items():
        ranked = []
        for n in names:
            group, name, horizon, _, cx = per[TICKERS[0]][1][n]
            calc = {}
            for t in TICKERS:
                v = per[t][1][n][3]; c = per[t][0]
                calc[t] = PX[t][d][k] * c["shares"] / v if v and v > 0 else None      # None: loss or missing data
            mt = metrics(calc, REF_PE)
            if mt:
                ranked.append((mt["medape"], mt["mape"], group, name, horizon, cx, calc, mt))
        ranked.sort(key=lambda x: (-x[7]["coverage"] // 7, x[0]))
        with (OUT / f"model-comparison ({basis.replace('/', '-')}).csv").open("w", newline="", encoding="utf-8") as fh:
            w = csv.writer(fh)
            w.writerow(["rank", "group", "formula", "horizon", "complexity"] + TICKERS +
                       ["coverage", "MAE", "MAPE", "RMSE", "MedAPE", "MaxAPE", "MaxAE", "exact_after_rounding", "within_5pct", "within_10pct"])
            for i, (_, _, group, name, horizon, cx, calc, mt) in enumerate(ranked, 1):
                w.writerow([i, group, name, horizon, cx] + [("" if calc[t] is None else round(calc[t], 2)) for t in TICKERS] +
                           [mt["coverage"], round(mt["mae"], 2), round(mt["mape"], 1), round(mt["rmse"], 2), round(mt["medape"], 1),
                            round(mt["max_ape"], 1), round(mt["max_ae"], 2), mt["exact"], mt["within5"], mt["within10"]])
        summary.append((basis, ranked))
    # ---- console + markdown for the primary basis
    basis, ranked = next(s for s in summary if s[0] == PRIMARY)
    full = [r for r in ranked if r[7]["coverage"] == len(TICKERS)]
    lines = [f"قیمت مبنا: {basis} — {len(ranked)} فرمول، {len(full)} فرمول با پوشش کامل", "",
             "| رتبه | مدل | فرمول | " + " | ".join(TICKERS) + " | MedAPE | MAPE | MAE | بیشینه خطا٪ | تطابق دقیق | ≤۵٪ | پوشش |",
             "|---|---|---|" + "---|" * len(TICKERS) + "---|---|---|---|---|---|---|",
             "| — | مرجع تحلیل‌اپ | — | " + " | ".join(str(REF_PE[t]) for t in TICKERS) + " | | | | | | | |"]
    for i, (_, _, group, name, horizon, cx, calc, mt) in enumerate(ranked[:25], 1):
        lines.append(f"| {i} | {group} | {name} | " + " | ".join("—" if calc[t] is None else f"{calc[t]:.2f}" for t in TICKERS) +
                     f" | {mt['medape']:.1f} | {mt['mape']:.1f} | {mt['mae']:.2f} | {mt['max_ape']:.0f} | {mt['exact']} | {mt['within5']} | {mt['coverage']}/{len(TICKERS)} |")
    (OUT / "model-matrix.md").write_text("\n".join(lines), encoding="utf-8")
    print("\n".join(lines))
    print("\nBest per group (primary basis):")
    seen = set()
    for _, _, group, name, horizon, cx, calc, mt in ranked:
        if group not in seen:
            seen.add(group)
            print(f"  {group}: {name} | MedAPE {mt['medape']:.1f}% MAPE {mt['mape']:.1f}% exact {mt['exact']}/7 cover {mt['coverage']}/7")
    print("\nBest MedAPE per price basis:")
    for basis, ranked in summary:
        b = ranked[0]
        print(f"  {basis}: {b[2]} | {b[3]} | MedAPE {b[7]['medape']:.1f}% MAPE {b[7]['mape']:.1f}% exact {b[7]['exact']}/7")
    print("\nExact matches after rounding, any formula, any basis:",
          sum(r[7]["exact"] for _, ranked in summary for r in ranked))


if __name__ == "__main__":
    main()
