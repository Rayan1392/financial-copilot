import { createFileRoute } from "@tanstack/react-router";
import { MobileScreen } from "@/components/mobile/mobile-screen";
import { ProfilePanel } from "@/components/app/profile-panel";

/** Profile and account security inside the mobile frame; the body is the desktop page's shared panel (T14b). */
export const Route = createFileRoute("/m/account/profile")({
  head: () => ({ meta: [{ title: "پروفایل و امنیت حساب — ساپیو" }] }),
  component: MobileProfile,
});

function MobileProfile() {
  return (
    <MobileScreen
      title="پروفایل و امنیت حساب"
      backFallback="/m/account"
      showTabBar={false}
      hideNewChat
    >
      <ProfilePanel as="div" signInRedirect="/m/account/profile" showHeading={false} />
    </MobileScreen>
  );
}
