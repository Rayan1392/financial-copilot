# Gauge Reverse Engineering Notes

Validated against the same-symbol شراز (`IRO1PRZZ0001`) and غگلپا (`IRO3PGPZ0001`) API/UI captures.

Conclusion: a-f values are fixed histogram bucket populations.

They are not equal quantiles.

The six arcs are equal-width histogram bins. Visual low-to-high order is `a,b,c,d,e,f`; each arc is
30 degrees. Bucket population controls only the percentage label (`count / total * 100`), not arc
width. The screenshots show the visible gauge axis as `min..max`, split into six equal numeric
intervals. The needle is driven by `ps-data.ps_ratio`, mapped linearly to `min..max` and clamped at
either edge. `start`/`end` remain provider range facts but are not the rendered gauge axis.

`ps-data.close` remains the separate Forward P/S fact and circle `close` remains separate provider
evidence. Neither drives the needle in the verified captures.
