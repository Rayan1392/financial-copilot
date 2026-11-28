// Feature 075 change «product-mix-answer-image-share»: the product revenue mix answer («ترکیب فروش محصولات ‹نماد›»)
// reaches the web app as Markdown only. It is recognised by the heading and the table header the server writes
// (`BuildProductRevenueMixContent`), so the answer can offer its picture.

const HEADING = /^###\s+ترکیب درآمد محصولات\s+—\s+(.+)$/m;
const TABLE_HEADER = "| ردیف | محصول | فروش (تومان) | سهم (٪) | غالب |";
// «معدنی و صنعتی گهر زمین (کگهر)»: the symbol is the last parenthesised part of the company label.
const SYMBOL_IN_LABEL = /\(([^()]+)\)\s*$/;

export interface ProductRevenueMixAnswer {
  /** The symbol from the heading, or the whole label when the answer names the company by symbol only. */
  symbol: string;
}

/** The product revenue mix answer in this Markdown, or null when the text is any other answer. */
export function parseProductRevenueMix(content: string): ProductRevenueMixAnswer | null {
  if (!content.includes(TABLE_HEADER)) return null;
  const label = HEADING.exec(content)?.[1].trim();
  if (!label) return null;
  return { symbol: SYMBOL_IN_LABEL.exec(label)?.[1].trim() || label };
}
