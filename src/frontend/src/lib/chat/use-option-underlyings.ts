import { useQuery } from "@tanstack/react-query";
import { getOptionUnderlyings } from "@/lib/chat.functions";
import { useChatCapabilityAvailability } from "@/lib/chat/use-capability-availability";

export const OPTION_UNDERLYINGS_QUERY_KEY = ["option-underlyings"] as const;

/** Stored symbols use Arabic letter forms; compare after folding «ي/ى/ك» and trimming. */
const normalizeSymbol = (symbol: string) => symbol.trim().replace(/[يى]/g, "ی").replace(/ك/g, "ک");

/**
 * Feature 168: whether a symbol has listed options, so its option shortcut is offered only where it can answer. The
 * list is asked for only while option answers are available; while it is loading, or when the request fails, the
 * shortcut stays hidden.
 */
export function useHasListedOptions(symbol: string): boolean {
  const { optionComparison } = useChatCapabilityAvailability();
  const { data } = useQuery({
    queryKey: OPTION_UNDERLYINGS_QUERY_KEY,
    queryFn: () => getOptionUnderlyings(),
    enabled: optionComparison,
    staleTime: 30 * 60_000,
    retry: false,
    throwOnError: false,
    refetchOnWindowFocus: false,
  });
  if (!optionComparison || !data || symbol.trim().length === 0) return false;
  const wanted = normalizeSymbol(symbol);
  return data.some((item) => normalizeSymbol(item) === wanted);
}
