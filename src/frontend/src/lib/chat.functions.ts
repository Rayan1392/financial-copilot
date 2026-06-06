import { createServerFn } from "@tanstack/react-start";
import { z } from "zod";
import {
  financialCopilotServerApi,
  requireFinancialCopilotAuth,
} from "@/integrations/financial-copilot/api-client.server";

export interface ChatThread {
  id: string;
  title: string;
  created_at: string;
  updated_at: string;
}

export interface ChatMessage {
  id: string;
  role: "user" | "assistant";
  content: { text: string } | AssistantChatBlock;
  created_at: string;
}

export interface AssistantChatBlock {
  message: string;
  intent: string;
  confidence: number;
  creditsUsed: number;
  suggestedQuestions: string[];
  filters: Array<{ label: string; value: string }>;
  table?: ScannerTable;
  citations: Array<{
    symbolCode: string;
    metricCode: string;
    observedAt?: string;
    freshnessStatus: string;
  }>;
}

export interface ScannerTable {
  columns: Array<{ identifier: string; displayName: string }>;
  rows: Array<{
    symbolCode: string;
    companyName?: string;
    score: number;
    cells: Record<
      string,
      { formattedValue?: string; value?: number; freshnessStatus: string; sourceTimestamp?: string }
    >;
  }>;
  executionFacts: {
    matchingSymbolCount: number;
    totalSymbolsEvaluated: number;
    fromCache: boolean;
    page: number;
    pageSize: number;
    totalPages: number;
  };
  missingDataWarnings: string[];
}

interface ConversationSummaryResponse {
  conversationId: string;
  title: string;
  startedAt: string;
  updatedAt: string;
}

interface QueryResponse extends AssistantContentResponse {
  conversationId: string;
  messageId: string;
  assistantMessageId: string;
}

interface ConversationMessagesResponse {
  messages: Array<{
    messageId: string;
    role: "User" | "Assistant";
    content: string;
    createdAt: string;
    assistantContent?: AssistantContentResponse;
  }>;
}

interface AssistantContentResponse {
  intent: string;
  clarificationRequired: boolean;
  clarificationMessage?: string;
  textAnswer?: string;
  scannerTable?: ScannerTable;
  symbolLookupTable?: ScannerTable;
  confidenceScore?: { score: number };
  explainableAnswer?: {
    filterChips: Array<{
      metricDisplayName: string;
      operatorSymbol: string;
      thresholdFormatted: string;
    }>;
    dataCitations: AssistantChatBlock["citations"];
    confidence: { score: number };
    suggestedFollowUpQuestions: string[];
    explanationText?: string;
  };
  usage?: { creditsCharged: number };
}

export const listThreads = createServerFn({ method: "GET" })
  .middleware([requireFinancialCopilotAuth])
  .handler(async ({ context }) => {
    const rows = await financialCopilotServerApi<ConversationSummaryResponse[]>(
      context,
      "/api/ai/v1/conversations",
    );
    return rows.map(mapThread);
  });

export const createThread = createServerFn({ method: "POST" })
  .middleware([requireFinancialCopilotAuth])
  .handler(async ({ context }) =>
    mapThread(
      await financialCopilotServerApi<ConversationSummaryResponse>(
        context,
        "/api/ai/v1/conversations",
        { method: "POST" },
      ),
    ),
  );

export const getThreadMessages = createServerFn({ method: "POST" })
  .middleware([requireFinancialCopilotAuth])
  .inputValidator((d) => z.object({ threadId: z.string().uuid() }).parse(d))
  .handler(async ({ context, data }) => {
    const result = await financialCopilotServerApi<ConversationMessagesResponse>(
      context,
      `/api/ai/v1/conversations/${data.threadId}/messages`,
    );
    return result.messages.map(mapPersistedMessage);
  });

export const deleteThread = createServerFn({ method: "POST" })
  .middleware([requireFinancialCopilotAuth])
  .inputValidator((d) => z.object({ threadId: z.string().uuid() }).parse(d))
  .handler(async ({ context, data }) => {
    await financialCopilotServerApi<void>(context, `/api/ai/v1/conversations/${data.threadId}`, {
      method: "DELETE",
    });
    return { ok: true };
  });

export const sendChatMessage = createServerFn({ method: "POST" })
  .middleware([requireFinancialCopilotAuth])
  .inputValidator((d) =>
    z
      .object({
        threadId: z.string().uuid().optional(),
        message: z.string().trim().min(1).max(2000),
        scannerPage: z.number().int().min(1).default(1),
        scannerPageSize: z.number().int().min(1).max(100).default(20),
      })
      .parse(d),
  )
  .handler(async ({ context, data }) => {
    const result = await financialCopilotServerApi<QueryResponse>(context, "/api/ai/v1/query", {
      method: "POST",
      body: JSON.stringify({
        conversationId: data.threadId,
        message: data.message,
        scannerPage: data.scannerPage,
        scannerPageSize: data.scannerPageSize,
      }),
    });
    const createdAt = new Date().toISOString();
    return {
      threadId: result.conversationId,
      userMsg: {
        id: result.messageId,
        role: "user",
        content: { text: data.message },
        created_at: createdAt,
      } satisfies ChatMessage,
      aiMsg: {
        id: result.assistantMessageId,
        role: "assistant",
        content: mapAssistantBlock(result, result.textAnswer ?? ""),
        created_at: createdAt,
      } satisfies ChatMessage,
    };
  });

function mapThread(row: ConversationSummaryResponse): ChatThread {
  return {
    id: row.conversationId,
    title: row.title,
    created_at: row.startedAt,
    updated_at: row.updatedAt,
  };
}

function mapPersistedMessage(
  message: ConversationMessagesResponse["messages"][number],
): ChatMessage {
  return {
    id: message.messageId,
    role: message.role.toLowerCase() as ChatMessage["role"],
    content:
      message.role === "User"
        ? { text: message.content }
        : mapAssistantBlock(message.assistantContent, message.content),
    created_at: message.createdAt,
  };
}

function mapAssistantBlock(
  content: AssistantContentResponse | undefined,
  fallbackMessage: string,
): AssistantChatBlock {
  const explanation = content?.explainableAnswer;
  // Prefer symbolLookupTable over scannerTable; both render via ScannerResultTable.
  const table = content?.symbolLookupTable ?? content?.scannerTable;
  return {
    message:
      explanation?.explanationText ??
      content?.clarificationMessage ??
      content?.textAnswer ??
      fallbackMessage,
    intent: content?.intent ?? "Unknown",
    confidence: content?.confidenceScore?.score ?? explanation?.confidence.score ?? 0,
    creditsUsed: content?.usage?.creditsCharged ?? 0,
    suggestedQuestions: explanation?.suggestedFollowUpQuestions ?? [],
    filters:
      explanation?.filterChips.map((chip) => ({
        label: chip.metricDisplayName,
        value: `${chip.operatorSymbol} ${chip.thresholdFormatted}`,
      })) ?? [],
    table,
    citations: explanation?.dataCitations ?? [],
  };
}
