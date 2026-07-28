import { describe, expect, it, vi } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MessageList } from "../message-list";
import type { AssistantChatBlock, ChatMessage } from "@/lib/chat.functions";

function renderListing(block: AssistantChatBlock, onDisclosurePageChange = vi.fn(), originalQuery?: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  const messages: ChatMessage[] = [
    ...(originalQuery ? [{ id: "question", role: "user" as const, content: { text: originalQuery }, created_at: "2026-07-27T11:59:00Z" }] : []),
    { id: "disclosure", role: "assistant", content: block, created_at: "2026-07-27T12:00:00Z" },
  ];
  render(<QueryClientProvider client={client}><MessageList messages={messages} loading={false} streaming={false} onSuggested={vi.fn()} onDisclosurePageChange={onDisclosurePageChange} /></QueryClientProvider>);
  return onDisclosurePageChange;
}

describe("web AI disclosure listing", () => {
  it("renders the structured listing table, partial/stale notice, and retains the original query for continuation", () => {
    const originalQuery = "فهرست آخرین تولید و فروش منتشر شده را بده";
    const pageChange = vi.fn();
    renderListing({
      message: "نتیجه",
      intent: "DisclosureListing",
      creditsUsed: 1,
      suggestedQuestions: [],
      filters: [],
      citations: [],
      disclosureListingResult: {
        items: [{ disclosureId: "d-1", symbol: "فولاد", type: "MonthlyProductionSales", title: "عنوان بلند اطلاعیه فارسی با ProviderA", publishedAt: "2026-07-01", receivedAt: "2026-07-01T20:30:00Z", providerName: "ProviderA", isRevised: false, isComposing: false }],
        page: 1,
        totalPages: 2,
        hasPreviousPage: false,
        hasNextPage: true,
        coverageStatus: "UnmappedCompany",
        freshnessReasonCode: "StalePersistedNormalizedData",
      },
    }, pageChange, originalQuery);

    expect(screen.getByRole("table")).toBeInTheDocument();
    expect(screen.getByRole("status")).toBeInTheDocument();
    expect(screen.getByText("فولاد").closest("bdi")).not.toBeNull();
    fireEvent.click(screen.getAllByRole("button").at(-1)!);
    expect(pageChange).toHaveBeenCalledWith(2, originalQuery);
  });

  it("renders an empty structured listing without losing its table/pagination contract", () => {
    renderListing({
      message: "نتیجه",
      intent: "DisclosureListing",
      creditsUsed: 1,
      suggestedQuestions: [],
      filters: [],
      citations: [],
      disclosureListingResult: {
        items: [], page: 1, totalPages: 0, hasPreviousPage: false, hasNextPage: false,
        coverageStatus: "Complete", freshnessReasonCode: "PersistedNormalizedData",
      },
    });

    expect(screen.getByRole("table")).toBeInTheDocument();
    expect(screen.getByRole("navigation")).toBeInTheDocument();
  });

  it("renders a consolidated financial-statement row with its disclosure label", () => {
    renderListing({
      message: "نتیجه",
      intent: "DisclosureListing",
      creditsUsed: 1,
      suggestedQuestions: [],
      filters: [],
      citations: [],
      disclosureListingResult: {
        items: [{ disclosureId: "income-1", symbol: "فولاد", type: "IncomeStatement", title: "صورت سود و زیان", publishedAt: "2026-07-01", receivedAt: "2026-07-01T20:30:00Z", providerName: "ProviderA", isRevised: false, isComposing: true }],
        page: 1, totalPages: 1, hasPreviousPage: false, hasNextPage: false,
        coverageStatus: "Complete", freshnessReasonCode: "PersistedNormalizedData",
      },
    });

    expect(screen.getByRole("table")).toHaveTextContent("صورت سود و زیان");
    expect(screen.getByRole("table")).toHaveTextContent("تلفیقی");
  });
});
