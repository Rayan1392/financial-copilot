// Feature 125 change «responsive-comparison-table»: the row model shared by every layout of the
// own-industry comparison. It is parsed from the stored Markdown answer, so old (four-column) and
// current (six-column, with rank and trade evidence) answers keep rendering without a migration.

export const INDUSTRY_COMPARISON_HEADER = "| نماد | P/E | P/S | قیمت به تعادلی |";
export const INDUSTRY_COMPARISON_PRICE_HEADER = `${INDUSTRY_COMPARISON_HEADER} رتبه | قیمت و تاریخ معامله |`;

export const METRIC_LABELS = ["P/E", "P/S", "قیمت به تعادلی"] as const;

/** Where a member's ratio sits against the group average: lower is the favourable side. */
export type MetricStatus = "below" | "above" | "neutral";

export interface PriceEvidence {
  /** The whole cell text, shown as-is when it does not follow the current format. */
  raw: string;
  price?: string;
  source?: string;
  date?: string;
  equilibrium?: string;
}

export interface IndustryComparisonRow {
  symbol: string;
  metrics: string[];
  statuses: MetricStatus[];
  rank?: string;
  evidence?: PriceEvidence;
}

export interface IndustryComparisonModel {
  before: string;
  after: string;
  hasPriceEvidence: boolean;
  /** Member rows in the order the answer lists them. */
  members: IndustryComparisonRow[];
  benchmark?: { label: string; metrics: string[] };
}

export function isIndustryComparisonMarkdown(content: string): boolean {
  return content.includes(INDUSTRY_COMPARISON_HEADER);
}

export function splitMarkdownTableRow(line: string): string[] {
  return line
    .trim()
    .replace(/^\||\|$/g, "")
    .split("|")
    .map((cell) => cell.trim());
}

export function parsePersianPercent(value: string): number | null {
  const normalized = value
    .replace(/[۰-۹]/g, (digit) => String("۰۱۲۳۴۵۶۷۸۹".indexOf(digit)))
    .replace(/[٬,]/g, "")
    .replace(/٫/g, ".")
    .replace(/[٪%]/g, "")
    .trim();
  const parsed = Number.parseFloat(normalized);
  return Number.isFinite(parsed) ? parsed : null;
}

const EMPTY_CELL = /^[-—–]?$/;
// «۳۴۴۰ ریال (روزانه، ۱۴۰۵/۰۸/۳۰)، تعادلی ۷۲۳۲٫۱۷ ریال» — written by IndustryRelativeValuationSemanticContracts.
const PRICE_EVIDENCE = /^(.+?\sریال)\s*\((.+?)،\s*(.+?)\)،\s*تعادلی\s+(.+?\sریال)$/;

export function parsePriceEvidence(cell: string | undefined): PriceEvidence | undefined {
  const raw = cell?.trim() ?? "";
  if (EMPTY_CELL.test(raw)) return undefined;
  const match = PRICE_EVIDENCE.exec(raw);
  if (!match) return { raw };
  return { raw, price: match[1], source: match[2], date: match[3], equilibrium: match[4] };
}

function isBenchmarkLabel(label: string): boolean {
  return label === "میانگین گروه" || label.includes("میانگین صنعت");
}

export function parseIndustryComparison(content: string): IndustryComparisonModel | null {
  const lines = content.split("\n");
  const headerIndex = lines.findIndex((line) =>
    [INDUSTRY_COMPARISON_HEADER, INDUSTRY_COMPARISON_PRICE_HEADER].includes(line.trim()),
  );
  if (headerIndex < 0 || !lines[headerIndex + 1]?.trim().startsWith("|")) return null;
  const hasPriceEvidence = lines[headerIndex].trim() === INDUSTRY_COMPARISON_PRICE_HEADER;

  const parsed: Array<{ label: string; metrics: string[]; rank?: string; price?: string }> = [];
  let endIndex = headerIndex + 2;
  for (; endIndex < lines.length && lines[endIndex].trim().startsWith("|"); endIndex++) {
    const cells = splitMarkdownTableRow(lines[endIndex]);
    if (cells.length < 4 || !cells[0].replace(/[:-]/g, "").trim()) continue;
    parsed.push({ label: cells[0], metrics: cells.slice(1, 4), rank: cells[4], price: cells[5] });
  }

  const benchmarkRow = parsed.find((row) => isBenchmarkLabel(row.label));
  const benchmarkValues = (benchmarkRow?.metrics ?? []).map(parsePersianPercent);
  const statusOf = (value: string, index: number): MetricStatus => {
    const numeric = parsePersianPercent(value);
    const reference = benchmarkValues[index];
    if (numeric == null || reference == null) return "neutral";
    return numeric <= reference ? "below" : "above";
  };

  return {
    hasPriceEvidence,
    before: lines.slice(0, headerIndex).join("\n").trim(),
    after: lines.slice(endIndex).join("\n").trim(),
    members: parsed
      .filter((row) => row !== benchmarkRow && !isBenchmarkLabel(row.label))
      .map((row) => ({
        symbol: row.label,
        metrics: row.metrics,
        statuses: row.metrics.map(statusOf),
        rank: row.rank && !EMPTY_CELL.test(row.rank) ? row.rank : undefined,
        evidence: hasPriceEvidence ? parsePriceEvidence(row.price) : undefined,
      })),
    benchmark: benchmarkRow
      ? { label: benchmarkRow.label, metrics: benchmarkRow.metrics }
      : undefined,
  };
}
