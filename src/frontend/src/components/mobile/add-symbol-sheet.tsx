import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useServerFn } from "@tanstack/react-start";
import { Search } from "lucide-react";
import { BottomSheet } from "@/components/mobile/bottom-sheet";
import { searchSymbolMetadata } from "@/lib/metadata.functions";
import { useFollowActions } from "@/lib/workspace/follow-actions";

const DEBOUNCE_MS = 300;

function useDebounced(value: string, delay: number): string {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(value), delay);
    return () => window.clearTimeout(timer);
  }, [value, delay]);
  return debounced;
}

/** Add-symbol sheet (design §6.2): debounced metadata search, follow on tap, API validation shown inline (AC-17). */
export function AddSymbolSheet({
  open,
  onOpenChange,
  followedIds,
}: {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  followedIds: ReadonlySet<string>;
}) {
  const [query, setQuery] = useState("");
  const [inlineError, setInlineError] = useState<string | null>(null);
  const search = useServerFn(searchSymbolMetadata);
  const { follow } = useFollowActions();
  const term = useDebounced(query.trim(), DEBOUNCE_MS);
  const results = useQuery({
    queryKey: ["symbol-metadata-search", term],
    queryFn: () => search({ data: { search: term, limit: 12 } }),
    enabled: open && term.length >= 1,
    retry: false,
    staleTime: 60_000,
  });

  useEffect(() => {
    if (!open) {
      setQuery("");
      setInlineError(null);
    }
  }, [open]);

  return (
    <BottomSheet open={open} onOpenChange={onOpenChange} title="افزودن نماد" fullHeight>
      <div className="px-2 pb-2 pt-1">
        <div className="flex items-center gap-2 rounded-xl bg-surface px-3 ring-1 ring-hairline focus-within:ring-2 focus-within:ring-emerald/40">
          <Search className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
          <label htmlFor="add-symbol-search" className="sr-only">
            جستجوی نماد یا نام شرکت
          </label>
          {/* Autofocus is acceptable here: the user opened this sheet to type. */}
          <input
            id="add-symbol-search"
            type="search"
            autoFocus
            value={query}
            onChange={(event) => {
              setQuery(event.target.value);
              setInlineError(null);
            }}
            placeholder="نماد یا نام شرکت…"
            className="min-h-11 min-w-0 flex-1 bg-transparent text-base focus:outline-none"
          />
        </div>
        {inlineError && (
          <p
            role="alert"
            className="mt-2 rounded-xl bg-rose/10 px-3 py-2 text-sm leading-6 mobile-danger-text"
          >
            {inlineError}
          </p>
        )}
      </div>
      {term.length === 0 ? null : results.isLoading ? (
        <p role="status" className="px-4 py-6 text-center text-sm text-muted-foreground">
          در حال جستجو…
        </p>
      ) : results.isError ? (
        <div role="alert" className="px-4 py-6 text-center text-sm">
          <p>جستجو ناموفق بود</p>
          <button
            type="button"
            onClick={() => void results.refetch()}
            className="mt-2 min-h-11 rounded-xl bg-surface px-4 font-medium ring-1 ring-hairline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-emerald/40"
          >
            تلاش مجدد
          </button>
        </div>
      ) : (results.data ?? []).length === 0 ? (
        <p role="status" className="px-4 py-6 text-center text-sm text-muted-foreground">
          نمادی یافت نشد
        </p>
      ) : (
        <ul>
          {(results.data ?? []).map((item) => {
            const alreadyFollowed = followedIds.has(item.externalCompanyId);
            const busy =
              follow.isPending && follow.variables?.externalCompanyId === item.externalCompanyId;
            return (
              <li key={item.externalCompanyId}>
                <button
                  type="button"
                  disabled={alreadyFollowed || busy}
                  onClick={() =>
                    follow.mutate(
                      { externalCompanyId: item.externalCompanyId, symbol: item.symbolCode },
                      {
                        onError: (error: Error) =>
                          setInlineError(error.message || "افزودن نماد انجام نشد."),
                      },
                    )
                  }
                  className="flex min-h-[52px] w-full items-center gap-3 rounded-xl px-3 text-right active:bg-surface-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-emerald/40 disabled:opacity-70"
                >
                  <span className="min-w-0 flex-1">
                    <bdi className="font-semibold text-foreground">{item.symbolCode}</bdi>
                    <span className="block truncate text-xs text-muted-foreground">
                      {item.companyName}
                    </span>
                  </span>
                  {alreadyFollowed ? (
                    <span className="text-xs text-emerald">دنبال‌شده</span>
                  ) : busy ? (
                    <span className="text-xs text-muted-foreground">…</span>
                  ) : null}
                </button>
              </li>
            );
          })}
        </ul>
      )}
    </BottomSheet>
  );
}
