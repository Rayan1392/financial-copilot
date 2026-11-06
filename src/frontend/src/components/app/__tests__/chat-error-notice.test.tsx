import {
  createMemoryHistory,
  createRootRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router";
import { render, screen } from "@testing-library/react";
import type { ReactNode } from "react";
import { describe, expect, it } from "vitest";
import { ChatErrorNotice } from "../chat-error-notice";
import { GENERIC_CHAT_ERROR_TEXT, INSUFFICIENT_CREDIT_TEXT } from "@/lib/chat/chat-errors";

function inRouter(ui: ReactNode) {
  const router = createRouter({
    routeTree: createRootRoute({
      component: () => (
        <>
          <span data-testid="router-ready" />
          {ui}
        </>
      ),
    }),
    history: createMemoryHistory({ initialEntries: ["/chat"] }),
  });
  return render(<RouterProvider router={router} />);
}

describe("ChatErrorNotice", () => {
  it("points to the account page when credit ran out", async () => {
    inRouter(<ChatErrorNotice failure={{ code: "insufficient_credit", message: "x" }} />);
    await screen.findByTestId("router-ready");

    expect(screen.getByRole("alert")).toHaveTextContent(INSUFFICIENT_CREDIT_TEXT);
    expect(screen.getByRole("link", { name: "مشاهده اعتبار" })).toHaveAttribute("href", "/profile");
  });

  it("shows other failures without the credit link", async () => {
    inRouter(<ChatErrorNotice failure={{ code: "server", message: "x" }} />);
    await screen.findByTestId("router-ready");

    expect(screen.getByRole("alert")).toHaveTextContent(GENERIC_CHAT_ERROR_TEXT);
    expect(screen.queryByRole("link")).not.toBeInTheDocument();
  });

  it("renders nothing without a failure", async () => {
    inRouter(<ChatErrorNotice failure={null} />);
    await screen.findByTestId("router-ready");

    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});
