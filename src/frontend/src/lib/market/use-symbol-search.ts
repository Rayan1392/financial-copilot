import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useServerFn } from "@tanstack/react-start";
import { searchSymbolMetadata } from "@/lib/metadata.functions";

export const SYMBOL_SEARCH_DEBOUNCE_MS = 300;

function useDebounced(value: string, delay: number): string {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(value), delay);
    return () => window.clearTimeout(timer);
  }, [value, delay]);
  return debounced;
}

/**
 * Type-ahead symbol search, the one owner of its pacing: the trimmed text is searched 300 ms after the last
 * keystroke, from one character, with no automatic retry, and an answer is reused for a minute. `term` is the text
 * the results belong to; it is empty until the first debounce has passed.
 */
export function useSymbolSearch(
  query: string,
  { enabled = true, limit = 12 }: { enabled?: boolean; limit?: number } = {},
) {
  const search = useServerFn(searchSymbolMetadata);
  const term = useDebounced(query.trim(), SYMBOL_SEARCH_DEBOUNCE_MS);
  const results = useQuery({
    queryKey: ["symbol-metadata-search", term, limit],
    queryFn: () => search({ data: { search: term, limit } }),
    enabled: enabled && term.length >= 1,
    retry: false,
    staleTime: 60_000,
  });
  return { term, results };
}
