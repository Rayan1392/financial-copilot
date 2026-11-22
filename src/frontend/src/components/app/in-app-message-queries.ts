import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useSyncExternalStore } from "react";
import {
  getStoredAuthenticatedUser,
  subscribeToAuthChanges,
} from "@/integrations/financial-copilot/auth";
import { messageApi } from "@/integrations/financial-copilot/in-app-messages";
import { FinancialCopilotApiError } from "@/integrations/financial-copilot/api-client";
import { formatNumber } from "@/lib/format/persian";

const key = "in-app-messages";
const serverScope = () => null;
const getScope = () => {
  const user = getStoredAuthenticatedUser();
  return user?.tenantId && user.userId ? `${user.tenantId}:${user.userId}` : null;
};

export function useMessageScope() {
  const client = useQueryClient();
  const scope = useSyncExternalStore(subscribeToAuthChanges, getScope, serverScope);
  useEffect(
    () =>
      subscribeToAuthChanges(() => {
        const current = getScope();
        client.removeQueries({
          predicate: ({ queryKey }) => queryKey[0] === key && queryKey[1] !== current,
        });
      }),
    [client],
  );
  return [key, scope] as const;
}

export function useUnreadMessages() {
  const scopeKey = useMessageScope();
  const query = useQuery({
    queryKey: [...scopeKey, "summary"],
    queryFn: ({ signal }) => messageApi.summary(signal),
    enabled: scopeKey[1] !== null,
    refetchInterval: 60_000,
    refetchOnWindowFocus: "always",
    staleTime: 30_000,
    retry: false,
  });
  const denied =
    query.error instanceof FinancialCopilotApiError &&
    (query.error.status === 401 || query.error.status === 403);
  const count = scopeKey[1] && !denied ? (query.data?.unreadCount ?? 0) : 0;
  return { count, label: count >= 100 ? "۹۹+" : formatNumber(count) };
}
