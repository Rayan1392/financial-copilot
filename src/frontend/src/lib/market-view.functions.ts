import { createServerFn } from "@tanstack/react-start";
import {
  financialCopilotServerApi,
  requireFinancialCopilotAuth,
} from "@/integrations/financial-copilot/api-client.server";

export interface UsageSummary {
  balance: number;
  reservedCredits: number;
  availableSpendingCapacity: number;
  walletUpdatedAt: string;
  planCode?: string | null;
  planName?: string | null;
  planIncludedCredits?: number | null;
  creditQuotaMode?: "Finite" | "Unlimited";
  subscriptionExpiresAtUtc?: string | null;
}

export interface TopUpStatus {
  status: "granted" | "expired-unfulfilled" | "ineligible-unfulfilled";
  purchaseId: string;
  grantId?: string | null;
  creditsGranted: number;
  expiresAtUtc: string;
  grantedAtUtc?: string | null;
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

export const getTopUpStatus = createServerFn({ method: "GET" })
  .middleware([requireFinancialCopilotAuth])
  .inputValidator((data: { purchaseId: string }) => data)
  .handler(async ({ context, data }) =>
    financialCopilotServerApi<TopUpStatus | null>(
      context,
      "/api/v1/billing/top-ups/" + encodeURIComponent(data.purchaseId),
      {},
      true,
    ),
  );

export const getWatchlist = createServerFn({ method: "GET" })
  .middleware([requireFinancialCopilotAuth])
  .handler(async ({ context }) =>
    financialCopilotServerApi<WatchlistView>(context, "/api/v1/watchlists/me"),
  );

export const getMarketSummary = createServerFn({ method: "GET" })
  .middleware([requireFinancialCopilotAuth])
  .handler(async ({ context }) =>
    financialCopilotServerApi<MarketSummary>(context, "/api/v1/market/summary"),
  );
