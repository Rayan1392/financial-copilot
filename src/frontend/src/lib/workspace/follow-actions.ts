import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useServerFn } from "@tanstack/react-start";
import { toast } from "sonner";
import {
  followSymbolByExternalId,
  unfollowSymbolByExternalId,
} from "@/lib/followed-symbols.functions";
import { resolveExternalCompanyId } from "@/lib/market-view.functions";

const UNDO_WINDOW_MS = 5_000;

/** Follow / unfollow with the watchlist invalidations desktop already uses (`["watchlist"]`, `["followed-symbols"]`). */
export function useFollowActions() {
  const qc = useQueryClient();
  const follow = useServerFn(followSymbolByExternalId);
  const unfollow = useServerFn(unfollowSymbolByExternalId);
  const resolve = useServerFn(resolveExternalCompanyId);

  const invalidate = () => {
    void qc.invalidateQueries({ queryKey: ["watchlist"] });
    void qc.invalidateQueries({ queryKey: ["followed-symbols"] });
  };

  const followMutation = useMutation({
    mutationFn: async ({ externalCompanyId }: { externalCompanyId: string; symbol: string }) =>
      follow({ data: { externalCompanyId } }),
    onSuccess: (_data, { symbol }) => {
      invalidate();
      toast.success(`${symbol} به دیده‌بان اضافه شد`);
    },
    // No error handling here: the add-symbol sheet shows the API message inline (for example the plan limit).
  });

  const unfollowMutation = useMutation({
    mutationFn: async ({ externalCompanyId }: { externalCompanyId: string; symbol: string }) =>
      unfollow({ data: { externalCompanyId } }),
    onSuccess: (_data, variables) => {
      invalidate();
      toast(`${variables.symbol} از دیده‌بان حذف شد`, {
        duration: UNDO_WINDOW_MS,
        action: {
          label: "بازگردانی",
          onClick: () =>
            followMutation.mutate(variables, {
              onError: (error: Error) => toast.error(error.message || "بازگردانی نماد انجام نشد."),
            }),
        },
      });
    },
    onError: () => {
      toast.error("حذف نماد انجام نشد.");
    },
  });

  /** For market movers: resolve the ticker to its external ID, then follow it. */
  const followBySymbolMutation = useMutation({
    mutationFn: async ({ symbol }: { symbol: string }) => {
      const externalCompanyId = await resolve({ data: { symbol } });
      if (!externalCompanyId) throw new Error("این نماد برای افزودن به دیده‌بان در دسترس نیست.");
      await follow({ data: { externalCompanyId } });
      return externalCompanyId;
    },
    onSuccess: (_id, { symbol }) => {
      invalidate();
      toast.success(`${symbol} به دیده‌بان اضافه شد`);
    },
    onError: (error: Error) => {
      toast.error(error.message || "افزودن نماد انجام نشد.");
    },
  });

  return {
    follow: followMutation,
    unfollow: unfollowMutation,
    followBySymbol: followBySymbolMutation,
  };
}
