import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";
import rehypeSanitize from "rehype-sanitize";
import type { Components } from "react-markdown";

const MONTHLY_SALES_QUALITY_RANKING_HEADER =
  "| رتبه | نماد | شرکت | صنعت | امتیاز کیفیت | برچسب | دلیل اصلی | اطمینان |";

function isMonthlySalesQualityRankingMarkdown(content: string): boolean {
  return content.includes(MONTHLY_SALES_QUALITY_RANKING_HEADER);
}

function createMarkdownComponents(isMonthlySalesQualityRankingTable: boolean): Components {
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
    p: ({ children }) => <p className="leading-relaxed text-pretty">{children}</p>,
    strong: ({ children }) => <strong className="font-semibold text-foreground">{children}</strong>,
    em: ({ children }) => <em>{children}</em>,
    pre: ({ children }) => (
      <pre className="bg-surface/60 ring-1 ring-hairline rounded-lg overflow-x-auto my-2 text-xs font-mono p-3">
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
    li: ({ children }) => <li className="leading-relaxed">{children}</li>,
    table: ({ children }) => (
      <div
        className="overflow-x-auto rounded-lg ring-1 ring-hairline my-2"
        dir={isMonthlySalesQualityRankingTable ? "rtl" : undefined}
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
        className={`${sharedTableCellClass} font-medium text-muted-foreground text-xs ${isMonthlySalesQualityRankingTable ? "whitespace-normal" : "text-left"}`.trim()}
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
}

export function MarkdownMessage({ content }: Props) {
  const isMonthlySalesQualityRankingTable = isMonthlySalesQualityRankingMarkdown(content);

  return (
    <div
      className={`text-foreground/90 leading-relaxed text-[15px] space-y-2 ${isMonthlySalesQualityRankingTable ? "max-w-none" : "max-w-[64ch]"}`}
      dir="auto"
    >
      <Markdown
        remarkPlugins={[remarkGfm]}
        rehypePlugins={[rehypeSanitize]}
        components={createMarkdownComponents(isMonthlySalesQualityRankingTable)}
      >
        {content}
      </Markdown>
    </div>
  );
}
