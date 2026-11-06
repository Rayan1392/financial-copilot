import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  createMemoryHistory,
  createRootRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router";
import { fireEvent, render, screen, within } from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import type { ChatThread } from "@/lib/chat.functions";

const mutate = vi.fn();
const reset = vi.fn();
const threads: ChatThread[] = [
  {
    id: "11111111-1111-1111-1111-111111111111",
    title: "روند فروش ماهانه کگهر",
    created_at: "2026-11-06T08:00:00Z",
    updated_at: "2026-11-06T08:30:00Z",
  },
];

vi.mock("@tanstack/react-start", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@tanstack/react-start")>()),
  useServerFn: () => vi.fn(),
}));
vi.mock("@/lib/workspace/queries", () => ({
  useConversations: () => ({ data: threads }),
  useUsage: () => ({ data: undefined, isLoading: false, isError: false, isFetching: false, refetch: vi.fn() }),
  useDeleteConversation: () => ({ mutate, reset, isPending: false, isError: false }),
}));

const { ConversationSidebar } = await import("../sidebar");

function renderExpandedSidebar() {
  const router = createRouter({
    routeTree: createRootRoute({
      component: () => (
        <>
          <span data-testid="router-ready" />
          <ConversationSidebar collapsed={false} onToggle={() => {}} />
        </>
      ),
    }),
    history: createMemoryHistory({ initialEntries: ["/chat"] }),
  });
  return render(
    <QueryClientProvider client={new QueryClient()}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
}

describe("ConversationSidebar delete", () => {
  afterEach(() => {
    mutate.mockReset();
    reset.mockReset();
  });

  it("asks for confirmation instead of deleting on the first click", async () => {
    renderExpandedSidebar();
    await screen.findByTestId("router-ready");

    fireEvent.click(screen.getByRole("button", { name: "حذف گفتگو: روند فروش ماهانه کگهر" }));

    const dialog = await screen.findByRole("alertdialog");
    expect(within(dialog).getByText(/روند فروش ماهانه کگهر/)).toBeInTheDocument();
    expect(mutate).not.toHaveBeenCalled();

    fireEvent.click(within(dialog).getByRole("button", { name: "حذف" }));
    expect(mutate).toHaveBeenCalledWith(threads[0].id);
  });

  it("does not delete when the user cancels", async () => {
    renderExpandedSidebar();
    await screen.findByTestId("router-ready");

    fireEvent.click(screen.getByRole("button", { name: "حذف گفتگو: روند فروش ماهانه کگهر" }));
    const dialog = await screen.findByRole("alertdialog");
    fireEvent.click(within(dialog).getByRole("button", { name: "انصراف" }));

    expect(mutate).not.toHaveBeenCalled();
    expect(screen.queryByRole("alertdialog")).not.toBeInTheDocument();
  });
});
