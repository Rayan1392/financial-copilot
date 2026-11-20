import { describe, expect, it } from "vitest";
import type { AssistantContentResponse, ScannerTable } from "@/lib/chat.functions";
import { mapAssistantBlock } from "@/lib/chat/assistant-block-mapper";

const table = (overrides: Partial<ScannerTable> = {}): ScannerTable => ({
  columns: [{ identifier: "SYMBOL", displayName: "نماد" }],
  rows: [
    {
      symbolCode: "فولاد",
      score: 1,
      cells: { SYMBOL: { formattedValue: "فولاد", freshnessStatus: "Persisted" } },
    },
  ],
  executionFacts: {
    matchingSymbolCount: 1,
    totalSymbolsEvaluated: 10,
    fromCache: false,
    page: 1,
    pageSize: 20,
    totalPages: 1,
  },
  missingDataWarnings: [],
  ...overrides,
});

const content = (overrides: Partial<AssistantContentResponse> = {}): AssistantContentResponse => ({
  intent: "MetricLookup",
  clarificationRequired: false,
  ...overrides,
});

describe("mapAssistantBlock", () => {
  it("falls back to the stored message text when there is no structured content", () => {
    const block = mapAssistantBlock(undefined, "پاسخ ذخیره‌شده");

    expect(block.message).toBe("پاسخ ذخیره‌شده");
    expect(block.intent).toBe("Unknown");
    expect(block.creditsUsed).toBe(0);
    expect(block.suggestedQuestions).toEqual([]);
    expect(block.suggestedActions).toEqual([]);
    expect(block.filters).toEqual([]);
    expect(block.citations).toEqual([]);
    expect(block.table).toBeUndefined();
    expect(block.orchestration).toBeUndefined();
  });

  it("prefers the explanation, then the clarification, then the text answer", () => {
    expect(
      mapAssistantBlock(content({ textAnswer: "متن", clarificationMessage: "روشن‌سازی" }), "x")
        .message,
    ).toBe("روشن‌سازی");
    expect(mapAssistantBlock(content({ textAnswer: "متن" }), "x").message).toBe("متن");
    const explained = mapAssistantBlock(
      content({
        textAnswer: "متن",
        confidenceScore: undefined,
        explainableAnswer: {
          filterChips: [{ metricDisplayName: "P/E", operatorSymbol: "<", thresholdFormatted: "5" }],
          dataCitations: [],
          confidence: { score: 0.8 },
          suggestedFollowUpQuestions: ["پرسش بعدی"],
          explanationText: "توضیح",
        },
        usage: { creditsCharged: 3 },
        outcome: "Answered",
        outcomeReasonCode: "ok",
      }),
      "x",
    );

    expect(explained.message).toBe("توضیح");
    expect(explained.filters).toEqual([{ label: "P/E", value: "< 5" }]);
    expect(explained.confidence).toBe(0.8);
    expect(explained.suggestedQuestions).toEqual(["پرسش بعدی"]);
    expect(explained.creditsUsed).toBe(3);
    expect(explained.outcome).toBe("Answered");
    expect(explained.outcomeReasonCode).toBe("ok");
  });

  it("prefers the symbol lookup table and drops an empty table", () => {
    const lookup = table({ columns: [{ identifier: "PRICE", displayName: "قیمت" }] });
    expect(
      mapAssistantBlock(content({ symbolLookupTable: lookup, scannerTable: table() }), "x").table,
    ).toBe(lookup);

    const empty = table({ rows: [] });
    expect(mapAssistantBlock(content({ scannerTable: empty }), "x").table).toBeUndefined();
    const emptyWithWarning = table({ rows: [], missingDataWarnings: ["داده نیست"] });
    expect(mapAssistantBlock(content({ scannerTable: emptyWithWarning }), "x").table).toBe(
      emptyWithWarning,
    );
  });

  it("turns the monthly sales technical unit note into a table label", () => {
    const monthly = table({
      columns: [{ identifier: "monthly_sales_amount", displayName: "فروش" }],
    });
    const block = mapAssistantBlock(
      content({ textAnswer: "Unit: million Rials", scannerTable: monthly }),
      "x",
    );

    expect(block.message).toBe("");
    expect(block.tableMetadataLabel).toBe("واحد: میلیون ریال");
    expect(mapAssistantBlock(content({ textAnswer: "Unit: million Rials" }), "x").message).toBe(
      "Unit: million Rials",
    );
  });

  it("shows no text beside a financial statement analysis and keeps structured results", () => {
    const analysis = {
      companySymbol: "فولاد",
    } as unknown as AssistantContentResponse["financialStatementAnalysisResult"];
    const block = mapAssistantBlock(
      content({
        textAnswer: "متن",
        financialStatementAnalysisResult: analysis,
        financialStatementTableResult: { balanceSheetReport: null },
        sources: null,
        groundedAnalysis: null,
      }),
      "x",
    );

    expect(block.message).toBe("");
    expect(block.financialStatementAnalysisResult).toBe(analysis);
    expect(block.financialStatementTableResult).toEqual({ balanceSheetReport: null });
    expect(block.sources).toBeUndefined();
    expect(block.groundedAnalysis).toBeUndefined();
  });

  it("adds orchestration details only when the answer carries them", () => {
    const block = mapAssistantBlock(
      content({ aiOrchestrationMode: "V2", workflowVersion: "2", workflowCorrelationId: "c-1" }),
      "x",
    );

    expect(block.orchestration).toEqual({
      mode: "V2",
      workflowVersion: "2",
      providerSelection: undefined,
      providerFallbackOccurred: undefined,
      correlationId: "c-1",
    });
  });
});
