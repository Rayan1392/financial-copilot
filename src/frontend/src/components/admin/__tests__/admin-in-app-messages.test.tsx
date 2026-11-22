import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { afterEach, expect, it, vi } from "vitest";
import { AdminMessageComposer } from "../admin-in-app-messages";
import type { AdminUserDirectoryItem } from "@/integrations/financial-copilot/admin-client";

const { start } = vi.hoisted(() => ({ start: vi.fn() }));
vi.mock("@/integrations/financial-copilot/in-app-messages", () => ({ messageApi: { start } }));

const recipient: AdminUserDirectoryItem = {
  userId: "selected-user",
  email: "selected@example.test",
  displayName: "کاربر منتخب",
  isEnabled: true,
  isLockedOut: false,
  status: "enabled",
  acquisitionSourceCode: "native",
  registeredAtUtc: null,
  lastSuccessfulLoginAtUtc: null,
  lastLoginProviderCode: null,
  linkedProviderCodes: [],
  subscription: { status: "not_authorized", data: null },
  credits: { status: "not_authorized", data: null },
};

afterEach(() => vi.unstubAllGlobals());

it("confirms the fixed recipient and retries an ambiguous send with the same key", async () => {
  vi.stubGlobal(
    "ResizeObserver",
    class {
      observe = vi.fn();
      unobserve = vi.fn();
      disconnect = vi.fn();
    },
  );
  start.mockRejectedValueOnce(new Error("network interrupted")).mockResolvedValueOnce(undefined);
  render(<AdminMessageComposer recipient={recipient} />);
  fireEvent.click(screen.getByRole("button", { name: "ارسال پیام" }));
  expect(await screen.findByText("selected@example.test")).toBeInTheDocument();
  fireEvent.change(screen.getByLabelText("عنوان"), { target: { value: "پیگیری حساب" } });
  fireEvent.change(screen.getByLabelText("متن پیام"), { target: { value: "متن آزمایشی" } });
  fireEvent.click(screen.getByRole("checkbox", { name: "امکان پاسخ" }));
  fireEvent.click(screen.getByRole("button", { name: "پیش‌نمایش" }));

  expect(screen.getByRole("heading", { name: "پیش‌نمایش پیام" })).toBeInTheDocument();
  expect(screen.getByText("selected@example.test")).toBeInTheDocument();
  expect(start).not.toHaveBeenCalled();
  fireEvent.click(screen.getByRole("button", { name: "تأیید و ارسال" }));
  fireEvent.click(await screen.findByRole("button", { name: "تلاش دوباره" }));
  await waitFor(() => expect(screen.getByRole("status")).toHaveTextContent("پیام ارسال شد."));

  expect(start).toHaveBeenCalledTimes(2);
  expect(start.mock.calls[0]).toEqual(start.mock.calls[1]);
  expect(start.mock.calls[0]).toEqual([
    recipient.userId,
    {
      subject: "پیگیری حساب",
      body: "متن آزمایشی",
      repliesEnabled: true,
      idempotencyKey: expect.any(String),
    },
  ]);
});
