import { useQuery } from "@tanstack/react-query";
import { getChatCapabilityAvailability } from "@/lib/chat.functions";

export const CHAT_CAPABILITY_AVAILABILITY_QUERY_KEY = ["chat-capability-availability"] as const;

/**
 * Feature 159 change «standalone-valuation-gauge-cards»: whether the optional chat shortcuts are offered. The API is
 * the only owner of the decision (flag, source mapping, V2 routing). While the answer is loading, or when the request
 * fails, every optional shortcut stays hidden, so a rollback or an outage never shows a shortcut that would not work.
 */
export function useChatCapabilityAvailability(): {
  valuationGaugeCards: boolean;
  optionComparison: boolean;
  /** The new-chat option card; typed option questions and the symbol menu follow `optionComparison` only. */
  optionComparisonStarterCard: boolean;
} {
  const { data } = useQuery({
    queryKey: CHAT_CAPABILITY_AVAILABILITY_QUERY_KEY,
    queryFn: () => getChatCapabilityAvailability(),
    staleTime: 5 * 60_000,
    retry: false,
    throwOnError: false,
    refetchOnWindowFocus: false,
  });
  return {
    valuationGaugeCards: data?.valuationGaugeCards === true,
    optionComparison: data?.optionComparison === true,
    optionComparisonStarterCard: data?.optionComparisonStarterCard === true,
  };
}
