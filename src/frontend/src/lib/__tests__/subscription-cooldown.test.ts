import { describe, expect, it } from "vitest";
import { formatSubscriptionCooldownDeniedMessage, isSubscriptionCooldownMessage } from "../subscription-cooldown";

describe("subscription cooldown denial message", () => {
  it("formats the authoritative UTC instant as Jalali date and Tehran time", () => {
    const message = formatSubscriptionCooldownDeniedMessage("2026-12-12T09:00:00Z", 25);

    expect(message).toBe(
      "اشتراک شما به پایان رسیده است و در حال حاضر نمی‌توانید پرسشی ارسال کنید. طرح رایگان شما در ۲۱ آذر ۱۴۰۵ ساعت ۱۲:۳۰ با ۲۵ اعتبار فعال می‌شود.",
    );
    expect(isSubscriptionCooldownMessage(message)).toBe(true);
  });

  it("does not fabricate a date when Billing has no authoritative cooldown", () => {
    const message = formatSubscriptionCooldownDeniedMessage(null, 25);

    expect(message).toContain("اشتراک شما به پایان رسیده است");
    expect(message).toContain("۲۵ اعتبار");
    expect(message).toContain("پس از پایان دوره محدودیت فعال می‌شود.");
    expect(message).not.toContain("در تاریخ");
  });
});
