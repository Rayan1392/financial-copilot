import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";
import rehypeSanitize from "rehype-sanitize";
import type { Components } from "react-markdown";
import { Fragment, useState } from "react";
import { ChevronDown } from "lucide-react";

const MONTHLY_SALES_QUALITY_RANKING_HEADER =
  "| رتبه | نماد | شرکت | صنعت | امتیاز کیفیت | برچسب | دلیل اصلی | اطمینان |";

const FINANCIAL_STATEMENT_MARKERS = ["خلاصه سود و زیان", "صورت مالی", "درآمد عملیاتی", "سود/زیان"];

function isMonthlySalesQualityRankingMarkdown(content: string): boolean {
  return content.includes(MONTHLY_SALES_QUALITY_RANKING_HEADER);
}

function isFinancialStatementMarkdown(content: string): boolean {
  return FINANCIAL_STATEMENT_MARKERS.some((marker) => content.includes(marker));
}

const INVESTMENT_DISCLAIMER = "این اطلاعات توصیه به سرمایه گذاری نیست";

const INDUSTRY_RELATIVE_VALUATION_HEADER =
  "| نماد | P/E | P/S | قیمت به تعادلی |";

interface IndustryRelativeValuationTable {
  before: string;
  after: string;
  groupTitle?: string;
  rows: Array<{ company: string; metrics: string[]; benchmark: boolean }>;
}

function isIndustryRelativeValuationMarkdown(content: string): boolean {
  return content.includes(INDUSTRY_RELATIVE_VALUATION_HEADER);
}

function parseIndustryRelativeValuationTable(content: string): IndustryRelativeValuationTable | null {
  const lines = content.split("\n");
  const headerIndex = lines.findIndex((line) => line.trim() === INDUSTRY_RELATIVE_VALUATION_HEADER);
  if (headerIndex < 0 || !lines[headerIndex + 1]?.trim().startsWith("|")) return null;

  const rows: IndustryRelativeValuationTable["rows"] = [];
  let endIndex = headerIndex + 2;
  while (endIndex < lines.length && lines[endIndex].trim().startsWith("|")) {
    const cells = splitMarkdownTableRow(lines[endIndex]);
    if (cells.length >= 4 && !cells[0].replace(/[:-]/g, "").trim()) continue;
    if (cells.length >= 4)
      rows.push({ company: cells[0], metrics: cells.slice(1, 4), benchmark: cells[0].includes("میانگین صنعت") });
    endIndex++;
  }

  return {
    before: lines.slice(0, headerIndex).join("\n").trim(),
    after: lines.slice(endIndex).join("\n").trim(),
    groupTitle: lines
      .slice(0, headerIndex)
      .join(" ")
      .match(/\*\*گروه صنعتی:\*\*\s*(.*?)(?=\s+\*\*وضعیت داده:\*\*|\s*$)/)?.[1]?.trim(),
    rows,
  };
}

