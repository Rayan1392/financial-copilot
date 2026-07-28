import { createServerFn } from "@tanstack/react-start";
import { z } from "zod";
import {
  financialCopilotServerApi,
  requireFinancialCopilotAuth,
} from "@/integrations/financial-copilot/api-client.server";

export interface MetricMetadata {
  metricCode: string;
  displayName: string;
  supportedPeriods: string[];
  aliases: Array<{ expression: string; language: string }>;
}

export interface PeriodMetadata {
  code: string;
  displayName: string;
  displayNameFa: string;
}

export interface SymbolMetadata {
  externalCompanyId: string;
  symbolCode: string;
  companyName: string;
  companyNameEnglish?: string;
  industryName?: string;
}

export interface IndustryMetadata {
  industryId: string;
  displayName: string;
}

const searchInput = z.object({
  search: z.string().max(100).optional(),
  limit: z.number().int().min(1).max(50).default(20),
});

export const getMetricMetadata = createServerFn({ method: "GET" })
  .middleware([requireFinancialCopilotAuth])
  .handler(async ({ context }) => {
    const response = await financialCopilotServerApi<{ metrics: MetricMetadata[] }>(
      context,
      "/api/ai/v1/metadata/metrics",
    );
    return response.metrics;
  });

export const getPeriodMetadata = createServerFn({ method: "GET" })
  .middleware([requireFinancialCopilotAuth])
  .handler(async ({ context }) =>
    financialCopilotServerApi<PeriodMetadata[]>(context, "/api/ai/v1/metadata/periods"),
  );

export const searchSymbolMetadata = createServerFn({ method: "POST" })
  .middleware([requireFinancialCopilotAuth])
  .inputValidator((data) => searchInput.parse(data))
  .handler(async ({ context, data }) =>
    financialCopilotServerApi<SymbolMetadata[]>(
      context,
      `/api/ai/v1/metadata/symbols?${new URLSearchParams({
        search: data.search ?? "",
        limit: data.limit.toString(),
      })}`,
    ),
  );

export const searchIndustryMetadata = createServerFn({ method: "POST" })
  .middleware([requireFinancialCopilotAuth])
  .inputValidator((data) => searchInput.parse(data))
  .handler(async ({ context, data }) =>
    financialCopilotServerApi<IndustryMetadata[]>(
      context,
      `/api/ai/v1/metadata/industries?${new URLSearchParams({
        search: data.search ?? "",
        limit: data.limit.toString(),
      })}`,
    ),
  );
