import { fireEvent, render, screen } from "@testing-library/react";
import { useState } from "react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { MobileComposer } from "../mobile-composer";

function mockPointer(coarse: boolean) {
  vi.stubGlobal("matchMedia", (query: string) => ({
    matches: query === "(pointer: coarse)" ? coarse : false,
    media: query,
    addEventListener: () => undefined,
    removeEventListener: () => undefined,
  }));
}

function Harness({
  onSubmit,
  online = true,
  pending = false,
}: {
  onSubmit: (text: string) => void;
  online?: boolean;
  pending?: boolean;
}) {
  const [value, setValue] = useState("");
  return (
    <MobileComposer
      value={value}
      onChange={setValue}
      onSubmit={onSubmit}
      online={online}
      pending={pending}
    />
  );
}

afterEach(() => vi.unstubAllGlobals());

describe("MobileComposer (AC-05)", () => {
  it("uses a 16 px input so iOS does not zoom on focus", () => {
    mockPointer(true);
    render(<Harness onSubmit={vi.fn()} />);
    expect(screen.getByLabelText("پرسش خود را بنویسید")).toHaveClass("text-m-body");
  });

  it("on touch screens Enter inserts a newline and only the button sends", () => {
    mockPointer(true);
    const onSubmit = vi.fn();
    render(<Harness onSubmit={onSubmit} />);
    const input = screen.getByLabelText("پرسش خود را بنویسید");
    fireEvent.change(input, { target: { value: "گزارش فصلی فملی" } });
    fireEvent.keyDown(input, { key: "Enter" });
    expect(onSubmit).not.toHaveBeenCalled();
    fireEvent.click(screen.getByRole("button", { name: "ارسال" }));
    expect(onSubmit).toHaveBeenCalledWith("گزارش فصلی فملی");
  });

  it("with a hardware keyboard Enter sends and Shift+Enter does not", () => {
    mockPointer(false);
    const onSubmit = vi.fn();
    render(<Harness onSubmit={onSubmit} />);
    const input = screen.getByLabelText("پرسش خود را بنویسید");
    fireEvent.change(input, { target: { value: "  فولاد  " } });
    fireEvent.keyDown(input, { key: "Enter", shiftKey: true });
    expect(onSubmit).not.toHaveBeenCalled();
    fireEvent.keyDown(input, { key: "Enter" });
    expect(onSubmit).toHaveBeenCalledWith("فولاد");
  });

  it("disables send when empty, pending, or offline, and explains offline", () => {
    mockPointer(true);
    const { rerender } = render(<Harness onSubmit={vi.fn()} />);
    expect(screen.getByRole("button", { name: "ارسال" })).toBeDisabled();

    rerender(<Harness onSubmit={vi.fn()} online={false} />);
    fireEvent.change(screen.getByLabelText("پرسش خود را بنویسید"), { target: { value: "x" } });
    const send = screen.getByRole("button", { name: "ارسال" });
    expect(send).toBeDisabled();
    expect(send).toHaveAccessibleDescription("برای ارسال پرسش به اینترنت نیاز است");

    rerender(<Harness onSubmit={vi.fn()} pending />);
    expect(screen.getByRole("button", { name: "ارسال" })).toBeDisabled();
  });
});
