import { fireEvent, render, screen, waitFor } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";

const login = vi.fn<(email: string, password: string) => Promise<void>>();
vi.mock("@/integrations/financial-copilot/auth", async (importOriginal) => ({
  ...(await importOriginal<typeof import("@/integrations/financial-copilot/auth")>()),
  login: (email: string, password: string) => login(email, password),
}));

const { AuthScreen } = await import("./auth");

function fillAndSubmit() {
  fireEvent.change(screen.getByLabelText("ایمیل"), { target: { value: "user@example.com" } });
  fireEvent.change(screen.getByLabelText("گذرواژه"), { target: { value: "secret" } });
  fireEvent.click(screen.getByRole("button", { name: "ورود" }));
}

describe("sign-in form usability", () => {
  it("toggles password visibility with an announced, pressed-state button", () => {
    render(<AuthScreen redirect="/chat" />);
    const password = screen.getByLabelText("گذرواژه");

    fireEvent.click(screen.getByRole("button", { name: "نمایش گذرواژه" }));
    expect(password).toHaveAttribute("type", "text");
    const hide = screen.getByRole("button", { name: "پنهان کردن گذرواژه" });
    expect(hide).toHaveAttribute("aria-pressed", "true");

    fireEvent.click(hide);
    expect(password).toHaveAttribute("type", "password");
  });

  it("says what it is doing while signing in", async () => {
    login.mockReturnValue(new Promise(() => {}));
    render(<AuthScreen redirect="/chat" />);

    fillAndSubmit();

    expect(await screen.findByRole("button", { name: "در حال ورود…" })).toBeDisabled();
  });

  it("announces a failed sign-in as an alert linked to the fields", async () => {
    login.mockRejectedValue(new Error("ایمیل یا گذرواژه واردشده نادرست است."));
    render(<AuthScreen redirect="/chat" />);

    fillAndSubmit();

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "ایمیل یا گذرواژه واردشده نادرست است.",
    );
    await waitFor(() =>
      expect(screen.getByLabelText("گذرواژه")).toHaveAttribute("aria-describedby", "auth-error"),
    );
    expect(screen.getByLabelText("ایمیل")).toHaveAttribute("aria-invalid", "true");
  });

  it("introduces the product beside the form at the site root", () => {
    render(<AuthScreen redirect="/chat" withIntro />);

    expect(
      screen.getByRole("heading", {
        name: "گزارش‌های مالی شرکت‌های بورسی را به زبان فارسی بپرسید",
      }),
    ).toBeInTheDocument();
    expect(screen.getByText("روند فروش ماهانه و ترکیب محصولات")).toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "ورود به حساب" })).toBeInTheDocument();
  });
});

describe("closed registration auth page", () => {
  it("keeps local login and password recovery without exposing public registration", () => {
    render(<AuthScreen redirect="/chat" />);

    expect(screen.getByRole("heading", { name: "ورود به حساب" })).toBeInTheDocument();
    expect(screen.getByLabelText("ایمیل")).toHaveAttribute("type", "email");
    expect(screen.getByLabelText("گذرواژه")).toHaveAttribute("type", "password");
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
