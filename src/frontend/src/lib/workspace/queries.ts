import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useServerFn } from "@tanstack/react-start";
import { deleteThread, listThreads, type ChatThread } from "@/lib/chat.functions";
import { getMarketSummary, getMyTopUps, getUsage, getWatchlist } from "@/lib/market-view.functions";
import { getFollowedSymbols } from "@/lib/followed-symbols.functions";
import { getOwnProfile } from "@/integrations/financial-copilot/auth";
import { joinWatchlistWithFollowed } from "@/lib/workspace/watchlist-join";
import { OWN_PROFILE_QUERY_KEY } from "@/lib/workspace/query-keys";

/**
 * Shared read hooks for the desktop shell and the mobile workspace (Feature 153 T03). Query keys are the ones
 * desktop already used, so both experiences share one cache and the same invalidations.
 */

/** Desktop keeps its exact earlier options; mobile passes `freshness` to refetch on focus after 60 s. */
export interface ReadOptions {
  freshness?: boolean;
}

function readOptions({ freshness = false }: ReadOptions = {}) {
  return freshness
    ? ({
        retry: false,
        throwOnError: false,
        staleTime: 60_000,
        refetchOnWindowFocus: true,
      } as const)
    : ({ retry: false, throwOnError: false, refetchOnWindowFocus: false } as const);
}

export { OWN_PROFILE_QUERY_KEY };

export function conversationsQueryKey(limit?: number) {
  return limit === undefined ? (["threads"] as const) : (["threads", { limit }] as const);
}

export function useConversations({ limit, ...options }: ReadOptions & { limit?: number } = {}) {
  const fetchThreads = useServerFn(listThreads);
  return useQuery({
    queryKey: conversationsQueryKey(limit),
    queryFn: () => (limit === undefined ? fetchThreads() : fetchThreads({ data: { limit } })),
    ...readOptions(options),
  });
}

export function useDeleteConversation({
  onDeleted,
}: { onDeleted?: (threadId: string) => void } = {}) {
  const qc = useQueryClient();
  const del = useServerFn(deleteThread);
  return useMutation({
    mutationFn: (threadId: string) => del({ data: { threadId } }),
    onSuccess: (_data, threadId) => {
      // Remove the row at once (every cached list, any limit), then confirm with a refetch.
      qc.setQueriesData<ChatThread[]>({ queryKey: ["threads"] }, (rows) =>
        rows?.filter((row) => row.id !== threadId),
      );
      qc.invalidateQueries({ queryKey: ["threads"] });
      qc.removeQueries({ queryKey: ["messages", threadId] });
      onDeleted?.(threadId);
    },
  });
}

export function useMarketSummary(options?: ReadOptions) {
  const fetchSummary = useServerFn(getMarketSummary);
  return useQuery({
    queryKey: ["market-summary"],
    queryFn: () => fetchSummary(),
    ...readOptions(options),
  });
}

export function useWatchlist(options?: ReadOptions) {
  const fetchWatchlist = useServerFn(getWatchlist);
  return useQuery({
    queryKey: ["watchlist"],
    queryFn: () => fetchWatchlist(),
    ...readOptions(options),
  });
}

export function useFollowedSymbols(options?: ReadOptions) {
  const fetchFollowed = useServerFn(getFollowedSymbols);
  return useQuery({
    queryKey: ["followed-symbols"],
    queryFn: () => fetchFollowed(),
    ...readOptions(options),
  });
}

/** Watchlist quotes joined with followed-symbol identity (company name, external ID) by symbol. */
export function useWatchlistWithNames(options?: ReadOptions) {
  const watchlist = useWatchlist(options);
  const followed = useFollowedSymbols(options);
  const join =
    watchlist.data && followed.data
      ? joinWatchlistWithFollowed(watchlist.data.symbols, followed.data.symbols)
      : watchlist.data
        ? joinWatchlistWithFollowed(watchlist.data.symbols, [])
        : null;
  return {
    join,
    asOf: watchlist.data?.asOf,
    followed: followed.data?.symbols ?? [],
    isLoading: watchlist.isLoading,
    isError: watchlist.isError,
    isFetching: watchlist.isFetching || followed.isFetching,
    dataUpdatedAt: watchlist.dataUpdatedAt,
    refetch: () => Promise.all([watchlist.refetch(), followed.refetch()]),
  };
}

export function useUsage(options?: ReadOptions) {
  const fetchUsage = useServerFn(getUsage);
  return useQuery({ queryKey: ["usage"], queryFn: () => fetchUsage(), ...readOptions(options) });
}

/** The signed-in user's recent credit purchases, newest first (bounded by the API at 50). */
export function useMyTopUps(limit = 10, options?: ReadOptions) {
  const fetchTopUps = useServerFn(getMyTopUps);
  return useQuery({
    queryKey: ["top-ups", { limit }],
    queryFn: () => fetchTopUps({ data: { limit } }),
    ...readOptions(options),
  });
}

/** The raw profile. `HeaderAccount` shares the key with a `select`, so both read one cached response. */
export function useOwnProfile() {
  return useQuery({
    queryKey: OWN_PROFILE_QUERY_KEY,
    queryFn: getOwnProfile,
    // The name changes only on a new TahlilApp launch, which issues a new session and clears the cache.
    staleTime: Infinity,
    retry: false,
    throwOnError: false,
    refetchOnWindowFocus: false,
  });
}
