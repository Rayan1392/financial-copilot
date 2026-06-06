import Markdown from "react-markdown";
import remarkGfm from "remark-gfm";
import rehypeSanitize from "rehype-sanitize";
import type { Components } from "react-markdown";

const markdownComponents: Components = {
  p: ({ children }) => (
    <p className="leading-relaxed text-pretty">{children}</p>
  ),
  strong: ({ children }) => (
    <strong className="font-semibold text-foreground">{children}</strong>
  ),
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
  ul: ({ children }) => (
    <ul className="list-disc list-inside space-y-1">{children}</ul>
  ),
  ol: ({ children }) => (
    <ol className="list-decimal list-inside space-y-1">{children}</ol>
  ),
  li: ({ children }) => <li className="leading-relaxed">{children}</li>,
  table: ({ children }) => (
    <div className="overflow-x-auto rounded-lg ring-1 ring-hairline my-2">
      <table className="w-full text-sm border-collapse">{children}</table>
    </div>
  ),
  thead: ({ children }) => <thead className="bg-white/5">{children}</thead>,
  th: ({ children }) => (
    <th className="px-3 py-2 font-medium text-muted-foreground text-xs text-left border-b border-hairline">
      {children}
    </th>
  ),
  td: ({ children }) => (
    <td className="px-3 py-2 text-foreground/80 border-b border-hairline text-[13px]">
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

interface Props {
  content: string;
}

export function MarkdownMessage({ content }: Props) {
  return (
    <div
      className="text-foreground/90 leading-relaxed text-[15px] max-w-[64ch] space-y-2"
      dir="auto"
    >
      <Markdown
        remarkPlugins={[remarkGfm]}
        rehypePlugins={[rehypeSanitize]}
        components={markdownComponents}
      >
        {content}
      </Markdown>
    </div>
  );
}
