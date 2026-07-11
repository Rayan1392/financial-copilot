import { createServerFn } from "@tanstack/react-start";
import { z } from "zod";
import {
  financialCopilotServerApi,
  requireFinancialCopilotAuth,
} from "@/integrations/financial-copilot/api-client.server";

export interface FollowedSymbol {
  externalCompanyId: string;
  symbol: string;
  companyName: string;
  companyNameEnglish?: string;
  followedAtUtc: string;
  source?: string;
}

export interface FollowedSymbolsResponse {
  symbols: FollowedSymbol[];
}

export interface InsightEvidenceItem {
  label: string;
  value: string;
  sourceProvider: string;
  sourcePeriod?: string;
  lastSyncedAtUtc?: string;
}

export interface InsightAction {
  kind: string;
  label: string;
  target?: string;
}

export interface InsightFeedItem {
  id: string;
  externalCompanyId: string;
  symbol: string;
  industryCode?: string;
  insightType: string;
  severity: string;
  importanceScore: number;
  confidenceScore: number;
  title: string;
  summary: string;
  reason: string;
  evidence: InsightEvidenceItem[];
  sourceProviderName: string;
  sourceEntityType: string;
  sourceEntityId?: string;
  sourcePeriod?: string;
  detectedAtUtc: string;
  expiresAtUtc?: string;
  suggestedActions: string[];
}

export interface FollowedSymbolInsightFeedItem {
  insight: InsightFeedItem;
  seen: boolean;
  dismissed: boolean;
  seenAtUtc?: string;
  dismissedAtUtc?: string;
  actions: InsightAction[];
}

export interface FollowedSymbolInsightFeedResponse {
  totalCount: number;
  generatedAtUtc: string;
  items: FollowedSymbolInsightFeedItem[];
  emptyState?: {
    reason: string;
    message: string;
    suggestedActions: InsightAction[];
  };
}

export interface UserInsightState {
  insightEventId: string;
  seen: boolean;
  dismissed: boolean;
  seenAtUtc?: string;
  dismissedAtUtc?: string;
}

interface QueryResponse {
  textAnswer?: string;
  clarificationMessage?: string;
}

const followInput = z.object({
  externalCompanyId: z.string().min(1).max(64),
});

const replaceInput = z.object({
  externalCompanyIds: z.array(z.string().min(1).max(64)).max(100),
});

const insightFeedInput = z.object({
  type: z.string().optional(),
  severity: z.string().optional(),
  includeDismissed: z.boolean().default(false),
  skip: z.number().int().min(0).default(0),
  take: z.number().int().min(1).max(100).default(20),
});

const insightIdInput = z.object({
  insightEventId: z.string().uuid(),
});

export const getFollowedSymbols = createServerFn({ method: "GET" })
  .middleware([requireFinancialCopilotAuth])
  .handler(async ({ context }) =>
    financialCopilotServerApi<FollowedSymbolsResponse>(context, "/api/v1/followed-symbols/me"),
  );

export const followSymbolByExternalId = createServerFn({ method: "POST" })
  .middleware([requireFinancialCopilotAuth])
  .inputValidator((data) => followInput.parse(data))
  .handler(async ({ context, data }) =>
    financialCopilotServerApi<FollowedSymbol>(
      context,
      `/api/v1/followed-symbols/me/${encodeURIComponent(data.externalCompanyId)}`,
      { method: "POST" },
    ),
  );

export const unfollowSymbolByExternalId = createServerFn({ method: "POST" })
  .middleware([requireFinancialCopilotAuth])
  .inputValidator((data) => followInput.parse(data))
  .handler(async ({ context, data }) =>
    financialCopilotServerApi<FollowedSymbolsResponse>(
      context,
      `/api/v1/followed-symbols/me/${encodeURIComponent(data.externalCompanyId)}`,
      { method: "DELETE" },
    ),
  );

export const replaceFollowedSymbols = createServerFn({ method: "POST" })
  .middleware([requireFinancialCopilotAuth])
  .inputValidator((data) => replaceInput.parse(data))
  .handler(async ({ context, data }) =>
    financialCopilotServerApi<FollowedSymbolsResponse>(context, "/api/v1/followed-symbols/me", {
      method: "PUT",
      body: JSON.stringify({ externalCompanyIds: data.externalCompanyIds, source: "Frontend" }),
    }),
  );

export const getFollowedSymbolInsights = createServerFn({ method: "GET" })
  .middleware([requireFinancialCopilotAuth])
  .inputValidator((data) => insightFeedInput.parse(data))
  .handler(async ({ context, data }) => {
    const params = new URLSearchParams({
      includeDismissed: String(data.includeDismissed),
      skip: String(data.skip),
      take: String(data.take),
    });
    if (data.type) params.set("type", data.type);
    if (data.severity) params.set("severity", data.severity);
    return financialCopilotServerApi<FollowedSymbolInsightFeedResponse>(
      context,
      `/api/v1/insights/followed-symbols/me?${params.toString()}`,
    );
  });

export const markInsightSeen = createServerFn({ method: "POST" })
  .middleware([requireFinancialCopilotAuth])
  .inputValidator((data) => insightIdInput.parse(data))
  .handler(async ({ context, data }) =>
    financialCopilotServerApi<UserInsightState>(
      context,
      `/api/v1/insights/${encodeURIComponent(data.insightEventId)}/seen`,
      { method: "POST" },
    ),
  );

export const dismissInsight = createServerFn({ method: "POST" })
  .middleware([requireFinancialCopilotAuth])
  .inputValidator((data) => insightIdInput.parse(data))
  .handler(async ({ context, data }) =>
    financialCopilotServerApi<UserInsightState>(
      context,
      `/api/v1/insights/${encodeURIComponent(data.insightEventId)}/dismiss`,
      { method: "POST" },
    ),
  );

export const explainInsight = createServerFn({ method: "POST" })
  .middleware([requireFinancialCopilotAuth])
  .inputValidator((data) => insightIdInput.parse(data))
  .handler(async ({ context, data }) => {
    const response = await financialCopilotServerApi<QueryResponse>(context, "/api/ai/v1/query", {
      method: "POST",
      body: JSON.stringify({
        message: "Explain this insight",
        context: { insightEventId: data.insightEventId },
      }),
    });
    return {
      text: response.textAnswer ?? response.clarificationMessage ?? "",
    };
  });
