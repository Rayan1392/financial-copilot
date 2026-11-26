"""Evidence script: TahlilApp's old API (futureprediction/{symbol}) returned a forward P/S that Sapio stored.

1. TTM P/S = closing price x shares / sum of the last 12 monthly sales (reproduced here).
2. The forward P/S stays frozen between provider batches. Pricing it at the batch-day close gives the same
   forward sales in two batches (2026-07-29 and 2026-08-17) to six digits, although a new monthly report
   was published in between. So the forward sales figure is not recomputed from each monthly report.
"""
from lib import *
PX = prices()
SHARES = {  # billion shares on each batch day (Capital / 1000 Rial); شگل raised capital between the two batches
    "ارفع": (32.0, 32.0), "شگل": (6.15, 17.0), "اخشان": (1.1, 1.1), "اخابر": (960.511332, 960.511332)}
snaps = {}
for r in rows("06_forward_ps_snapshots.csv"):
    snaps.setdefault(r["ticker"], {})[r["obs_date"]] = (float(r["ttm_ps"]), float(r["forward_ps"]))
print("ticker | forward P/S batch 1 -> forward sales (bn Rial) at 07/29 close | batch 2 -> at 08/17 close | TTM sales implied 07/29")
for t, (s1, s2) in SHARES.items():
    ttm1, f1 = snaps[t]["2026-07-29"]; _, f2 = snaps[t]["2026-08-22"]
    p1 = PX[t]["2026-07-29"]["close"]; p2 = PX[t]["2026-08-17"]["close"]
    print(f"{t} | {f1:.8f} -> {p1 * s1 / f1:,.0f} | {f2:.8f} -> {p2 * s2 / f2:,.0f} | {p1 * s1 / ttm1:,.0f} (S_f/TTM = {ttm1 / f1:.3f})")
