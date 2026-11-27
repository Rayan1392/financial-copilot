"""Exploration: on 2026-07-29 TTM and forward P/S share the same price, so ttm_ps/forward_ps = S_f / S_ttm exactly.
Count companies for which a candidate reproduces that ratio within 0.2%."""
import math, statistics, collections
import pathlib, sys
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))
from lib import *
m = collections.defaultdict(dict)
for r in rows('07_monthly_sales_all.csv'):
    m[r['ticker']][r['period_end'][:7]] = m[r['ticker']].get(r['period_end'][:7], 0) + float(r['sales_million_rial'] or 0)
snap = {r['ticker']: float(r['ttm_ps']) / float(r['forward_ps']) for r in rows('06_forward_ps_snapshots.csv') if r['obs_date'] == '2026-07-29' and float(r['forward_ps']) > 0 and float(r['ttm_ps']) > 0}
months = ['2025-%02d' % i for i in range(4, 13)] + ['2026-%02d' % i for i in range(1, 11)]
def lin(v, h=12):
    n = len(v); xb = (n - 1) / 2; yb = sum(v) / n
    s = sum((i - xb) * (y - yb) for i, y in enumerate(v)) / sum((i - xb) ** 2 for i in range(n))
    return sum(yb + s * (n + k - xb) for k in range(h))
def cands(v):   # v: months oldest..latest, at least 13
    ttm = sum(v[-12:]); L = v[-1]; ly = v[-13]; a12 = sum(v[-13:-1]) / 12
    c = {'12L': 12 * L, '4*last3': 4 * sum(v[-3:]), '2*last6': 2 * sum(v[-6:]), 'ttm*L/LY': ttm * L / ly, 'ttm*L/avg12': ttm * L / a12,
         '12L*L/avg12': 12 * L * L / a12, '12L*L/LY': 12 * L * L / ly, 'lin12': lin(v[-12:]), 'lin6': lin(v[-6:]), 'lin13': lin(v[-13:]),
         'lin3': lin(v[-3:]), '12*max': 12 * max(v[-12:]), 'ttm*last3/prev3': ttm * sum(v[-3:]) / sum(v[-6:-3]),
         '4*last3*last3/prev3': 4 * sum(v[-3:]) ** 2 / sum(v[-6:-3]), 'ttm*2': 2 * ttm, 'ttm*(2-1/g)': ttm * (2 - a12 / L)}
    try: c['explin12'] = sum(math.exp(x) for x in [0]) and math.exp(lin([math.log(x) for x in v[-12:]], 1)) * 12
    except ValueError: pass
    return ttm, c
for last in ('2026-06', '2026-07'):
    i = months.index(last); res = collections.defaultdict(list); n = 0
    for t, R in snap.items():
        v = [m[t].get(k) for k in months[:i + 1]]
        if None in v or min(v) <= 0: continue
        n += 1; ttm, c = cands(v)
        for k, x in c.items(): res[k].append(math.log(x / (R * ttm)) if x > 0 else 9.0)
    print(f'latest month {last}: {n} companies')
    for k, e in sorted(res.items(), key=lambda kv: statistics.median(abs(x) for x in kv[1])):
        print(f'  {k:22s} median|log err| {statistics.median(abs(x) for x in e):.3f}  bias {statistics.median(e):+.3f}  exact(<0.2%) {sum(abs(x) < 0.002 for x in e)}  <2% {sum(abs(x) < 0.02 for x in e)}')
