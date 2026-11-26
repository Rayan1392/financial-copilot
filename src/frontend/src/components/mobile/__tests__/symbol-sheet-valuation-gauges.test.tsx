import type { ReactNode } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { fireEvent, render, screen } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { SymbolSheet } from "../symbol-sheet";
import { CHAT_CAPABILITY_AVAILABILITY_QUERY_KEY } from "@/lib/chat/use-capability-availability";

const availability = vi.hoisted(() => ({ get: vi.fn() }));

vi.mock("@/lib/chat.functions", () => ({
  getChatCapabilityAvailability: availability.get,
}));
vi.mock("@/lib/workspace/follow-actions", () => ({
  useFollowActions: () => ({ unfollow: vi.fn(), followBySymbol: vi.fn() }),
}));
// The vaul drawer is not what this test is about; render the sheet's content in place.
vi.mock("@/components/mobile/bottom-sheet", () => ({
  BottomSheet: ({ children }: { children: ReactNode }) => <div>{children}</div>,
  SheetAction: ({ children, onClick }: { children: ReactNode; onClick?: () => void }) => (
    <button type="button" onClick={onClick}>
      {children}
    </button>
  ),
}));

function renderSheet() {
  const onAsk = vi.fn();
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } });
  render(
    <QueryClientProvider client={client}>
      <SymbolSheet
        subject={{ symbol: "شاراک", followed: false }}
        onOpenChange={vi.fn()}
        onAsk={onAsk}
        onAskOther={vi.fn()}
      />
    </QueryClientProvider>,
  );
  const settled = () =>
    vi.waitFor(() =>
      expect(client.getQueryState(CHAT_CAPABILITY_AVAILABILITY_QUERY_KEY)?.status).not.toBe(
        "pending",
      ),
    );
  return { onAsk, settled };
}

describe("symbol sheet valuation-gauge shortcut (OQ-132)", () => {
  beforeEach(() => {
    availability.get.mockReset();
  });

  it("offers the shortcut when the API says it is available and sends the typed phrase", async () => {
    availability.get.mockResolvedValue({ valuationGaugeCards: true });
    const { onAsk } = renderSheet();

    fireEvent.click(
      await screen.findByRole("button", { name: "گیج‌های ارزش‌گذاری شاراک" }, { timeout: 5000 }),
    );

    expect(onAsk).toHaveBeenCalledWith("گیج‌های ارزش‌گذاری شاراک");
  });

  it("hides the shortcut when the flag is off", async () => {
    availability.get.mockResolvedValue({ valuationGaugeCards: false });
    await renderSheet().settled();
    expect(screen.getByRole("button", { name: "ترازنامه شاراک" })).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /گیج/ })).not.toBeInTheDocument();
  });

  it("hides the shortcut when availability cannot be read", async () => {
    availability.get.mockImplementation(async () => {
      throw new Error("offline");
    });
    await renderSheet().settled();
    expect(screen.queryByRole("button", { name: /گیج/ })).not.toBeInTheDocument();
  });
});
