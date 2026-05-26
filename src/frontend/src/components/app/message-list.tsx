import type { ChatBlock, StockCard as StockCardT, TableBlock, ScreenerBlock, PortfolioBlock, ResearchStep } from "@/lib/mock/data";
import { formatNumber, formatPercent, toPersianDigits } from "@/lib/format/persian";
import { TrendingUp, ShieldCheck, Activity, Loader2 } from "lucide-react";

interface MsgRow {
  id: string;
  role: string;
  content: unknown;
  created_at: string;
}

interface Props {
  messages: MsgRow[];
  loading: boolean;
  streaming: boolean;
  deepResearch: boolean;
  onSuggested: (q: string) => void;
}

export function MessageList({ messages, loading, streaming, deepResearch, onSuggested }: Props) {
  if (loading) return <div className="p-8 text-sm text-muted-foreground">در حال بارگذاری...</div>;
  return (
    <div className="p-6 md:p-8 space-y-10 max-w-4xl mx-auto w-full">
      {messages.map((m) => (
        <div key={m.id} className="animate-fade-up">
          {m.role === "user" ? (
            <UserBubble text={(m.content as { text: string }).text} />
          ) : (
            <AssistantBlock block={m.content as ChatBlock} onSuggested={onSuggested} />
          )}
        </div>
      ))}
      {streaming && <StreamingPlaceholder deep={deepResearch} />}
    </div>
  );
}

function UserBubble({ text }: { text: string }) {
  return (
    <div className="flex justify-end">
      <div className="max-w-[56ch] bg-surface ring-1 ring-hairline rounded-2xl px-5 py-3 text-foreground text-pretty text-sm">
        {text}
      </div>
    </div>
  );
}

