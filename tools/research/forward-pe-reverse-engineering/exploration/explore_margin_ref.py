"""Exploration: implied forward net margin = forward P/S / forward P/E (price and shares cancel out).
Counts, for each candidate margin rule, how many of the 10 symbols fall inside the rounding interval."""
import pathlib, sys, statistics
sys.path.insert(0, str(pathlib.Path(__file__).resolve().parent.parent))
from lib import *
Q, M = quarters(), monthly()
band = {t: ((REF_PS[t] - .005) / (REF_PE[t] + .005), (REF_PS[t] + .005) / (REF_PE[t] - .005)) for t in TICKERS}
def series(t):
    q = Q[t][-12:]
    return {'np': [line(x, 66) for x in q], 'rev': [line(x, REV_LINES) for x in q], 'op': [line(x, 56) for x in q],
            'gp': [line(x, 52) for x in q], 'pre': [line(x, (62, 60)) or line(x, 66) for x in q]}
cands = {}
for num_ in ('np', 'op', 'pre'):
    for lag in range(0, 5):
        for w in range(1, 9):
            def f(s, num_=num_, lag=lag, w=w):
                e = len(s['np']) - lag; a = s[num_][e - w:e]; b = s['rev'][e - w:e]
                return sum(a) / sum(b) if len(a) == w and None not in a and sum(b) else None
            cands[f'{num_} pooled w={w} lag={lag}'] = f
            def g(s, num_=num_, lag=lag, w=w):
                e = len(s['np']) - lag; a = s[num_][e - w:e]; b = s['rev'][e - w:e]
                return statistics.mean(x / y for x, y in zip(a, b)) if len(a) == w and None not in a else None
            def h(s, num_=num_, lag=lag, w=w):
                e = len(s['np']) - lag; a = s[num_][e - w:e]; b = s['rev'][e - w:e]
                return statistics.median(x / y for x, y in zip(a, b)) if len(a) == w and None not in a else None
            if w > 1: cands[f'{num_} mean-of-margins w={w} lag={lag}'] = g; cands[f'{num_} median w={w} lag={lag}'] = h
res = []
for name, f in cands.items():
    vals = {t: f(series(t)) for t in TICKERS}
    inside = sum(v is not None and band[t][0] <= v <= band[t][1] for t, v in vals.items())
    err = statistics.median(abs(v / (REF_PS[t] / REF_PE[t]) - 1) for t, v in vals.items() if v is not None)
    res.append((inside, -err, name, vals))
res.sort(reverse=True)
print('implied margin %:', {t: round(REF_PS[t] / REF_PE[t] * 100, 2) for t in TICKERS})
for inside, e, name, vals in res[:12]:
    print(f'{inside}/10 inside band, median rel err {-e*100:.1f}% | {name} | ' + ' '.join(f'{v*100:.1f}' if v is not None else '-' for v in vals.values()))
print(); print('quarterly net margins (oldest..latest, last 8):')
for t in TICKERS:
    s = series(t); print(t, [round(a / b * 100, 1) for a, b in zip(s['np'][-8:], s['rev'][-8:])], 'implied', round(REF_PS[t] / REF_PE[t] * 100, 2))
