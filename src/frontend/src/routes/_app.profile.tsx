import { createFileRoute } from "@tanstack/react-router";
import { ProfilePanel } from "@/components/app/profile-panel";

export { TahlilAppProfileCard } from "@/components/app/profile-panel";

export const Route = createFileRoute("/_app/profile")({
  head: () => ({ meta: [{ title: "پروفایل حساب — ساپیو - دستیار هوشمند بازار" }] }),
  component: ProfilePage,
});

function ProfilePage() {
  return <ProfilePanel />;
}
