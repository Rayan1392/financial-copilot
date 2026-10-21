import { createServerFn } from "@tanstack/react-start";
import {
  financialCopilotServerApi,
  requireFinancialCopilotAuth,
} from "@/integrations/financial-copilot/api-client.server";
import {
  apiUrl,
  FinancialCopilotAuthError,
  getAccessToken,
} from "@/integrations/financial-copilot/auth";

export interface UsageSummary {
  balance: number;
  reservedCredits: number;
  availableSpendingCapacity: number;
  walletUpdatedAt: string;
  planCode?: string | null;
  planName?: string | null;
  planIncludedCredits?: number | null;
}

export interface WatchlistQuote {
  symbol: string;
  latestPrice?: number;
  changePercent?: number;
  asOf?: string;
  sourceKind?: string;
  isStale: boolean;
}

export interface WatchlistView {
  symbols: WatchlistQuote[];
  asOf?: string;
}

export interface MarketIndexObservation {
  symbol: string;
  name: string;
  value?: number;
  changePercent?: number;
  asOf: string;
  sourceKind: string;
}

export interface MarketMover {
  symbol: string;
  name: string;
  latestPrice: number;
  changePercent: number;
  asOf: string;
  isStale: boolean;
}

export interface MarketSummary {
  indices: MarketIndexObservation[];
  topGainers: MarketMover[];
  topLosers: MarketMover[];
  asOf?: string;
  realMoneyFlow?: number;
  trendingIndustries?: Array<{ name: string; changePercent: number }>;
  insight?: string;
}

export const getUsage = createServerFn({ method: "GET" })
  .middleware([requireFinancialCopilotAuth])
  .handler(async ({ context }) =>
    financialCopilotServerApi<UsageSummary>(context, "/api/v1/usage/me"),
  );

export const getWatchlist = () => authorizedMarketRequest<WatchlistView>("/api/v1/watchlists/me");

export const getMarketSummary = () =>
  authorizedMarketRequest<MarketSummary>("/api/v1/market/summary");

async function authorizedMarketRequest<T>(path: string): Promise<T> {
  const token = await getAccessToken();
  if (!token) throw new FinancialCopilotAuthError("Sign-in required.", 401);

  const response = await fetch(apiUrl(path), {
    method: "GET",
    credentials: "include",
    cache: "no-store",
    headers: {
      Accept: "application/json",
      Authorization: `Bearer ${token}`,
      "X-Correlation-Id": crypto.randomUUID(),
    },
  });

  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new FinancialCopilotAuthError(
      problem?.detail ?? problem?.title ?? `Market data request failed (${response.status}).`,
      response.status,
      problem?.type,
      problem?.correlationId,
    );
  }

  return response.status === 204 ? (undefined as T) : ((await response.json()) as T);
}
