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

const followInput = z.object({
  externalCompanyId: z.string().min(1).max(64),
});

const replaceInput = z.object({
  externalCompanyIds: z.array(z.string().min(1).max(64)).max(100),
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
