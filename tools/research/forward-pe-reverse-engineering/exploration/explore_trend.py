"""Exploration: linear and log-linear trend extrapolations (quarterly and monthly) as forward-sales candidates."""
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
mk = ['2025-%02d' % i for i in range(4, 13)] + ['2026-%02d' % i for i in range(1, 8)]
def fit(v):
    n = len(v); xb = (n - 1) / 2; yb = sum(v) / n
    s = sum((i - xb) * (y - yb) for i, y in enumerate(v)) / sum((i - xb) ** 2 for i in range(n))
    return lambda k: yb + s * (n - 1 + k - xb)       # k steps after the last point
res = collections.defaultdict(list)
for t, R in snap.items():
    v = [m[t].get(k) for k in mk]; s = q.get(t, [])
    if None in v or min(v) <= 0 or len(s) < 10 or min(s[-10:]) <= 0: continue
    sf = R * sum(v[3:15]); c = {}
    for n in (4, 5, 6, 8, 10):
        f = fit(s[-n:]); c[f'linQ{n}'] = sum(f(k) for k in range(1, 5))
        g = fit([math.log(x) for x in s[-n:]]); c[f'expQ{n}'] = sum(math.exp(g(k)) for k in range(1, 5))
    for n in (6, 9, 12, 15):
        f = fit(v[15 - n:15]); c[f'linM{n}'] = sum(f(k) for k in range(1, 13))
        g = fit([math.log(x) for x in v[15 - n:15]]); c[f'expM{n}'] = sum(math.exp(g(k)) for k in range(1, 13))
    ttm = sum(s[-4:]); prev = sum(s[-8:-4])
    c['2ttm-prev'] = 2 * ttm - prev; c['ttm^2/prev'] = ttm * ttm / prev
    c['cagr2y'] = ttm * math.sqrt(ttm / sum(s[-10:-6])) if len(s) >= 10 else 0
    for k, x in c.items(): res[k].append(math.log(x / sf) if x > 0 else 9)
print(len(next(iter(res.values()))), 'companies')
for k, e in sorted(res.items(), key=lambda kv: statistics.median(abs(x) for x in kv[1])):
    print(f'  {k:12s} median|log err| {statistics.median(abs(x) for x in e):.3f}  bias {statistics.median(e):+.3f}  exact(<0.2%) {sum(abs(x) < 0.002 for x in e)}  <2% {sum(abs(x) < 0.02 for x in e)}  <5% {sum(abs(x) < 0.05 for x in e)}')
