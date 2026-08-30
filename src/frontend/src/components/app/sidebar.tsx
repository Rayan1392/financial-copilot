import { Link, useNavigate, useParams } from "@tanstack/react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useServerFn } from "@tanstack/react-start";
import { useEffect, useState } from "react";
import { Plus, Trash2, LogOut, Settings, Send } from "lucide-react";
import { listThreads, createThread, deleteThread } from "@/lib/chat.functions";
import { getUsage } from "@/lib/market-view.functions";
import {
  getStoredAuthenticatedUser,
  logout,
  subscribeToAuthChanges,
} from "@/integrations/financial-copilot/auth";
import { canAccessAdmin } from "@/integrations/financial-copilot/admin-permissions";
import { toPersianDigits } from "@/lib/format/persian";

export function ConversationSidebar() {
  const navigate = useNavigate();
  const qc = useQueryClient();
  const params = useParams({ strict: false });
  const activeId = (params as { threadId?: string }).threadId;
  const fetchThreads = useServerFn(listThreads);
  const fetchUsage = useServerFn(getUsage);
  const create = useServerFn(createThread);
  const del = useServerFn(deleteThread);
  const qOpts = { retry: false, throwOnError: false, refetchOnWindowFocus: false } as const;
  const { data: threads = [] } = useQuery({
    queryKey: ["threads"],
    queryFn: () => fetchThreads(),
    ...qOpts,
  });
  const {
    data: usage,
    isLoading: usageLoading,
    isError: usageError,
  } = useQuery({
    queryKey: ["usage"],
    queryFn: () => fetchUsage(),
    ...qOpts,
  });
  const newChat = useMutation({
    mutationFn: () => create(),
    onSuccess: (thread) => {
      qc.invalidateQueries({ queryKey: ["threads"] });
      navigate({ to: "/c/$threadId", params: { threadId: thread.id } });
    },
  });
  const removeChat = useMutation({
    mutationFn: (id: string) => del({ data: { threadId: id } }),
    onSuccess: (_data, id) => {
      qc.invalidateQueries({ queryKey: ["threads"] });
      if (activeId === id) navigate({ to: "/chat" });
    },
  });
  const percentage = usage
    ? Math.round(
        (usage.availableSpendingCapacity /
          Math.max(usage.availableSpendingCapacity + usage.reservedCredits, 1)) *
          100,
      )
    : 0;
  const [showAdmin, setShowAdmin] = useState(false);
  useEffect(() => {
    const updateAccess = () => {
      const user = getStoredAuthenticatedUser();
      setShowAdmin(canAccessAdmin(user));
    };
    updateAccess();
    return subscribeToAuthChanges(updateAccess);
  }, []);

  async function signOut() {
    await logout();
    navigate({ to: "/auth" });
  }

  return (
    <aside className="hidden h-screen w-72 flex-shrink-0 flex-col overflow-hidden border-l border-hairline bg-surface/50 md:flex">
      <div className="flex min-h-0 flex-1 flex-col p-5">
        <Link to="/chat" className="flex items-center gap-3 mb-7">
          <div className="size-8 rounded-lg bg-emerald-soft ring-1 ring-emerald/30 flex items-center justify-center">
            <div className="size-3 bg-emerald rounded-full" />
          </div>
          <h1 className="text-[15px] font-bold tracking-tight text-foreground leading-tight">
            ساپیو - دستیار هوشمند بازار
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
        <div className="flex min-h-0 flex-1 flex-col">
          <p className="text-[10px] font-semibold text-muted-foreground uppercase tracking-widest px-2 mb-3">
            گفتگوهای اخیر
          </p>
          <nav className="min-h-0 flex-1 space-y-1 overflow-y-auto pr-1">
            {threads.length === 0 && (
              <p className="text-xs text-muted-foreground px-3 py-2">گفتگویی نیست.</p>
            )}
            {threads.map((thread) => (
              <div key={thread.id} className="group relative">
                <Link
                  to="/c/$threadId"
                  params={{ threadId: thread.id }}
                  className={`flex items-center gap-3 px-3 py-2 rounded-md transition ${
                    activeId === thread.id
                      ? "bg-white/5 text-foreground"
                      : "hover:bg-white/5 text-muted-foreground hover:text-foreground"
                  }`}
                >
                  <div
                    className={`size-1.5 rounded-full ${activeId === thread.id ? "bg-emerald" : "bg-muted-foreground/40"}`}
                  />
                  <span className="text-sm truncate flex-1">{thread.title}</span>
                </Link>
                <button
                  onClick={(event) => {
                    event.preventDefault();
                    removeChat.mutate(thread.id);
                  }}
                  className="absolute left-1.5 top-1/2 -translate-y-1/2 p-1 text-muted-foreground opacity-0 transition group-hover:opacity-100 hover:text-destructive"
                  aria-label="حذف"
                >
                  <Trash2 className="size-3.5" />
                </button>
              </div>
            ))}
          </nav>
        </div>
      </div>
      <div className="mt-auto shrink-0 border-t border-hairline p-5 space-y-4">
        {usageLoading && <p className="text-xs text-muted-foreground">در حال بارگذاری اعتبار...</p>}
        {usageError && <p className="text-xs text-rose">اعتبار در دسترس نیست.</p>}
        {usage && (
          <div className="p-3 rounded-xl bg-background/50 ring-1 ring-hairline">
            <div className="flex items-center justify-between mb-2">
              <span className="text-[11px] text-muted-foreground">اعتبار هوش مصنوعی</span>
              <span className="text-[11px] text-foreground">
                {toPersianDigits(usage.availableSpendingCapacity)}
              </span>
            </div>
            <div className="h-1 w-full bg-muted rounded-full overflow-hidden">
              <div
                className="h-full bg-emerald transition-all"
                style={{ width: `${percentage}%` }}
              />
            </div>
          </div>
        )}
        <div className="flex items-center justify-between px-1">
          <span className="text-xs font-medium text-foreground">حساب کاربری</span>
          <Link
            to="/telegram-link"
            className="p-1.5 text-muted-foreground hover:text-foreground transition"
            aria-label="اتصال تلگرام"
          >
            <Send className="size-4" />
          </Link>
          {showAdmin && (
            <Link
              to="/admin"
              className="p-1.5 text-muted-foreground hover:text-foreground transition"
              aria-label="پنل مدیریت"
            >
              <Settings className="size-4" />
            </Link>
          )}
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
