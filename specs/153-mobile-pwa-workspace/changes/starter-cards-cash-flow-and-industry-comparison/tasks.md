# Implement the cash-flow and industry-comparison starter cards

Status: Gate B: Approved by the owner on 2026-11-24

- [x] Add `industry-comparison` and `cash-flow` to `STARTER_PROMPTS` (ids, texts, topics, tones, icons, `forSymbol`), the phone icons in `components/mobile/icons.ts` and `routes/m.index.tsx`, and the desktop intro sentence in `routes/_app.chat.tsx`.
- [x] Update `starter-prompts.test.ts` (eight cards, personalized order) and `ui-refresh.test.tsx` (texts and tone order).
- [x] Run the starter, mobile and terminology tests, lint and the build. The terminology test reports only the three existing hits in `profile-editor.tsx` and `grounded-answer-layout-browser-fixture.tsx`.
- [x] Screenshot desktop `/chat` (1440, 768) and mobile `/m` (390) in both themes from a production build against the mock API; no horizontal overflow, both cards present. Screenshots in `evidence/`.
