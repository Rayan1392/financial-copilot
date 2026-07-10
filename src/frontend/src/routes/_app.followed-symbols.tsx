import { createFileRoute } from "@tanstack/react-router";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useServerFn } from "@tanstack/react-start";
import { useState } from "react";
import {
  getFollowedSymbols,
  unfollowSymbolByExternalId,
} from "@/lib/followed-symbols.functions";
import { searchSymbolMetadata, type SymbolMetadata } from "@/lib/metadata.functions";
import { FollowSymbolButton } from "@/components/app/follow-symbol-button";

export const Route = createFileRoute("/_app/followed-symbols")({
  component: FollowedSymbolsPage,
});

function FollowedSymbolsPage() {
  const qc = useQueryClient();
  const fetchFollowed = useServerFn(getFollowedSymbols);
  const searchSymbols = useServerFn(searchSymbolMetadata);
  const unfollow = useServerFn(unfollowSymbolByExternalId);
  const [search, setSearch] = useState("");
  const followed = useQuery({
    queryKey: ["followed-symbols"],
    queryFn: () => fetchFollowed(),
    retry: false,
    throwOnError: false,
    refetchOnWindowFocus: false,
  });
  const searchQuery = useQuery({
    queryKey: ["followed-symbol-search", search],
    queryFn: () => searchSymbols({ data: { search, limit: 12 } }),
    enabled: search.trim().length > 0,
    retry: false,
    throwOnError: false,
    refetchOnWindowFocus: false,
  });
  const remove = useMutation({
    mutationFn: (externalCompanyId: string) => unfollow({ data: { externalCompanyId } }),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["followed-symbols"] }),
  });
  const followedIds = new Set(followed.data?.symbols.map((item) => item.externalCompanyId) ?? []);

  return (
    <div className="flex-1 overflow-y-auto p-8">
      <div className="mx-auto max-w-4xl space-y-8">
        <header className="space-y-3">
          <p className="text-xs font-semibold uppercase tracking-[0.3em] text-emerald">
            Followed symbols
          </p>
          <h1 className="text-3xl font-bold text-foreground">Manage followed symbols</h1>
          <p className="max-w-2xl text-sm text-muted-foreground">
            Followed symbols are a personal attention list for future AI feeds. They are not
            portfolio holdings and do not imply position size, cost basis, or exposure.
          </p>
        </header>

        <section className="rounded-2xl border border-hairline bg-surface/60 p-5">
          <label className="text-xs font-medium text-muted-foreground" htmlFor="symbol-search">
            Add a symbol by company search
          </label>
          <input
            id="symbol-search"
            value={search}
            onChange={(event) => setSearch(event.target.value)}
            placeholder="Search symbol or company name"
            className="mt-2 w-full rounded-xl border border-hairline bg-background px-4 py-3 text-sm outline-none focus:border-emerald/50"
          />
          <div className="mt-4 grid gap-2 md:grid-cols-2">
            {searchQuery.data?.map((item) => (
              <SearchResult
                key={item.externalCompanyId}
                item={item}
                followed={followedIds.has(item.externalCompanyId)}
              />
            ))}
          </div>
        </section>

        <section className="rounded-2xl border border-hairline bg-surface/60 p-5">
          <div className="mb-4 flex items-center justify-between">
            <h2 className="text-lg font-semibold">My followed symbols</h2>
            <span className="text-xs text-muted-foreground">
              {followed.data?.symbols.length ?? 0} symbols
            </span>
          </div>
          {followed.isLoading && <p className="text-sm text-muted-foreground">Loading followed symbols...</p>}
          {followed.isError && <p className="text-sm text-rose">Could not load followed symbols.</p>}
          {!followed.isLoading && !followed.isError && followed.data?.symbols.length === 0 && (
            <p className="text-sm text-muted-foreground">No followed symbols yet.</p>
          )}
          <div className="grid gap-3">
            {followed.data?.symbols.map((item) => (
              <div
                key={item.externalCompanyId}
                className="flex items-center justify-between rounded-xl border border-hairline bg-background/50 px-4 py-3"
              >
                <div>
                  <div className="font-semibold text-foreground">{item.symbol}</div>
                  <div className="text-xs text-muted-foreground">{item.companyName}</div>
                  <div className="mt-1 text-[10px] text-muted-foreground">
                    External company id: {item.externalCompanyId}
                  </div>
                </div>
                <button
                  type="button"
                  onClick={() => remove.mutate(item.externalCompanyId)}
                  disabled={remove.isPending}
                  className="rounded-lg border border-rose/30 px-3 py-1.5 text-xs text-rose hover:bg-rose/10 disabled:opacity-50"
                >
                  Unfollow
                </button>
              </div>
            ))}
          </div>
        </section>
      </div>
    </div>
  );
}

function SearchResult({ item, followed }: { item: SymbolMetadata; followed: boolean }) {
  return (
    <div className="flex items-center justify-between rounded-xl border border-hairline bg-background/50 px-4 py-3">
      <div>
        <div className="font-semibold text-foreground">{item.symbolCode}</div>
        <div className="text-xs text-muted-foreground">{item.companyName}</div>
      </div>
      {followed ? (
        <span className="rounded-full border border-emerald/30 px-2 py-1 text-xs text-emerald">
          Followed
        </span>
      ) : (
        <FollowSymbolButton
          externalCompanyId={item.externalCompanyId}
          symbol={item.symbolCode}
          compact
        />
      )}
    </div>
  );
}