function AssistantBlock({ block, onSuggested }: { block: ChatBlock; onSuggested: (q: string) => void }) {
  return (
    <div className="flex gap-4">
      <div className="size-7 rounded-lg bg-emerald-soft ring-1 ring-emerald/30 flex-shrink-0 flex items-center justify-center text-[10px] text-emerald font-mono">AI</div>
      <div className="flex-1 space-y-5 min-w-0">
        <p className="text-foreground/90 leading-relaxed text-pretty text-[15px] max-w-[64ch]">
          {block.message}
        </p>

        {block.research && <DeepResearch steps={block.research} />}

        {block.cards && block.cards.map((c, i) => <StockCard key={i} card={c} />)}

        {block.tables && block.tables.map((t, i) => <DataTable key={i} table={t} />)}

        {block.screener && <Screener data={block.screener} />}

        {block.portfolio && <Portfolio data={block.portfolio} />}

        <div className="flex items-center gap-3 text-[10px] text-muted-foreground">
          <span className="flex items-center gap-1">
            <ShieldCheck className="size-3 text-emerald" />
            اطمینان {toPersianDigits(Math.round(block.confidence * 100))}٪
          </span>
          <span>•</span>
          <span>{toPersianDigits(block.creditsUsed)} اعتبار مصرف شد</span>
        </div>

        {block.suggestedQuestions?.length > 0 && (
          <div className="flex flex-wrap gap-2 pt-1">
            {block.suggestedQuestions.map((q) => (
              <button
                key={q}
                onClick={() => onSuggested(q)}
                className="px-3 py-1.5 rounded-full ring-1 ring-hairline bg-white/5 text-[11px] text-muted-foreground hover:text-foreground hover:bg-white/10 transition"
              >
                {q}
              </button>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

function Sparkline({ data }: { data: number[] }) {
  const max = Math.max(...data, 1);
  return (
    <div className="flex items-end gap-0.5 h-8">
      {data.map((v, i) => {
        const intensity = i === data.length - 1 ? "bg-emerald" : i >= data.length - 3 ? "bg-emerald/70" : "bg-emerald/40";
        return <div key={i} className={`w-1 ${intensity} rounded-sm`} style={{ height: `${(v / max) * 100}%` }} />;
      })}
    </div>
  );
}

function StockCard({ card: c }: { card: StockCardT }) {
  const valColor = c.valuation === "ارزنده" ? "text-emerald" : c.valuation === "گران" ? "text-rose" : "text-gold";
  return (
    <div className="rounded-2xl ring-1 ring-hairline bg-surface p-5 space-y-4">
      <div className="flex justify-between items-start">
        <div>
          <div className="text-base font-bold text-foreground">{c.symbol}</div>
          <div className="text-[11px] text-muted-foreground mt-0.5">{c.fullName}</div>
        </div>
        <div className="text-left">
          <div className="text-base mono text-foreground">{formatNumber(c.price)} <span className="text-[10px] text-muted-foreground font-sans">ریال</span></div>
          <div className={`text-xs mono ${c.changePercent >= 0 ? "text-emerald" : "text-rose"}`}>{formatPercent(c.changePercent)}</div>
        </div>
      </div>

      <div className="grid grid-cols-3 gap-3">
        <div className="p-3 rounded-xl bg-background/40 ring-1 ring-hairline flex flex-col gap-1.5">
          <span className="text-[10px] text-muted-foreground uppercase tracking-wider font-semibold">ارزش‌گذاری</span>
          <span className={`text-base font-medium ${valColor}`}>{c.valuation}</span>
          <div className="flex gap-1 mt-auto">
            <div className={`h-1 flex-1 ${c.valuation === "ارزنده" ? "bg-emerald" : "bg-muted"}`} />
            <div className={`h-1 flex-1 ${c.valuation !== "گران" ? "bg-emerald" : "bg-muted"}`} />
            <div className={`h-1 flex-1 ${c.confidence > 0.8 ? "bg-emerald" : "bg-muted"}`} />
          </div>
        </div>
        <div className="p-3 rounded-xl bg-background/40 ring-1 ring-hairline">
          <span className="text-[10px] text-muted-foreground uppercase tracking-wider font-semibold">نسبت P/E</span>
          <div className="text-lg mono mt-1 text-foreground">{toPersianDigits(c.forwardPE)}</div>
          <span className="text-[10px] text-muted-foreground">میانگین گروه: {toPersianDigits(c.industryPE)}</span>
        </div>
        <div className="p-3 rounded-xl bg-background/40 ring-1 ring-hairline">
          <span className="text-[10px] text-muted-foreground uppercase tracking-wider font-semibold">اطمینان</span>
          <div className="text-lg mono mt-1 text-foreground">{toPersianDigits(Math.round(c.confidence * 100))}٪</div>
          <span className="text-[10px] text-emerald">تایید شده</span>
        </div>
      </div>

      <div className="p-3 rounded-xl ring-1 ring-hairline bg-background/40 flex items-center justify-between">
        <div>
          <span className="text-[11px] text-muted-foreground">نمودار قیمت (۹۰ روزه)</span>
          <div className="text-sm mono mt-0.5 text-foreground flex items-center gap-1">
            <TrendingUp className="size-3 text-emerald" />
            {formatNumber(c.price)}
          </div>
        </div>
        <Sparkline data={c.sparkline} />
      </div>
    </div>
  );
}

function DataTable({ table }: { table: TableBlock }) {
  return (
    <div className="rounded-2xl overflow-hidden ring-1 ring-hairline bg-surface/40">
      {table.title && <div className="px-4 py-3 text-xs font-semibold text-muted-foreground border-b border-hairline">{table.title}</div>}
      <table className="w-full text-right text-sm">
        <thead className="bg-white/5">
          <tr>
            {table.columns.map((c, i) => (
              <th key={i} className={`px-4 py-2.5 font-medium text-muted-foreground text-xs ${table.highlightCol === i ? "text-emerald" : ""}`}>{c}</th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-hairline">
          {table.rows.map((r, i) => (
            <tr key={i}>
              {r.map((cell, j) => (
                <td key={j} className={`px-4 py-2.5 ${j === 0 ? "text-foreground/90" : "mono"} ${table.highlightCol === j ? "text-emerald font-medium" : "text-foreground/80"}`}>
                  {cell}
                </td>
              ))}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function Screener({ data }: { data: ScreenerBlock }) {
  return (
    <div className="space-y-3">
      <div className="flex flex-wrap gap-2">
        {data.detectedFilters.map((f) => (
          <span key={f.label} className="text-[11px] px-2.5 py-1 rounded-full bg-emerald-soft ring-1 ring-emerald/20 text-emerald">
            {f.label}: {f.value}
          </span>
        ))}
      </div>
      <DataTable
        table={{
          title: data.title,
          columns: ["نماد", "قیمت", "تغییر", "P/E", "ارزش بازار"],
          rows: data.results.map((r) => [
            r.symbol,
            formatNumber(r.price),
            formatPercent(r.changePercent),
            toPersianDigits(r.pe),
            r.marketCap,
          ]),
        }}
      />
    </div>
  );
}

function Portfolio({ data }: { data: PortfolioBlock }) {
  return (
    <div className="rounded-2xl ring-1 ring-hairline bg-surface p-5 space-y-5">
      <div className="flex justify-between items-start">
        <div>
          <div className="text-xs text-muted-foreground">امتیاز پرتفو</div>
          <div className="text-3xl font-bold mono text-emerald mt-1">{toPersianDigits(data.score)}</div>
          <div className="text-[11px] text-muted-foreground mt-1">{data.diversificationLabel} • ریسک تمرکز: {data.concentrationRisk}</div>
        </div>
        <Activity className="size-5 text-emerald" />
      </div>

      <div>
        <div className="text-[10px] uppercase tracking-wider text-muted-foreground font-semibold mb-3">تخصیص بخشی</div>
        <div className="flex h-2 rounded-full overflow-hidden ring-1 ring-hairline">
          {data.allocations.map((a, i) => (
            <div key={a.sector} className={i % 2 ? "bg-emerald/60" : "bg-emerald"} style={{ width: `${a.percent}%` }} />
          ))}
        </div>
        <div className="grid grid-cols-2 gap-2 mt-3">
          {data.allocations.map((a) => (
            <div key={a.sector} className="flex justify-between text-xs">
              <span className="text-foreground/80">{a.sector}</span>
              <span className="mono text-muted-foreground">{toPersianDigits(a.percent)}٪</span>
            </div>
          ))}
        </div>
      </div>

      <div className="border-t border-hairline pt-4 space-y-2">
        <div className="text-[10px] uppercase tracking-wider text-muted-foreground font-semibold mb-2">پیشنهادهای هوش مصنوعی</div>
        {data.recommendations.map((r, i) => (
          <div key={i} className="text-xs text-foreground/80 leading-relaxed flex gap-2">
            <span className="text-emerald mt-0.5">◆</span><span>{r}</span>
          </div>
        ))}
      </div>
    </div>
  );
}

function DeepResearch({ steps }: { steps: ResearchStep[] }) {
  return (
    <div className="rounded-2xl ring-1 ring-emerald/20 bg-emerald-soft/30 p-4 space-y-3">
      <div className="flex items-center gap-2 text-xs font-semibold text-emerald">
        <Activity className="size-3.5" />
        فرایند جستجوی عمیق
      </div>
      <div className="space-y-2">
        {steps.map((s, i) => (
          <div key={i} className="flex items-start gap-3 text-xs">
            <div className="size-4 rounded-full bg-emerald/20 ring-1 ring-emerald/30 flex items-center justify-center mt-0.5">
              <div className="size-1 rounded-full bg-emerald" />
            </div>
            <div className="flex-1">
              <div className="text-foreground/90 font-medium">{s.label}</div>
              <div className="text-muted-foreground text-[11px] mt-0.5">{s.detail}</div>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

const DEEP_STEPS = [
  "بررسی صورت‌های مالی ۴ فصل اخیر...",
  "تحلیل گزارش‌های کدال...",
  "مقایسه با همگروهان صنعت...",
  "تحلیل جریان نقدینگی حقیقی...",
];

function StreamingPlaceholder({ deep }: { deep: boolean }) {
  return (
    <div className="flex gap-4 animate-fade-up">
      <div className="size-7 rounded-lg bg-emerald-soft ring-1 ring-emerald/30 flex-shrink-0 flex items-center justify-center">
        <Loader2 className="size-3.5 text-emerald animate-spin" />
      </div>
      <div className="flex-1 space-y-2 pt-1">
        {deep ? (
          <div className="space-y-1.5">
            {DEEP_STEPS.map((s, i) => (
              <div key={i} className="text-xs text-muted-foreground flex items-center gap-2">
                <span className="size-1 rounded-full bg-emerald animate-pulse" style={{ animationDelay: `${i * 0.2}s` }} />
                {s}
              </div>
            ))}
          </div>
        ) : (
          <div className="text-sm text-muted-foreground flex items-center gap-1">
            در حال تحلیل
            <span className="inline-block size-1.5 rounded-full bg-emerald cursor-pulse mr-1" />
          </div>
        )}
      </div>
    </div>
  );
}
