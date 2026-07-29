# Feature 114 - CyclicalWaves P/S Visualization Data Sync (Updated)

## Gauge Contract Clarification

Based on multiple verified CyclicalWaves responses, the Gauge provider
contract is now defined.

The provider returns: - a,b,c,d,e,f: six histogram bucket populations -
start/end: retained provider range facts - min/max: rendered gauge axis - avg: provider reference marker

The implementation must not calculate quantiles locally.

## Segment Calculation

Total buckets: a+b+c+d+e+f

Visual low-to-high rendering order: a -\> b -\> c -\> d -\> e -\> f

Segment percentage: segmentValue / total

Each segment occupies exactly 30 degrees. Population changes the displayed percentage, not arc width.

Persist raw values for deterministic rendering.
