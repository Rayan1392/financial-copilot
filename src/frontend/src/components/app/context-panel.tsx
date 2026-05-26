import { MARKET_SNAPSHOT } from "@/lib/mock/data";
import { formatNumber, formatPercent, toPersianDigits } from "@/lib/format/persian";

export function ContextPanel() {
  const m = MARKET_SNAPSHOT;
  return (
    <aside className="w-80 flex-shrink-0 border-r border-hairline bg-surface/50 flex-col hidden lg:flex">
      <div className="p-6 space-y-8 overflow-y-auto scrollbar-thin">
        <div>
          <h3 className="text-xs font-semibold text-muted-foreground uppercase tracking-widest mb-4">وضعیت کلی بازار</h3>
          <div className="space-y-4">
            <div className="flex justify-between items-end">
              <div>
                <div className="text-[11px] text-muted-foreground">شاخص کل</div>
                <div className="text-xl mono mt-1 text-foreground">{formatNumber(m.totalIndex)}</div>
              </div>
              <div className={`text-sm mono ${m.totalIndexChange >= 0 ? "text-emerald" : "text-rose"}`}>
                {formatPercent(m.totalIndexChange)}
              </div>
            </div>
            <div className="flex justify-between items-end">
              <div>
                <div className="text-[11px] text-muted-foreground">شاخص هم‌وزن</div>
                <div className="text-lg mono mt-1 text-foreground">{formatNumber(m.weightedIndex)}</div>
              </div>
              <div className={`text-sm mono ${m.weightedIndexChange >= 0 ? "text-emerald" : "text-rose"}`}>
                {formatPercent(m.weightedIndexChange)}
              </div>
            </div>
            <div className="p-3 rounded-lg bg-background ring-1 ring-hairline">
              <div className="flex justify-between text-[11px] mb-1.5">
                <span className="text-muted-foreground">ورود پول حقیقی</span>
                <span className={`mono ${m.realMoneyFlow >= 0 ? "text-emerald" : "text-gold"}`}>
                  {toPersianDigits(m.realMoneyFlow)} میلیارد
                </span>
              </div>
              <div className="h-1 bg-muted rounded-full overflow-hidden">
                <div className="h-full bg-gold" style={{ width: "33%" }} />
              </div>
            </div>
          </div>
        </div>

        <div>
          <h3 className="text-xs font-semibold text-muted-foreground uppercase tracking-widest mb-4">نمادهای ترند</h3>
          <div className="space-y-1.5">
            {[...m.topGainers, ...m.topLosers].slice(0, 6).map((s) => (
              <div key={s.symbol} className="flex items-center justify-between p-2 rounded-lg hover:bg-white/5 transition">
                <div className="flex items-center gap-3">
                  <div className={`size-2 rounded-full ${s.change >= 0 ? "bg-emerald" : "bg-rose/60"}`} />
                  <span className="text-sm text-foreground">{s.symbol}</span>
                </div>
                <span className={`text-xs mono ${s.change >= 0 ? "text-emerald" : "text-rose"}`}>
                  {formatPercent(s.change)}
                </span>
              </div>
            ))}
          </div>
        </div>

        <div>
          <h3 className="text-xs font-semibold text-muted-foreground uppercase tracking-widest mb-4">صنایع فعال</h3>
          <div className="space-y-2">
            {m.trendingIndustries.map((i) => (
              <div key={i.name} className="flex items-center justify-between text-sm">
                <span className="text-foreground">{i.name}</span>
                <span className={`text-xs mono ${i.change >= 0 ? "text-emerald" : "text-rose"}`}>
                  {formatPercent(i.change)}
                </span>
              </div>
            ))}
          </div>
        </div>

        <div className="p-4 rounded-xl bg-emerald-soft ring-1 ring-emerald/20">
          <h4 className="text-xs font-semibold text-emerald mb-2">بینش هوش مصنوعی</h4>
          <p className="text-xs text-muted-foreground leading-relaxed">{m.insight}</p>
        </div>
      </div>
    </aside>
  );
}
