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
    expect(screen.getByRole("link", { name: "گذرواژه را فراموش کرده‌اید؟" })).toHaveAttribute(
      "href",
      "/forgot-password",
    );

    expect(
      screen.queryByRole("button", { name: /Create Account|Register|Sign Up|ساخت حساب|ثبت.?نام/i }),
    ).not.toBeInTheDocument();
  });

  it("guides new users to TahlilApp in a separate tab", () => {
    render(<AuthScreen redirect="/chat" />);

    const signupLink = screen.getByRole("link", { name: "تحلیل‌اپ" });
    const guidance = signupLink.closest("p");
    expect(guidance).toHaveAttribute("dir", "rtl");
    expect(guidance).toHaveTextContent("هنوز حساب کاربری ندارید؟");
    expect(guidance).toHaveTextContent("برای ثبت‌نام و ورود به ساپیو، از طریق");
    expect(guidance).toHaveTextContent("اقدام کنید.");
    expect(guidance?.querySelector("br")).toBeInTheDocument();
    expect(signupLink).toHaveAttribute("href", "https://tahlilapp.com/sapio");
    expect(signupLink).toHaveAttribute("target", "_blank");
    expect(signupLink).toHaveAttribute("rel", "noopener noreferrer");
  });
});
