# Starter cards for cash flow and industry comparison

Status: Gate A: Approved by the owner on 2026-11-24

## Change

The shared new-chat starter list (`src/frontend/src/lib/chat/starter-prompts.ts`, used by desktop `/chat` and mobile `/m`) gets two cards. The list goes from six to eight cards.

| Card | Question sent | Topic label | Tone | Position |
|---|---|---|---|---|
| `industry-comparison` | «شاراک را با صنعت خودش مقایسه کن» | مقایسه با صنعت | sales (green), like the monthly-report ranking | after the ranking card |
| `cash-flow` | «جریان وجه نقد اخابر» | جریان وجوه نقد | statement (gold), like the quarterly report and balance sheet | after the balance-sheet card |

- The question texts are the owner's wording. The cash-flow topic uses «جریان وجوه نقد», the title of the cash-flow report in the app; the question keeps «وجه», which the cash-flow routing already recognizes.
- Icons: desktop lucide `ArrowLeftRight` and `Banknote`; mobile Phosphor `ArrowsLeftRight` and `Money`, added to `components/mobile/icons.ts`.
- The desktop intro sentence lists the two new capabilities.
- Both cards define `forSymbol` like the other per-symbol cards, so they follow watchlist personalization if it is turned back on (it is off).

## Acceptance criteria

- Desktop `/chat` and mobile `/m` show eight cards in this order: green cards first, then the gold ones, in both themes, with no horizontal overflow.
- Selecting a new card sends exactly its question text.
