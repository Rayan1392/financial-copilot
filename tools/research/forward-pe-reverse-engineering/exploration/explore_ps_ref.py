"""Exploration on the second reference batch: forward sales implied by the forward P/S of 1405/09/03.
S_f = price x shares / forward P/S. Shares come from TahlilApp's own TTM P/E gauge (so capital changes are right)."""
import pathlib, sys
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))
from lib import *
Q, P, M, S = quarters(), prices(), monthly(), screener()
G = {r['ticker'] + r['gauge']: float(r['close_ratio']) for r in rows('03_cw_gauges.csv')}
def shares(t):
    ttm = sum(line(x, 66) for x in Q[t][-4:])
    return G[t + 'PE'] * ttm / P[t]['2026-11-21']['last']
print('S_f / (TTM monthly x yoy growth of last n months), price = last 09/03; a formula fits when the value is ~1.00 for all')
hdr = ['n=1', 'n=2', 'n=3', 'n=4', 'n=5', 'n=6', 'n=7', 'fiscalYTD']
print('ticker | S_f bn | S_f/TTM_m | ' + ' | '.join(hdr) + ' | rounding band +-%')
for t in TICKERS:
    m = [M[t][k] for k in sorted(M[t])]; sh = shares(t); p = P[t]['2026-11-24']['last']
    sf = p * sh / REF_PS[t]; ttm = sum(m[-12:]); fm = Q[t][-1]['fm'] + 1
    out = []
    for n in list(range(1, 8)) + [fm]:
        n = min(n, 7); g = sum(m[-n:]) / sum(m[-12 - n:-12]); out.append(sf / (ttm * g))
    print(f"{t} | {sf/1e9:,.0f} | {sf/ttm:.3f} | " + ' | '.join(f'{x:.3f}' for x in out) + f" | {0.5 / REF_PS[t]:.1f}")
