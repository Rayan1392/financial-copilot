# Equilibrium price in whole rials

Status: Gate A: Approved by the owner on 2026-11-24

## Current behavior

In the own-industry comparison, the trade evidence column shows the equilibrium price with up to two decimals («تعادلی ۷۲۳۲٫۱۷ ریال»). `IndustryRelativeValuationPresentation.PriceEvidence` formats it with `0.##`. Stored answers in conversation history carry the same text.

## Change

- Round the equilibrium price to the nearest whole rial, half away from zero, where the answer text is built. New answers on the web and in the Telegram table image show «تعادلی ۷۲۳۲ ریال».
- The web comparison renderer also rounds the equilibrium amount it parses from stored answers, so earlier conversations show the same whole-rial value. Text that does not match the expected «‹digits›٫‹digits› ریال» shape stays unchanged.
- The market price, ratios, benchmarks and the stored valuation data are unchanged. No migration.

## Acceptance criteria

- A new own-industry comparison shows the equilibrium price without decimals (۷۲۳۲٫۱۷ → ۷۲۳۲, ۱۵۰۲۱٫۵ → ۱۵۰۲۲).
- A stored answer with «تعادلی ۷۲۳۲٫۱۷ ریال» shows «۷۲۳۲ ریال» in every web layout (wide table, compact table, cards).