function IndustryRelativeValuationTable({ rows, groupTitle }: { rows: IndustryRelativeValuationTable["rows"]; groupTitle?: string }) {
  const benchmark = rows.find((row) => row.benchmark)?.metrics ?? [];
  const members = rows.filter((row) => !row.benchmark);
  const status = (value: string, index: number) => {
    const numericValue = parsePersianPercent(value);
    const numericBenchmark = parsePersianPercent(benchmark[index] ?? "");
    if (numericValue == null || numericBenchmark == null) return "neutral";
    return numericValue <= numericBenchmark ? "green" : "red";
  };

  return (
    <div className="my-2 overflow-x-auto rounded-lg ring-1 ring-hairline" dir="rtl">
      <table className="w-full min-w-[460px] table-fixed border-collapse text-sm">
        {groupTitle && <caption className="border-b border-hairline bg-slate-100 px-3 py-2 text-right text-sm font-semibold text-slate-700">{groupTitle}</caption>}
        <colgroup>
          <col className="w-[42%]" />
          <col className="w-[19%]" />
          <col className="w-[19%]" />
          <col className="w-[20%]" />
        </colgroup>
        <thead className="bg-white/5">
          <tr>
            <th className="px-3 py-2 text-right text-xs font-medium text-muted-foreground">نماد</th>
            <th className="px-3 py-2 text-right text-xs font-medium text-muted-foreground">P/E</th>
            <th className="px-3 py-2 text-right text-xs font-medium text-muted-foreground">P/S</th>
            <th className="px-3 py-2 text-right text-xs font-medium text-muted-foreground">قیمت به تعادلی</th>
          </tr>
        </thead>
        <tbody>
          {members.map((row, index) => (
            <tr key={`${row.company}:${index}`} className="border-b border-hairline text-slate-800">
              <td className="bg-surface px-3 py-2 align-top font-medium">{row.company}</td>
              {row.metrics.map((value, metricIndex) => (
                <td key={`${row.company}:${metricIndex}`} className={`px-3 py-2 align-top tabular-nums ${status(value, metricIndex) === "green" ? "bg-emerald-100" : status(value, metricIndex) === "red" ? "bg-rose-100" : "bg-surface"}`}>
                  {value}
                </td>
              ))}
            </tr>
          ))}
          {rows.filter((row) => row.benchmark).map((row) => (
            <tr key={row.company} className="border-t-2 border-slate-300 bg-slate-100 font-semibold text-slate-800">
              <td className="px-3 py-2">{row.company}</td>
              {row.metrics.map((value, index) => <td key={index} className="px-3 py-2 tabular-nums">{value}</td>)}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function parsePersianPercent(value: string): number | null {
  const normalized = value
    .replace(/[۰-۹]/g, (digit) => String("۰۱۲۳۴۵۶۷۸۹".indexOf(digit)))
    .replace(/[٫٬]/g, ".")
    .replace(/٪/g, "")
    .replace(/,/g, "")
    .trim();
  const parsed = Number.parseFloat(normalized);
  return Number.isFinite(parsed) ? parsed : null;
}

function formatFinancialStatementMarkdown(content: string): string {
  return content
    .split("\n")
    .map((line) => {
      if (/^\s*(?:[-*+]\s+|\d+[.)]\s+|\|)/.test(line)) {
        return line;
      }

      return line.replace(/([.!؟])\s+(?=(?:✅\s*)?[\u0600-\u06FF])/g, "$1\n\n");
    })
    .join("\n");
}

interface MonthlySalesQualityRow {
  rank: string;
  symbol: string;
  company: string;
  industry: string;
  score: string;
  label: string;
  reason: string;
  confidence: string;
}

function splitMarkdownTableRow(line: string): string[] {
  return line
    .trim()
    .replace(/^\||\|$/g, "")
    .split("|")
    .map((cell) => cell.trim());
}

function parseMonthlySalesQualityTable(content: string): {
  before: string;
  after: string;
  rows: MonthlySalesQualityRow[];
} | null {
  const lines = content.split("\n");
  const headerIndex = lines.findIndex((line) => line.trim() === MONTHLY_SALES_QUALITY_RANKING_HEADER);
  if (headerIndex < 0 || !lines[headerIndex + 1]?.trim().startsWith("|")) return null;

  const rows: MonthlySalesQualityRow[] = [];
  let endIndex = headerIndex + 2;
  while (endIndex < lines.length && lines[endIndex].trim().startsWith("|")) {
    const cells = splitMarkdownTableRow(lines[endIndex]);
    if (cells.length >= 8) {
      rows.push({
        rank: cells[0],
        symbol: cells[1],
        company: cells[2],
        industry: cells[3],
        score: cells[4],
        label: cells[5],
        reason: cells[6],
        confidence: cells[7],
      });
    }
    endIndex++;
  }

  return {
    before: lines.slice(0, headerIndex).join("\n").trim(),
    after: lines.slice(endIndex).join("\n").trim(),
    rows,
  };
}

function RankingDetails({ row }: { row: MonthlySalesQualityRow }) {
  return (
    <div className="grid gap-2 rounded-lg bg-background/60 p-3 text-xs text-muted-foreground sm:grid-cols-2">
      <div>
        <span className="font-medium text-foreground">برچسب: </span>
        {row.label || "-"}
      </div>
      <div>
        <span className="font-medium text-foreground">اطمینان: </span>
        {row.confidence || "-"}
      </div>
      <div className="sm:col-span-2">
        <span className="font-medium text-foreground">دلیل اصلی: </span>
        {row.reason || "-"}
      </div>
    </div>
  );
}

function MonthlySalesQualityRanking({ rows }: { rows: MonthlySalesQualityRow[] }) {
  const [expanded, setExpanded] = useState<Set<number>>(() => new Set());
  const toggle = (index: number) => {
    setExpanded((current) => {
      const next = new Set(current);
      if (next.has(index)) next.delete(index);
      else next.add(index);
      return next;
    });
  };

  return (
    <div className="my-2 rounded-lg ring-1 ring-hairline" dir="rtl">
      <div className="hidden overflow-hidden md:block" dir="rtl">
        <table className="w-full table-fixed border-collapse text-sm">
          <colgroup>
            <col className="w-[9%]" />
            <col className="w-[13%]" />
            <col className="w-[25%]" />
            <col className="w-[22%]" />
            <col className="w-[16%]" />
            <col className="w-[15%]" />
          </colgroup>
          <thead className="bg-white/5">
            <tr>
              {[
                "رتبه",
                "نماد",
                "شرکت",
                "صنعت",
                "امتیاز کیفیت",
                "جزئیات",
              ].map((header) => (
                <th key={header} className="border-b border-hairline px-3 py-2 text-right text-xs font-medium text-muted-foreground">
                  {header}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {rows.map((row, index) => (
              <Fragment key={`item-${index}`}>
                <tr key={`row-${index}`} className="border-b border-hairline last:border-b-0">
                  <td className="px-3 py-3 text-center font-semibold text-foreground">{row.rank}</td>
                  <td className="px-3 py-3 font-medium text-foreground">{row.symbol}</td>
                  <td className="px-3 py-3 text-foreground/80">{row.company || "-"}</td>
                  <td className="px-3 py-3 text-foreground/80">{row.industry || "-"}</td>
                  <td className="px-3 py-3 font-semibold text-emerald">{row.score || "-"}</td>
                  <td className="px-3 py-3">
                    <button
                      type="button"
                      onClick={() => toggle(index)}
                      aria-expanded={expanded.has(index)}
                      className="inline-flex items-center gap-1 rounded-md px-2 py-1 text-xs text-emerald transition hover:bg-emerald-soft"
                    >
                      جزئیات
                      <ChevronDown className={`size-3 transition-transform ${expanded.has(index) ? "rotate-180" : ""}`} />
                    </button>
                  </td>
                </tr>
                {expanded.has(index) && (
                  <tr key={`details-${index}`} className="border-b border-hairline bg-surface/40">
                    <td colSpan={6} className="px-3 py-3">
                      <RankingDetails row={row} />
                    </td>
                  </tr>
                )}
              </Fragment>
            ))}
          </tbody>
        </table>
      </div>

      <div className="space-y-2 p-2 md:hidden">
        {rows.map((row, index) => (
          <article key={index} className="rounded-lg border border-hairline bg-surface/40 p-3">
            <div className="grid grid-cols-[auto_1fr_auto] items-center gap-3">
              <span className="text-sm font-semibold text-foreground">#{row.rank}</span>
              <div className="min-w-0">
                <div className="truncate font-medium text-foreground">{row.symbol}</div>
                <div className="truncate text-xs text-muted-foreground">{row.company || row.industry || "-"}</div>
              </div>
              <div className="text-left">
                <div className="text-sm font-semibold text-emerald">{row.score || "-"}</div>
                <div className="text-[10px] text-muted-foreground">امتیاز کیفیت</div>
              </div>
            </div>
            <div className="mt-2 flex items-center justify-between gap-2 border-t border-hairline pt-2">
              <span className="truncate text-xs text-muted-foreground">{row.industry || "-"}</span>
              <button
                type="button"
                onClick={() => toggle(index)}
                aria-expanded={expanded.has(index)}
                className="inline-flex shrink-0 items-center gap-1 rounded-md px-2 py-1 text-xs text-emerald transition hover:bg-emerald-soft"
              >
                جزئیات
                <ChevronDown className={`size-3 transition-transform ${expanded.has(index) ? "rotate-180" : ""}`} />
              </button>
            </div>
            {expanded.has(index) && <div className="mt-2"><RankingDetails row={row} /></div>}
          </article>
        ))}
      </div>
    </div>
  );
}

function createMarkdownComponents(isMonthlySalesQualityRankingTable: boolean, isRtl: boolean): Components {
  const sharedTableCellClass = "px-3 py-2 text-[13px] align-top border-b border-hairline";
  const rankingTableLayoutClass = isMonthlySalesQualityRankingTable
    ? [
        "[&_thead_th:nth-child(1)]:text-center [&_thead_th:nth-child(1)]:whitespace-nowrap",
        "[&_thead_th:nth-child(2)]:text-center [&_thead_th:nth-child(2)]:whitespace-nowrap",
        "[&_thead_th:nth-child(3)]:text-right",
        "[&_thead_th:nth-child(4)]:text-right",
        "[&_th:nth-child(1)]:text-center [&_th:nth-child(1)]:whitespace-nowrap",
        "[&_th:nth-child(2)]:text-center [&_th:nth-child(2)]:whitespace-nowrap",
        "[&_th:nth-child(5)]:text-center [&_th:nth-child(5)]:whitespace-nowrap",
        "[&_th:nth-child(6)]:text-center [&_th:nth-child(6)]:whitespace-nowrap",
        "[&_th:nth-child(8)]:text-center [&_th:nth-child(8)]:whitespace-nowrap",
        "[&_tbody_td:nth-child(1)]:text-center [&_tbody_td:nth-child(1)]:whitespace-nowrap",
        "[&_tbody_td:nth-child(2)]:text-center [&_tbody_td:nth-child(2)]:whitespace-nowrap",
        "[&_tbody_td:nth-child(3)]:text-right",
        "[&_tbody_td:nth-child(4)]:text-right",
        "[&_td:nth-child(1)]:text-center [&_td:nth-child(1)]:whitespace-nowrap",
        "[&_td:nth-child(2)]:text-center [&_td:nth-child(2)]:whitespace-nowrap",
        "[&_td:nth-child(5)]:text-center [&_td:nth-child(5)]:whitespace-nowrap",
        "[&_td:nth-child(6)]:text-center [&_td:nth-child(6)]:whitespace-nowrap",
        "[&_tbody_td:nth-child(7)]:text-right [&_tbody_td:nth-child(7)]:whitespace-normal [&_tbody_td:nth-child(7)]:break-normal [&_tbody_td:nth-child(7)]:leading-6",
        "[&_td:nth-child(7)]:text-right [&_td:nth-child(7)]:whitespace-normal [&_td:nth-child(7)]:break-normal [&_td:nth-child(7)]:leading-6",
        "[&_td:nth-child(8)]:text-center [&_td:nth-child(8)]:whitespace-nowrap",
      ].join(" ")
    : "";
  const rankingTableClass =
    `${isMonthlySalesQualityRankingTable ? "monthly-sales-quality-ranking-table w-max min-w-full" : "w-full"} table-auto text-sm border-collapse ${rankingTableLayoutClass}`.trim();

  return {
    p: ({ children }) => {
      const text = Array.isArray(children) ? children.join("") : String(children ?? "");
      const isDisclaimer = text.trim() === INVESTMENT_DISCLAIMER;
      return (
        <p className={isDisclaimer ? "text-xs text-muted-foreground leading-relaxed" : "leading-relaxed text-pretty [unicode-bidi:plaintext]"}>
          {children}
        </p>
      );
    },
    strong: ({ children }) => <strong className="font-semibold text-foreground">{children}</strong>,
    em: ({ children }) => <em>{children}</em>,
    pre: ({ children }) => (
      <pre className="bg-surface/60 ring-1 ring-hairline rounded-lg overflow-x-auto my-2 text-xs font-mono p-3 text-left" dir="ltr">
        {children}
      </pre>
    ),
    code: ({ className, children }) => {
      if (className) {
        // Block code inside <pre>: keep clean, let pre provide container styling
        return <code className={className}>{children}</code>;
      }
      return (
        <code className="bg-surface/60 ring-1 ring-hairline rounded px-1 py-0.5 text-xs font-mono">
          {children}
        </code>
      );
    },
    ul: ({ children }) => <ul className="list-disc list-inside space-y-1">{children}</ul>,
    ol: ({ children }) => <ol className="list-decimal list-inside space-y-1">{children}</ol>,
    li: ({ children }) => <li className="leading-relaxed [unicode-bidi:plaintext]">{children}</li>,
    table: ({ children }) => (
      <div
        className="overflow-x-auto rounded-lg ring-1 ring-hairline my-2"
        dir={isMonthlySalesQualityRankingTable || isRtl ? "rtl" : "ltr"}
      >
        <table className={rankingTableClass}>
          {isMonthlySalesQualityRankingTable && (
            <colgroup>
              <col style={{ width: "56px" }} />
              <col style={{ width: "84px" }} />
              <col style={{ width: "160px" }} />
              <col style={{ width: "160px" }} />
              <col style={{ width: "104px" }} />
              <col style={{ width: "104px" }} />
              <col style={{ minWidth: "320px" }} />
              <col style={{ width: "80px" }} />
            </colgroup>
          )}
          {children}
        </table>
      </div>
    ),
    thead: ({ children }) => <thead className="bg-white/5">{children}</thead>,
    th: ({ children }) => (
      <th
        className={`${sharedTableCellClass} font-medium text-muted-foreground text-xs ${isMonthlySalesQualityRankingTable ? "whitespace-normal" : isRtl ? "text-right" : "text-left"}`.trim()}
      >
        {children}
      </th>
    ),
    td: ({ children }) => (
      <td
        className={`${sharedTableCellClass} text-foreground/80 ${isMonthlySalesQualityRankingTable ? "whitespace-normal break-normal" : ""}`.trim()}
      >
        {children}
      </td>
    ),
    a: ({ href, children }) => (
      <a
        href={href}
        target="_blank"
        rel="noopener noreferrer"
        className="text-emerald underline underline-offset-2 hover:text-emerald/80"
      >
        {children}
      </a>
    ),
  };
}

interface Props {
  content: string;
  direction?: "rtl" | "ltr" | "auto";
}

export function MarkdownMessage({ content, direction = "auto" }: Props) {
  const isMonthlySalesQualityRankingTable = isMonthlySalesQualityRankingMarkdown(content);
  const rankingTable = isMonthlySalesQualityRankingTable
    ? parseMonthlySalesQualityTable(content)
    : null;
  const isFinancialStatement = isFinancialStatementMarkdown(content);
  const isIndustryRelativeValuation = isIndustryRelativeValuationMarkdown(content);
  const industryTable = isIndustryRelativeValuation ? parseIndustryRelativeValuationTable(content) : null;
  const isRtl = direction === "rtl" || (direction === "auto" && isFinancialStatement);
  const renderedContent = isFinancialStatement
    ? formatFinancialStatementMarkdown(content)
    : content;

  return (
    <div
      className={`text-foreground/90 leading-relaxed text-[15px] space-y-2 ${isMonthlySalesQualityRankingTable ? "max-w-none" : "max-w-[64ch]"} ${isRtl ? "ml-auto text-right [&_p]:text-right [&_li]:text-right" : "text-left [&_p]:text-left [&_li]:text-left"}`}
      dir={isRtl ? "rtl" : direction}
    >
      {industryTable ? (
        <>
          {industryTable.before && (
            <Markdown remarkPlugins={[remarkGfm]} rehypePlugins={[rehypeSanitize]} components={createMarkdownComponents(false, isRtl)}>
              {industryTable.before}
            </Markdown>
          )}
          <IndustryRelativeValuationTable rows={industryTable.rows} groupTitle={industryTable.groupTitle} />
          {industryTable.after && <MarkdownMessage content={industryTable.after} direction={isRtl ? "rtl" : "ltr"} />}
        </>
      ) : rankingTable ? (
        <>
          {rankingTable.before && (
            <Markdown remarkPlugins={[remarkGfm]} rehypePlugins={[rehypeSanitize]} components={createMarkdownComponents(false, isRtl)}>
              {rankingTable.before}
            </Markdown>
          )}
          <MonthlySalesQualityRanking rows={rankingTable.rows} />
          {rankingTable.after && (
            <Markdown remarkPlugins={[remarkGfm]} rehypePlugins={[rehypeSanitize]} components={createMarkdownComponents(false, isRtl)}>
              {rankingTable.after}
            </Markdown>
          )}
        </>
      ) : (
        <Markdown
          remarkPlugins={[remarkGfm]}
          rehypePlugins={[rehypeSanitize]}
          components={createMarkdownComponents(isMonthlySalesQualityRankingTable, isRtl)}
        >
          {renderedContent}
        </Markdown>
      )}
    </div>
  );
}
