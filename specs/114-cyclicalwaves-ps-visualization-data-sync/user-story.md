# Feature 114 - CyclicalWaves P/S Visualization Data Sync (Updated)

## Gauge Contract Clarification

Based on multiple verified CyclicalWaves responses, the Gauge provider
contract is now defined.

The provider returns: - a,b,c,d,e,f: six histogram bucket populations -
start/end: the outer numeric range of the rendered gauge - min/max: the inner
numeric range of the rendered gauge - avg: provider reference marker

The implementation must not calculate quantiles locally.

## Segment Calculation

Total buckets: a+b+c+d+e+f

Visual low-to-high rendering order: a -\> b -\> c -\> d -\> e -\> f

Segment percentage: segmentValue / total

Each segment occupies exactly 30 degrees. Population changes the displayed percentage, not arc width.

The six numeric segments are not one linear `min..max` axis:

1. Segment `a` spans `start..min`.
2. Segments `b..e` divide `min..max` into four equal numeric intervals.
3. Segment `f` spans `max..end`.

`start` and `end` are therefore the outer gauge boundaries. `min` is the
upper boundary of the first segment, and `max` is the lower boundary of the
last segment. Values are mapped piecewise within these six numeric intervals
and clamped only below `start` or above `end`.

Persist the raw boundary facts and the derived segment boundaries for
deterministic rendering.
