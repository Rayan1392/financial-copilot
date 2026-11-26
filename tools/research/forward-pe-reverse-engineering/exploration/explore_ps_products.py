"""Exploration: forward sales vs product-level candidates (quantity x rate) and trend fits, second reference batch."""
import pathlib, sys, math, collections
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))
from lib import *
Q, P, M = quarters(), prices(), monthly()
G = {r['ticker'] + r['gauge']: float(r['close_ratio']) for r in rows('03_cw_gauges.csv')}
prod = collections.defaultdict(lambda: collections.defaultdict(dict))
for r in rows('09_noavaran_monthly_products.csv'):
    prod[r['ticker']][r['product_code']][r['period_end'][:7]] = (num(r['sales_quantity']) or 0, num(r['sales_rate']) or 0, (num(r['sales_amount_million_rial']) or 0) * 1e6)
def fit(v):
    n = len(v); xb = (n - 1) / 2; yb = sum(v) / n
    s = sum((i - xb) * (y - yb) for i, y in enumerate(v)) / sum((i - xb) ** 2 for i in range(n))
    return lambda k: yb + s * (n - 1 + k - xb)
print('ratio S_f / candidate (1.00 = match)')
for t in TICKERS:
    m = [M[t][k] for k in sorted(M[t])]; keys = sorted(M[t])[-12:]
    sh = G[t + 'PE'] * sum(line(x, 66) for x in Q[t][-4:]) / P[t]['2026-11-21']['last']
    sf = P[t]['2026-11-24']['last'] * sh / REF_PS[t]; c = {}
    a = b = d = 0.0
    for code, bm in prod[t].items():
        last = [k for k in keys if bm.get(k, (0, 0, 0))[1] > 0 and bm[k][0] > 0]
        if not last: continue
        rate = bm[last[-1]][1] * 1e6
        a += sum(bm.get(k, (0, 0, 0))[0] for k in keys) * rate
        b += sum(bm.get(k, (0, 0, 0))[0] for k in keys[-3:]) * 4 * rate
        d += sum(bm.get(k, (0, 0, 0))[0] for k in keys[-6:]) * 2 * rate
    if a: c['TTMqty*lastRate'] = a; c['3m qty*4*lastRate'] = b; c['6m qty*2*lastRate'] = d
    for n in (6, 12, 19):
        f = fit(m[-n:]); c[f'lin{n}'] = sum(f(k) for k in range(1, 13))
        if min(m[-n:]) > 0:
            g = fit([math.log(x) for x in m[-n:]]); c[f'exp{n}'] = sum(math.exp(g(k)) for k in range(1, 13))
    print(t, f'S_f={sf/1e9:,.0f}', ' '.join(f'{k}={sf/v:.3f}' for k, v in c.items() if v > 0))
