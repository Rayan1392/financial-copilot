import { screen } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { ChatErrorCard, INDETERMINATE_TEXT, NOT_FOUND_TEXT } from "../chat-error-card";
import { renderInRouter } from "./test-router";
import { INSUFFICIENT_CREDIT_TEXT } from "@/lib/chat/chat-errors";

const network = { code: "network" as const, message: "Failed to fetch" };

describe("ChatErrorCard (spec AC-06a, AC-06b, AC-07)", () => {
  it("first message: indeterminate status, a history link, and no resend or retry control", async () => {
    await renderInRouter(<ChatErrorCard failure={network} recovery={{ kind: "indeterminate" }} />);
    expect(screen.getByRole("alert")).toHaveTextContent(INDETERMINATE_TEXT);
    expect(screen.getByRole("link", { name: "تاریخچه" })).toHaveAttribute("href", "/m/history");
    expect(screen.queryAllByRole("button")).toHaveLength(0);
    expect(screen.getAllByRole("link").map((link) => link.textContent)).toEqual(["تاریخچه"]);
  });

  it("follow-up: no action at all while reconciling", async () => {
    await renderInRouter(<ChatErrorCard failure={network} recovery={{ kind: "reconciling" }} />);
    expect(screen.getByRole("status")).toHaveTextContent("در حال بررسی وضعیت...");
    expect(screen.queryAllByRole("button")).toHaveLength(0);
  });

  it("follow-up: offers only «بررسی دوباره» when the refetch failed", async () => {
    const onRecheck = vi.fn();
    const onEditAndResend = vi.fn();
    await renderInRouter(
      <ChatErrorCard
        failure={network}
        recovery={{ kind: "reconcile_failed" }}
        onRecheck={onRecheck}
        onEditAndResend={onEditAndResend}
      />,
    );
    expect(screen.getAllByRole("button").map((button) => button.textContent)).toEqual([
      "بررسی دوباره",
    ]);
    screen.getByRole("button", { name: "بررسی دوباره" }).click();
    expect(onRecheck).toHaveBeenCalledOnce();
    expect(onEditAndResend).not.toHaveBeenCalled();
  });

  it("follow-up: offers «ویرایش و ارسال دوباره» only after a refetch found no answer", async () => {
    const onEditAndResend = vi.fn();
    await renderInRouter(
      <ChatErrorCard
        failure={network}
        recovery={{ kind: "not_found" }}
        onEditAndResend={onEditAndResend}
      />,
    );
    expect(screen.getByRole("alert")).toHaveTextContent(NOT_FOUND_TEXT);
    screen.getByRole("button", { name: "ویرایش و ارسال دوباره" }).click();
    expect(onEditAndResend).toHaveBeenCalledOnce();
  });

  it("insufficient credit: existing text, «مشاهده اعتبار», no external purchase link", async () => {
    const { container } = await renderInRouter(
      <ChatErrorCard
        failure={{ code: "insufficient_credit", message: "x" }}
        recovery={{ kind: "definite", code: "insufficient_credit" }}
      />,
    );
    expect(screen.getByRole("alert")).toHaveTextContent(INSUFFICIENT_CREDIT_TEXT);
    expect(screen.getByRole("link", { name: "مشاهده اعتبار" })).toHaveAttribute(
      "href",
      "/m/account",
    );
    expect(container.querySelector('a[href*="tahlilapp.com"]')).toBeNull();
    expect(screen.queryAllByRole("button")).toHaveLength(0);
  });

  it("renders nothing once the answer was found", async () => {
    await renderInRouter(<ChatErrorCard failure={network} recovery={{ kind: "answered" }} />);
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });
});
