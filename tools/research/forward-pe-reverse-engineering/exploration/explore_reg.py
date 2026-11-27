"""Exploration: OLS of log(S_f/TTM) on growth features (pure Python) to see what drives TahlilApp's forward sales."""
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
def solve(A, b):
    n = len(A); M = [r[:] + [b[i]] for i, r in enumerate(A)]
    for i in range(n):
        p = max(range(i, n), key=lambda r: abs(M[r][i])); M[i], M[p] = M[p], M[i]
        for r in range(n):
            if r != i:
                f = M[r][i] / M[i][i]; M[r] = [x - f * y for x, y in zip(M[r], M[i])]
    return [M[i][n] / M[i][i] for i in range(n)]
def ols(X, y, names):
    X = [[1.0] + r for r in X]; k = len(X[0])
    A = [[sum(r[i] * r[j] for r in X) for j in range(k)] for i in range(k)]; b = [sum(r[i] * yy for r, yy in zip(X, y)) for i in range(k)]
    beta = solve(A, b); res = [yy - sum(bi * xi for bi, xi in zip(beta, r)) for r, yy in zip(X, y)]
    r2 = 1 - sum(e * e for e in res) / sum((yy - statistics.mean(y)) ** 2 for yy in y)
    print('  n=%d R2=%.3f  resid sd %.3f | ' % (len(y), r2, statistics.pstdev(res)) + '  '.join(f'{n}={b:+.3f}' for n, b in zip(['const'] + names, beta)))
    return res
data = []
for t, R in snap.items():
    v = [m[t].get(k) for k in mk]; s = q.get(t, [])
    if None in v or min(v) <= 0 or len(s) < 8 or min(s[-8:]) <= 0: continue
    khor = v[:15]; ttm = sum(khor[-12:])
    data.append((t, math.log(R), {
        'yoyQ0': math.log(s[-1] / s[-5]), 'yoyQ1': math.log(s[-2] / s[-6]), 'yoyQ2': math.log(s[-3] / s[-7]), 'yoyQ3': math.log(s[-4] / s[-8]),
        'qoq': math.log(s[-1] / s[-2]), '4Q0/ttm': math.log(4 * s[-1] / sum(s[-4:])), 'ttm/prev': math.log(sum(s[-4:]) / sum(s[-8:-4])),
        '12L/ttm': math.log(12 * khor[-1] / ttm), 'L/LY': math.log(khor[-1] / khor[-13]), 'last3yoy': math.log(sum(khor[-3:]) / sum(khor[-15:-12])),
        '12Tir/ttm': math.log(12 * v[15] / ttm), 'Tir/LY': math.log(v[15] / v[3])}))
y = [d[1] for d in data]
for names in (['yoyQ0'], ['ttm/prev'], ['yoyQ0', 'ttm/prev'], ['yoyQ0', 'yoyQ1', 'yoyQ2', 'yoyQ3'], ['yoyQ0', '4Q0/ttm'], ['yoyQ0', 'qoq', 'ttm/prev', '4Q0/ttm'],
              ['L/LY', '12L/ttm'], ['last3yoy', '12L/ttm'], ['Tir/LY', '12Tir/ttm'], ['yoyQ0', '4Q0/ttm', 'L/LY', '12L/ttm', 'Tir/LY', '12Tir/ttm']):
    ols([[d[2][n] for n in names] for d in data], y, names)
