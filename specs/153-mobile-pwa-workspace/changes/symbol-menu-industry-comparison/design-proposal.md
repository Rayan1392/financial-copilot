# Industry comparison in the symbol menu

Status: Gate A: Approved by the owner on 2026-11-24

## Change

The symbol prompts that open from a symbol in the market movers and the watchlist (desktop `symbol-actions-menu.tsx`) and in the mobile symbol sheet (`symbol-sheet.tsx`) come from one owner, `src/frontend/src/lib/chat/symbol-prompts.ts` (Feature 153 decision D-Q1). A fourth prompt is added after «ترازنامه ‹نماد›» and before «پرسش دیگری درباره …»:

- id `industry-comparison`, text «‹نماد› را با صنعت خودش مقایسه کن» (the same wording as the home starter card and the comparison follow-ups).
- Mobile sheet: the two-way-arrows icon (`IndustryComparisonIcon`) in the green tone, like monthly sales, matching the green group of the home starter cards. Statement prompts stay gold.
- Desktop menu: the existing sparkle icon used by every analysis prompt.

## Acceptance criteria

- The desktop menu from the movers and from the watchlist, and the mobile symbol sheet, show the new prompt as the fourth item in both themes; selecting it sends «‹نماد› را با صنعت خودش مقایسه کن».
