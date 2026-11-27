// Stand-ins for the browser fixture of the starter cards (`scripts/starter-cards.browser.mjs` aliases
// `@tanstack/react-start` and `@/lib/metadata.functions` to this file): the fixture has no server, so the symbol
// search answers from a fixed list. The search text «خطا» fails, to show the failed state.
const SYMBOLS = [
  { externalCompanyId: "1", symbolCode: "فولاد", companyName: "فولاد مبارکه اصفهان" },
  { externalCompanyId: "2", symbolCode: "فولاژ", companyName: "فولاد آلیاژی ایران" },
  { externalCompanyId: "3", symbolCode: "فولادح", companyName: "ح. فولاد مبارکه اصفهان" },
  { externalCompanyId: "4", symbolCode: "شپنا", companyName: "پالایش نفت اصفهان" },
  {
    externalCompanyId: "5",
    symbolCode: "وتوصا",
    companyName: "گروه سرمایه‌گذاری توسعه صنعتی ایران با نامی بلند برای آزمون بریده‌شدن متن",
  },
];

export const useServerFn = <T>(fn: T): T => fn;

export async function searchSymbolMetadata({ data }: { data: { search?: string; limit: number } }) {
  await new Promise((resolve) => setTimeout(resolve, 150));
  const term = data.search ?? "";
  if (term === "خطا") throw new Error("search failed");
  return SYMBOLS.filter(
    (item) => item.symbolCode.includes(term) || item.companyName.includes(term),
  ).slice(0, data.limit);
}
