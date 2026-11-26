"""Exploration: quarterly-statement candidates for forward sales on the 2026-07-29 same-price snapshot."""
import math, statistics, collections
import pathlib, sys
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))
from lib import *
m = collections.defaultdict(dict)
for r in rows('07_monthly_sales_all.csv'):
    m[r['ticker']][r['period_end'][:7]] = m[r['ticker']].get(r['period_end'][:7], 0) + float(r['sales_million_rial'] or 0) * 1e6
q = collections.defaultdict(list)
for r in rows('08_cw_quarterly_all.csv'):
    if r['period_end'] <= '2026-06-30' and r['revenue_q']: q[r['ticker']].append(float(r['revenue_q']))
snap = {r['ticker']: float(r['ttm_ps']) / float(r['forward_ps']) for r in rows('06_forward_ps_snapshots.csv')
        if r['obs_date'] == '2026-07-29' and float(r['forward_ps']) > 0 and float(r['ttm_ps']) > 0}
months = ['2025-%02d' % i for i in range(7, 13)] + ['2026-%02d' % i for i in range(1, 7)]
res = collections.defaultdict(list); n = 0; detail = {}
for t, R in snap.items():
    v = [m[t].get(k) for k in months]; s = q.get(t, [])
    if None in v or len(s) < 8 or min(s[-8:]) <= 0: continue
    n += 1; ttm_m = sum(v); sf = R * ttm_m; ttm_q = sum(s[-4:]); prev = sum(s[-8:-4])
    c = {'ttm_q': ttm_q, '4*Q0': 4 * s[-1], 'ttm_q*Q0/Q4': ttm_q * s[-1] / s[-5], 'ttm_q*ttm/prev': ttm_q ** 2 / prev,
         '4*Q0*Q0/Q4': 4 * s[-1] ** 2 / s[-5], '4*Q0*Q0/Q1': 4 * s[-1] ** 2 / s[-2], '4*Q0*ttm/prev': 4 * s[-1] * ttm_q / prev,
         '2*(Q0+Q1)': 2 * (s[-1] + s[-2]), 'ttm_q*Q0/avg4': ttm_q * s[-1] / (sum(s[-5:-1]) / 4), '4*Q0*Q0/avg4': 4 * s[-1] ** 2 / (sum(s[-5:-1]) / 4)}
    detail[t] = (sf, ttm_m, ttm_q, s[-5:])
    for k, x in c.items(): res[k].append(math.log(x / sf))
print(n, 'companies')
for k, e in sorted(res.items(), key=lambda kv: statistics.median(abs(x) for x in kv[1])):
    print(f'  {k:18s} median|log err| {statistics.median(abs(x) for x in e):.3f}  bias {statistics.median(e):+.3f}  exact(<0.2%) {sum(abs(x) < 0.002 for x in e)}  <2% {sum(abs(x) < 0.02 for x in e)}')
print('S_f/TTM_m distribution:', [round(x, 2) for x in statistics.quantiles([d[0] / d[1] for d in detail.values()], n=10)])
print('TTM_q/TTM_m distribution:', [round(x, 2) for x in statistics.quantiles([d[2] / d[1] for d in detail.values()], n=10)])
