import { Link, useNavigate, useParams } from "@tanstack/react-router";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { useServerFn } from "@tanstack/react-start";
import { listThreads, createThread, deleteThread } from "@/lib/chat.functions";
import { getSubscription, getWatchlist } from "@/lib/prototype-sidebar.functions";
import { logout } from "@/integrations/financial-copilot/auth";
import { formatPercent, toPersianDigits } from "@/lib/format/persian";
import { STOCK_DB } from "@/lib/mock/data";
import { Plus, Trash2, LogOut } from "lucide-react";

export function ConversationSidebar() {
  const navigate = useNavigate();
  const qc = useQueryClient();
  const params = useParams({ strict: false });
  const activeId = (params as { threadId?: string }).threadId;

  const fetchThreads = useServerFn(listThreads);
  const fetchSub = useServerFn(getSubscription);
  const fetchWatch = useServerFn(getWatchlist);
  const create = useServerFn(createThread);
  const del = useServerFn(deleteThread);

  const qOpts = { retry: false, throwOnError: false, refetchOnWindowFocus: false } as const;
  const { data: threads = [] } = useQuery({
    queryKey: ["threads"],
    queryFn: () => fetchThreads(),
    ...qOpts,
  });
  const { data: sub } = useQuery({
    queryKey: ["subscription"],
    queryFn: () => fetchSub(),
    ...qOpts,
  });
  const { data: watchlist = [] } = useQuery({
    queryKey: ["watchlist"],
    queryFn: () => fetchWatch(),
    ...qOpts,
  });

  const newChat = useMutation({
    mutationFn: () => create(),
    onSuccess: (t) => {
      qc.invalidateQueries({ queryKey: ["threads"] });
      navigate({ to: "/c/$threadId", params: { threadId: t.id } });
    },
  });

  const removeChat = useMutation({
    mutationFn: (id: string) => del({ data: { threadId: id } }),
    onSuccess: (_d, id) => {
      qc.invalidateQueries({ queryKey: ["threads"] });
      if (activeId === id) navigate({ to: "/chat" });
    },
  });

  async function signOut() {
    await logout();
    navigate({ to: "/auth" });
  }

  const pct = sub ? Math.round((sub.ai_credits_remaining / sub.ai_credits_total) * 100) : 0;

  return (
    <aside className="w-72 flex-shrink-0 border-l border-hairline bg-surface/50 flex flex-col hidden md:flex">
      <div className="p-5">
        <Link to="/chat" className="flex items-center gap-3 mb-7">
          <div className="size-8 rounded-lg bg-emerald-soft ring-1 ring-emerald/30 flex items-center justify-center">
            <div className="size-3 bg-emerald rounded-full" />
          </div>
          <h1 className="text-[15px] font-bold tracking-tight text-foreground leading-tight">
            دستیار هوشمند تحلیل بازار
          </h1>
        </Link>

        <button
          onClick={() => newChat.mutate()}
          disabled={newChat.isPending}
          className="w-full flex items-center justify-between py-2.5 px-3 rounded-lg bg-emerald text-primary-foreground text-sm font-medium hover:brightness-110 transition disabled:opacity-50 mb-6"
        >
          <span>گفتگوی جدید</span>
          <Plus className="size-4" />
        </button>

        <nav className="space-y-1 mb-7">
          <p className="text-[10px] font-semibold text-muted-foreground uppercase tracking-widest px-2 mb-3">
            گفتگوهای اخیر
          </p>
          {threads.length === 0 && (
            <p className="text-xs text-muted-foreground px-3 py-2">گفتگویی نیست.</p>
          )}
          {threads.map((t) => (
            <div key={t.id} className="group relative">
              <Link
                to="/c/$threadId"
                params={{ threadId: t.id }}
                className={`flex items-center gap-3 px-3 py-2 rounded-md transition ${
                  activeId === t.id
                    ? "bg-white/5 text-foreground"
                    : "hover:bg-white/5 text-muted-foreground hover:text-foreground"
                }`}
              >
                <div
                  className={`size-1.5 rounded-full ${activeId === t.id ? "bg-emerald" : "bg-muted-foreground/40"}`}
                />
                <span className="text-sm truncate flex-1">{t.title}</span>
              </Link>
              <button
                onClick={(e) => {
                  e.preventDefault();
                  removeChat.mutate(t.id);
                }}
                className="absolute left-1.5 top-1/2 -translate-y-1/2 opacity-0 group-hover:opacity-100 p-1 text-muted-foreground hover:text-destructive transition"
                aria-label="حذف"
              >
                <Trash2 className="size-3.5" />
              </button>
            </div>
          ))}
        </nav>

        <div className="space-y-3">
          <p className="text-[10px] font-semibold text-muted-foreground uppercase tracking-widest px-2">
            دیده‌بان من
          </p>
          <div className="grid grid-cols-2 gap-2">
            {watchlist.slice(0, 4).map((sym) => {
              const s = STOCK_DB[sym];
              if (!s)
                return (
                  <div key={sym} className="p-2 rounded border border-hairline bg-background/50">
                    <div className="text-[10px] text-muted-foreground">{sym}</div>
                    <div className="text-xs mono text-muted-foreground">—</div>
                  </div>
                );
              return (
                <div key={sym} className="p-2 rounded border border-hairline bg-background/50">
                  <div className="text-[10px] text-muted-foreground">{sym}</div>
                  <div
                    className={`text-xs mono ${s.changePercent >= 0 ? "text-emerald" : "text-rose"}`}
                  >
                    {formatPercent(s.changePercent)}
                  </div>
                </div>
              );
            })}
          </div>
        </div>
      </div>

      <div className="mt-auto p-5 border-t border-hairline space-y-4">
        {sub && (
          <div className="p-3 rounded-xl bg-background/50 ring-1 ring-hairline">
            <div className="flex items-center justify-between mb-2">
              <span className="text-[11px] text-muted-foreground">اعتبار هوش مصنوعی</span>
              <span className="text-[11px] mono text-foreground">
                {toPersianDigits(sub.ai_credits_remaining)} /{" "}
                {toPersianDigits(sub.ai_credits_total)}
              </span>
            </div>
            <div className="h-1 w-full bg-muted rounded-full overflow-hidden">
              <div className="h-full bg-emerald transition-all" style={{ width: `${pct}%` }} />
            </div>
          </div>
        )}
        <div className="flex items-center justify-between px-1">
          <div className="flex items-center gap-3">
            <div className="size-8 rounded-full bg-muted flex items-center justify-center text-[10px] text-muted-foreground font-semibold">
              کاربر
            </div>
            <div className="flex flex-col">
              <span className="text-xs font-medium text-foreground">حساب حرفه‌ای</span>
              <span className="text-[10px] text-gold">
                پلن {sub?.plan === "pro" ? "پرو" : (sub?.plan ?? "—")}
              </span>
            </div>
          </div>
          <button
            onClick={signOut}
            className="p-1.5 text-muted-foreground hover:text-foreground transition"
            aria-label="خروج"
          >
            <LogOut className="size-4" />
          </button>
        </div>
      </div>
    </aside>
  );
}
