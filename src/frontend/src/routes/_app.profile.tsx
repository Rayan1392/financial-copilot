import { createFileRoute } from "@tanstack/react-router";
import { ProfilePanel } from "@/components/app/profile-panel";
import { PurchaseHistory } from "@/components/app/purchase-history";
import { CreditSummary } from "@/components/mobile/credit-summary";

export { TahlilAppProfileCard } from "@/components/app/profile-panel";

export const Route = createFileRoute("/_app/profile")({
  head: () => ({ meta: [{ title: "پروفایل حساب — ساپیو - دستیار هوشمند بازار" }] }),
  component: ProfilePage,
});

function ProfilePage() {
  // Plan, remaining credit, expiry and purchase guidance (CreditSummary, shared with mobile) plus purchases.
  return (
    <ProfilePanel
      subscription={
        <>
          <CreditSummary />
          <PurchaseHistory />
        </>
      }
    />
  );
}
