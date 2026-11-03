const subscriptionCooldownPrefix = "اشتراک شما به پایان رسیده است";

export function formatSubscriptionCooldownDeniedMessage(
  cooldownUntilUtc: string | null | undefined,
  freePlanCredits: number | null | undefined,
): string {
  const credits = Number.isFinite(freePlanCredits) && (freePlanCredits ?? 0) >= 0
    ? new Intl.NumberFormat("fa-IR").format(freePlanCredits!)
    : null;
  const instant = cooldownUntilUtc ? new Date(cooldownUntilUtc) : null;
  const hasValidInstant = instant !== null && Number.isFinite(instant.getTime());

  if (!hasValidInstant) {
    return credits === null
      ? `${subscriptionCooldownPrefix} و در حال حاضر نمی‌توانید پرسشی ارسال کنید. زمان فعال شدن طرح رایگان شما در دسترس نیست.`
      : `${subscriptionCooldownPrefix} و در حال حاضر نمی‌توانید پرسشی ارسال کنید. طرح رایگان شما با ${credits} اعتبار پس از پایان دوره محدودیت فعال می‌شود.`;
  }

  try {
    const date = new Intl.DateTimeFormat("fa-IR-u-ca-persian", {
      timeZone: "Asia/Tehran",
      year: "numeric",
      month: "long",
      day: "numeric",
    }).format(instant!);
    const time = new Intl.DateTimeFormat("fa-IR", {
      timeZone: "Asia/Tehran",
      hour: "2-digit",
      minute: "2-digit",
      hourCycle: "h23",
    }).format(instant!);
    const allowance = credits === null ? "" : ` با ${credits} اعتبار`;
    return `${subscriptionCooldownPrefix} و در حال حاضر نمی‌توانید پرسشی ارسال کنید. طرح رایگان شما در ${date} ساعت ${time}${allowance} فعال می‌شود.`;
  } catch {
    return credits === null
      ? `${subscriptionCooldownPrefix} و در حال حاضر نمی‌توانید پرسشی ارسال کنید. زمان فعال شدن طرح رایگان شما در دسترس نیست.`
      : `${subscriptionCooldownPrefix} و در حال حاضر نمی‌توانید پرسشی ارسال کنید. طرح رایگان شما با ${credits} اعتبار پس از پایان دوره محدودیت فعال می‌شود.`;
  }
}

export function isSubscriptionCooldownMessage(message: string): boolean {
  return message.startsWith(subscriptionCooldownPrefix);
}
