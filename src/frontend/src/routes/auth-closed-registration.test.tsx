import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { AuthScreen } from "./auth";

describe("closed registration auth page", () => {
  it("keeps local login and password recovery without exposing public registration", () => {
    render(<AuthScreen redirect="/chat" />);

    expect(screen.getByRole("heading", { name: "ورود به حساب" })).toBeInTheDocument();
    expect(screen.getByLabelText("ایمیل")).toHaveAttribute("type", "email");
    expect(screen.getByLabelText("رمز عبور")).toHaveAttribute("type", "password");
    expect(screen.getByRole("button", { name: "ورود" })).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "گذرواژه را فراموش کرده‌اید؟" })).toHaveAttribute("href", "/forgot-password");

    expect(screen.queryByText(/Create Account|Register|Sign Up|ساخت حساب|ثبت.?نام/i)).not.toBeInTheDocument();
    expect(screen.queryByRole("button", { name: /Create Account|Register|Sign Up|ساخت حساب|ثبت.?نام/i })).not.toBeInTheDocument();
  });
});
