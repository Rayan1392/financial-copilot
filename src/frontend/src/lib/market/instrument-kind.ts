/**
 * TSE naming: a rights issue (حق تقدم) trades under the parent symbol plus a final «ح» (شگل → شگلح). It has
 * a price but no financial statements or monthly sales of its own, so analysis prompts about it would be
 * answered for the wrong instrument. The followed-symbols API carries no instrument kind, so the name rule is
 * the single source here.
 */
const RIGHTS_SUFFIX = "ح";

export function isRightsIssueSymbol(symbol: string): boolean {
  const trimmed = symbol.trim();
  // Needs a parent symbol before the suffix; «ح» alone is not a rights ticker.
  return trimmed.length > 1 && trimmed.endsWith(RIGHTS_SUFFIX);
}
